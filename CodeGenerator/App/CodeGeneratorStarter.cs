using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeIntelligence;
using DomainDiscovery;
using DomainDiscovery.Wikipedia.Dump;
using NaturalLanguageBridge;
using System.Text.Json;

namespace CodeGenerator.App;

/// <summary>
/// The main application class for the Code Generator console app.
/// </summary>
internal class CodeGeneratorStarter
{
    // workspace root (resolved once)
    private string _workspaceRoot = string.Empty;

    private List<PropertyRule> _configuredRules = [];

    /// <summary>How many projects the workspace scan found, for <c>status</c> to report.</summary>
    private int _projectCount;

    private readonly List<string> _commandHistory = [];
    private readonly string _historyPath;
    private const string HISTORY_FILENAME = ".codegen_history";

    private readonly NaturalLanguagePipeline _pipeline = new();
    private readonly BuildAppCommand _buildApp = new();
    private readonly BatchCommand _batch = new();
    private readonly AiLayer _ai = AiLayer.Create();

    /// <summary>The input currently being handled, so the confirm callback can record
    /// it in the decision log. The callback is invoked from deep inside the pipeline
    /// and is handed only the resolution, not the text it came from.</summary>
    private string _lastInput = string.Empty;

    internal CodeGeneratorStarter()
    {
        _workspaceRoot = ResolveWorkspaceRoot();

        _historyPath = Path.Combine(_workspaceRoot, HISTORY_FILENAME);

        LoadConfiguredRules();

        ProjectManager.LoadProject();

        // At startup, prefer an existing project in the workspace.
        InitializeExistingProjects();

        // The registry file is global; drop anything not in this workspace so
        // projects from other workspaces don't leak into list/use/generate.
        ProjectManager.RetainOnlyWithin(_workspaceRoot);

        // Load per-workspace learned intent memory. Consult/capture honor
        // CG_NO_LEARN, but this init still loads the file regardless.
        LearnedIntentStore.Initialize(_workspaceRoot);

        LoadCommandHistory();
    }

    /// <summary>
    /// Resolves the workspace root directory.
    /// FIX #4: Proper environment variable handling
    /// 1. Check PROJECTPATH environment variable
    /// 2. If not set or empty, use current directory
    /// </summary>
    private static string ResolveWorkspaceRoot()
    {
        var projectPath = Environment.GetEnvironmentVariable("PROJECTPATH");

        // When launched from Visual Studio / dotnet run the current directory is
        // the build-output folder (bin\Debug\...). PathHelper climbs out of it to
        // the real project/solution root so generated classes are saved somewhere
        // visible instead of being buried under bin.
        var resolved = PathHelper.ResolveWorkspaceRoot(
            projectPath, Directory.GetCurrentDirectory());

        //Console.WriteLine($"Workspace: {resolved}");
        return resolved;
    }

    /// <summary>
    /// Executes a single command non-interactively (no key-by-key editor) and
    /// returns. Used for scripting/automation, e.g. `CodeGenerator "create ..."`.
    /// Goes through the exact same pipeline as the interactive loop.
    /// </summary>
    internal bool RunOnce(string command)
    {
        IntentRepository.Initialize();

        ProjectMetaData? currentProject = null;

        try
        {
            if (BuildAppCommand.IsBuildApp(command))
            {
                _buildApp.Handle(command, _workspaceRoot);
                return true;
            }

            // Intercepted here too, so a scripted `help build app` gets the same answer as a
            // typed one rather than falling through to the NLP path.
            if (HelpCommand.IsHelp(command))
            {
                Console.WriteLine(HelpCommand.Render(command));
                return true;
            }

            if (StatusCommand.IsStatus(command))
            {
                PrintStatus();
                return true;
            }

            // No resolver and no confirm delegate: scripted single-shot invocations
            // stay rule-based only. _lastInput is set anyway so that if RunOnce is ever
            // given a confirm callback, the decision log records the right text rather
            // than an empty string.
            _lastInput = command;
            var outcome = _pipeline.Handle(command, ref currentProject,
                _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r);

            // Only offer the "did you mean" help for input we could not recognize.
            // A recognized command that failed (e.g. "No project selected") has
            // already reported its own reason; calling it unrecognized is wrong.
            if (!outcome.Recognized && !string.IsNullOrWhiteSpace(command))
            {
                Console.WriteLine(
                    UnrecognizedCommand.Describe(command, CommandSuggester.Closest(command)));
            }

            return outcome.Handled;
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
            return false;
        }
    }

    /// <summary>Prompts for an AI reading and records the answer. The decision log is
    /// written whichever way the user answers — a decline is as informative as an
    /// accept for calibrating a later auto-execute threshold.</summary>
    private bool Confirm(CodeIntelligence.Abstractions.AiResolution resolution)
        => AiDecisionRecorder.ConfirmAndRecord(
            resolution, _lastInput, _ai.DecisionLog, AiConfirmPrompt.Confirm);

    internal void Run()
    {
        PrintHeader();

        IntentRepository.Initialize();

        ProjectMetaData? currentProject = null;

        while (true)
        {
            Console.Write("CodeGenerator >> ");

            var editor = new CommandLineEditor(
                _commandHistory, _historyPath);


            var input = editor.ReadCommand();

            //if (string.IsNullOrWhiteSpace(input))
            //    continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (ClearCommand.IsClear(input))
            {
                // Screen-only wipe, then redraw the header so it stays put (the
                // store-status report is NOT re-printed). Skipped when output
                // is redirected because Console.Clear() throws IOException with no
                // console attached, and clearing a piped transcript is meaningless.
                // History (in-memory list and .codegen_history) stays intact:
                // CommandLineEditor never records clear/cls (see
                // ShouldRecordInHistory), so ↑ still recalls the previous command.
                if (!Console.IsOutputRedirected)
                {
                    Console.Clear();
                    PrintBanner();
                }
                continue;
            }

            if (HistoryCommand.IsHistory(input))
            {
                Console.WriteLine(HistoryView.Format(_commandHistory, 20));
                continue;
            }

            // Ahead of the pipeline: LiteralCommandTable keys on the exact word "help", so
            // `help build app` matches nothing there. See HelpCommand.
            if (HelpCommand.IsHelp(input))
            {
                Console.WriteLine(HelpCommand.Render(input));
                continue;
            }

            if (StatusCommand.IsStatus(input))
            {
                PrintStatus();
                continue;
            }

            if (AiCommand.IsAiCommand(input))
            {
                Console.WriteLine(AiCommand.Handle(input, _ai));
                continue;
            }

            // FIX #9: Improved exception handling with specific catch blocks
            try
            {
                if (BuildAppCommand.IsBuildApp(input))
                {
                    _buildApp.Handle(input, _workspaceRoot, _ai);
                    continue;
                }

                if (BatchCommand.IsBatch(input))
                {
                    _batch.Handle(ref currentProject, _configuredRules, _workspaceRoot,
                        r => ProjectManager.CurrentProject = r, _pipeline);
                    continue;
                }

                // NLE analyzes first, then CodeGenerator generates from the result.
                // The NLE analysis is used internally only; we surface just the
                // command's own output (e.g. the project list), not the raw
                // SemanticResult, so no onAnalyzed callback is passed.
                // Console.IsInputRedirected is the gate: a piped or scripted run must
                // never block on a prompt, which also keeps scripted E2E runs
                // deterministic and model-free.
                _lastInput = input;
                var outcome = _pipeline.Handle(input, ref currentProject,
                     _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r,
                     onAnalyzed: null,
                     aiResolver: Console.IsInputRedirected ? null : _ai.Resolver,
                     confirmAi: Console.IsInputRedirected ? null : Confirm);

                // See RunOnce: gate on recognition, not success, so a recognized
                // command that failed keeps its own error and isn't mislabeled.
                if (!outcome.Recognized && !string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine(
                        UnrecognizedCommand.Describe(input, CommandSuggester.Closest(input)));
                }
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid argument: {ex.Message}");
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"File not found: {ex.Message}");
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine($"Directory not found: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Invalid operation: {ex.Message}");
            }
            catch (Exception ex)
            {
                ReportUnexpected(ex);
            }
        }
    }

    /// <summary>
    /// Reports a failure nothing else handled.
    /// </summary>
    /// <remarks>
    /// The exception's type name used to be printed here. It tells a user nothing they can act on
    /// and makes an ordinary problem read like a crash, so it — and the stack — now sit behind
    /// <c>CG_VERBOSE</c>. That also replaces the old "stack trace only when a debugger is
    /// attached", which was no help at all to the person actually hitting the failure.
    /// </remarks>
    private static void ReportUnexpected(Exception exception)
    {
        foreach (var line in ErrorText.Lines(exception, ErrorText.Verbose))
            Output.Error(line);
    }

    private void PrintHeader()
    {
        PrintBanner();

        ReportWikipediaStoreStatus();

        // Only when it is a problem. "AI: enabled" and a deliberate "off (disabled)" are both
        // states the user chose, and `status` reports them; an Unavailable reason means AI was
        // meant to be on and could not load, which would otherwise make every unrecognized
        // command silently behave as though it were switched off.
        //
        // Deliberately NOT in PrintBanner: that method is static and, more to the point, `clear`
        // re-runs it, which would repaint this on every screen wipe.
        if (_ai.Unavailable is { } reason)
            Output.Warning($"AI: off ({reason}) — 'status' for detail.");
    }

    /// <summary>
    /// Writes the title and the few hints worth having in front of a new user.
    /// </summary>
    /// <remarks>
    /// Kept separate from the one-time startup work in <see cref="PrintHeader"/> so <c>clear</c>
    /// can redraw it without re-triggering that work.
    /// <para>
    /// Short on purpose. This used to be nine lines of box art and hints, followed by the store
    /// report and the AI line — thirteen in the healthy case, which pushed the prompt and the
    /// command just recommended off a short terminal, and taught the eye to skim exactly the
    /// block that carries the store's warnings. What is left either says what to type or is a
    /// problem.
    /// </para>
    /// </remarks>
    private static void PrintBanner()
    {
        Console.WriteLine($"Smart Code Generator {VersionInfo.Current}");
        Console.WriteLine("Type 'help' for commands, 'status' for system state, 'exit' to quit.");
        Console.WriteLine("TAB completes commands; 'batch' runs several at once.");
        Console.WriteLine();
    }

    /// <summary>
    /// Reports a missing store loudly, or reports what the store holds and what the updater service
    /// is doing. This method no longer <i>performs</i> any update: the Windows Service
    /// <c>SmartCodeGenWikipediaUpdater</c> is the sole owner of that work, so the CLI never competes
    /// with it for the store and never spends 780 MB a day of the user's connection unasked.
    /// </summary>
    private void ReportWikipediaStoreStatus()
    {
        var (lines, healthy) = StoreReport();

        // Silence means healthy. A line printed at every launch gets skimmed, and this is the very
        // block that carries the store's warnings — so it earns its place on screen only when it
        // has something wrong to say. `status` has the detail either way.
        if (healthy)
            return;

        // Warning, not error: none of these stop the tool working — a missing store falls back to
        // the online API — but every one of them is something the user needs to know.
        foreach (var line in lines)
            Output.Warning(line);

        Console.WriteLine();
    }

    /// <summary>
    /// The store report, and whether it is well enough to keep to itself.
    /// </summary>
    /// <remarks>
    /// One place, used by both the startup warning and <c>status</c>, so the two cannot disagree
    /// about what the store is doing.
    /// </remarks>
    private (IReadOnlyList<string> Lines, bool Healthy) StoreReport()
    {
        var databasePath = DomainDiscoveryPipeline.DefaultDumpDatabasePath;

        if (!File.Exists(databasePath))
        {
            // The store lives in a git-ignored directory, so an external clean, a disk-cleanup tool or
            // a manual delete removes it, and the app would otherwise slip to the online API without a
            // word. Say so, loudly — but do not stop. A fresh clone that never imported the store
            // depends on exactly that fallback to run 'build app:' at all.
            return
            ([
                $"Wikipedia store: NOT FOUND at {databasePath}.",
                "  'build app:' will fall back to the online Wikipedia API — slower, rate-limited, and",
                "  it needs a connection. If you had already imported the store, it has been moved or",
                "  deleted. To (re)build it locally (a one-time import), run:",
                string.Empty,
                "      dotnet run --project WikipediaDumpImporter -c Release",
            ], false);
        }

        int? storeDate;
        try
        {
            storeDate = new WikipediaStore(databasePath).GetDumpDate();
        }
        catch
        {
            // A store that cannot answer is still usable for reads; do not let a status line stop the app.
            storeDate = null;
        }

        var status = new StoreUpdateStatusFile(
            StoreUpdateStatusFile.BesideStore(databasePath)).Read();

        var service = UpdaterServicePresence.Detect();
        var now = DateTimeOffset.UtcNow;

        return (
            StoreStatusReport.Lines(
                storeDate, status, service, @".\tools\install-updater-service.ps1", now),
            StoreStatusReport.IsHealthy(storeDate, status, service, now));
    }

    /// <summary>Everything startup no longer prints, on demand.</summary>
    private void PrintStatus()
    {
        foreach (var line in StatusReport.Lines(
            _workspaceRoot,
            _projectCount,
            ProjectManager.CurrentProject?.ProjectName,
            _configuredRules.Count,
            StoreReport().Lines,
            AiState(),
            VersionInfo.Current))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
    }

    /// <summary>How the AI layer would describe itself.</summary>
    private string AiState()
        => _ai.Resolver is not null
            ? "enabled (retrieval)"
            : $"off ({_ai.Unavailable ?? "disabled"})";

    private void LoadConfiguredRules()
    {
        var rulesJsonPath = Path.Combine(_workspaceRoot, "rules.json");
        var rulesTxtPath = Path.Combine(_workspaceRoot, "rules.txt");

        try
        {
            if (File.Exists(rulesJsonPath))
            {
                var json = File.ReadAllText(rulesJsonPath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<List<PropertyRule>>(json, opts);
                if (loaded != null)
                    _configuredRules = loaded;
                Console.WriteLine($"Loaded {_configuredRules.Count} property rules from rules.json");
            }
            else if (File.Exists(rulesTxtPath))
            {
                var lines = File.ReadAllLines(rulesTxtPath).Where(l => !string.IsNullOrWhiteSpace(l));
                _configuredRules = RuleParser.Parse(lines).ToList();
                Console.WriteLine($"Parsed {_configuredRules.Count} property rules from rules.txt");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load rules: {ex.Message}");
            _configuredRules = new List<PropertyRule>();
        }
    }

    private void InitializeExistingProjects()
    {
        try
        {
            var projects = Directory.GetFiles(_workspaceRoot, "*.csproj",
                SearchOption.AllDirectories);

            foreach (var project in projects)
            {
                var projectDir = Path.GetDirectoryName(project);
                ProjectManager.Register(new ProjectMetaData
                {
                    Success = true,
                    ProjectPath = projectDir ?? Path.GetDirectoryName(_workspaceRoot) ?? _workspaceRoot,
                    CsProjPath = project,
                    ProjectName = Path.GetFileNameWithoutExtension(project),
                    Message = "Loaded"
                });
            }

            _projectCount = projects.Length;

            // Nothing at all when there are none, rather than the empty WriteLine this used to
            // emit — a stray blank line above the banner on every first run.
            if (projects.Length > 0)
            {
                Console.WriteLine(
                    $"Found {projects.Length} project(s), "
                    + $"{ProjectManager.CurrentProject?.ProjectName} selected.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to initialize existing projects: {ex.Message}");
        }
    }

    private void LoadCommandHistory()
    {
        _commandHistory.Clear();
        _commandHistory.AddRange(CommandHistoryFile.ReadLines(_historyPath));
    }
}

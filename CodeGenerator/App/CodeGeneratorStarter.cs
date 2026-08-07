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
            Console.WriteLine($"Error [{ex.GetType().Name}]: {ex.Message}");
            return false;
        }
    }

    /// <summary>Prompts for an AI reading and records the answer. The decision log is
    /// written whichever way the user answers — a decline is as informative as an
    /// accept for calibrating a later auto-execute threshold.</summary>
    private bool Confirm(CodeIntelligence.Abstractions.AiResolution resolution)
    {
        var accepted = AiConfirmPrompt.Confirm(resolution);
        _ai.DecisionLog.Append(_lastInput, resolution, accepted);
        return accepted;
    }

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
                    _buildApp.Handle(input, _workspaceRoot);
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
                Console.WriteLine($"Unexpected error [{ex.GetType().Name}]: {ex.Message}");

                // Include stack trace only in debug mode
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }
    }

    private void PrintHeader()
    {
        PrintBanner();

        ReportWikipediaStoreStatus();

        // Reported once per session, beside the updater-service line, so a missing or
        // unloadable model is stated plainly at startup instead of silently making
        // every unrecognized command behave as though AI were switched off.
        //
        // Deliberately NOT in PrintBanner: that method is static and, more to the
        // point, `clear` re-runs it, which would repaint this line on every screen
        // wipe. PrintHeader is the once-per-session path.
        Console.WriteLine(_ai.Resolver is not null
            ? "AI: enabled (retrieval)"
            : $"AI: off ({_ai.Unavailable ?? "disabled"})");
    }

    /// <summary>
    /// Writes the title banner and command hints. Kept separate from the one-time
    /// startup work in <see cref="PrintHeader"/> (e.g. reporting the Wikipedia store's
    /// status) so <c>clear</c> can redraw the header after wiping the screen without
    /// re-triggering that work.
    /// </summary>
    private static void PrintBanner()
    {
        Console.WriteLine("============================================================");
        Console.WriteLine("               Code Generation Engine");
        Console.WriteLine("============================================================");
        Console.WriteLine();

        Console.WriteLine("Type 'help' to see the complete command reference.");
        Console.WriteLine("Press TAB for command suggestions.");
        Console.WriteLine("Type 'batch' to enter several commands and review them before they run.");
        Console.WriteLine("Type 'exit' to quit the application.");
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
        var databasePath = DomainDiscoveryPipeline.DefaultDumpDatabasePath;

        if (!File.Exists(databasePath))
        {
            // The store lives in a git-ignored directory, so an external clean, a disk-cleanup tool or
            // a manual delete removes it, and the app would otherwise slip to the online API without a
            // word. Say so, loudly — but do not stop. A fresh clone that never imported the store
            // depends on exactly that fallback to run 'build app:' at all.
            Console.WriteLine($"Wikipedia store: NOT FOUND at {databasePath}.");
            Console.WriteLine(
                "  'build app:' will fall back to the online Wikipedia API — slower, rate-limited, and");
            Console.WriteLine(
                "  it needs a connection. If you had already imported the store, it has been moved or");
            Console.WriteLine("  deleted. To (re)build it locally (a one-time import), run:");
            Console.WriteLine();
            Console.WriteLine("      dotnet run --project WikipediaDumpImporter -c Release");
            Console.WriteLine();
            return;
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

        foreach (var line in StoreStatusReport.Lines(
            storeDate, status, UpdaterServicePresence.Detect(),
            @".\tools\install-updater-service.ps1", DateTimeOffset.UtcNow))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
    }

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

            Console.WriteLine(projects.Length > 0
                ? $"Found {projects.Length} project(s) and {ProjectManager.CurrentProject?.ProjectName} project is selected." : "");
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

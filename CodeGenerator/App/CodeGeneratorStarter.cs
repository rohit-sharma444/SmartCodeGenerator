using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
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

        Console.WriteLine($"Workspace: {resolved}");
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

            var outcome = _pipeline.Handle(command, ref currentProject,
                _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r);
            return outcome.Handled;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error [{ex.GetType().Name}]: {ex.Message}");
            return false;
        }
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
                _pipeline.Handle(input, ref currentProject,
                     _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r);
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
        Console.WriteLine("============================================================");
        Console.WriteLine("               Code Generation Engine");
        Console.WriteLine("============================================================");
        Console.WriteLine();

        Console.WriteLine("Type 'help' to see the complete command reference.");
        Console.WriteLine("Press TAB for command suggestions.");
        Console.WriteLine("Type 'batch' to enter several commands and review them before they run.");
        Console.WriteLine("Type 'exit' to quit the application.");
        Console.WriteLine();

        ReportWikipediaStore();
    }

    /// <summary>
    /// Reports the local Wikipedia store's state at startup — missing, up to date, or how far behind
    /// the encyclopedia it has fallen — and, when it is merely behind, brings it forward. A missing
    /// store is a loud warning, not an error: the app still starts on the online-API fallback.
    /// </summary>
    /// <remarks>
    /// <b>This costs gigabytes and hours, so it says so, and it can always be escaped.</b> Wikimedia's
    /// daily increments run to ~780 MB each and a full dump is cut only twice a month, so a store is
    /// routinely a fortnight behind and owes some 10 GB. That is why the size, the day count and the
    /// progress of every day are on screen, and why Ctrl-C skips straight to the prompt: the days
    /// already applied are kept, and the next start resumes at the one that was interrupted.
    /// <para>
    /// <b>Nothing here may stop the application from starting.</b> Being offline is the NORMAL case
    /// for an app whose whole point is a local dump, so an unanswered check runs nothing, an
    /// interrupted update is not an error, and a failed one leaves the store exactly as stale as it
    /// already was. In every case the user gets their prompt.
    /// </para>
    /// </remarks>
    private static void ReportWikipediaStore()
    {
        var databasePath = DomainDiscoveryPipeline.DefaultDumpDatabasePath;
        if (!File.Exists(databasePath))
        {
            // The store lives in a git-ignored directory, so nothing here can protect it: an external
            // clean, a disk-cleanup tool or a manual delete removes it, and the app would otherwise
            // slip to the online API without a word. Say so, loudly — but do not stop. A fresh clone
            // that never imported the store depends on exactly that fallback to run `build app:` at all.
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

        var store = new WikipediaStore(databasePath);
        var staleness = new DumpFreshness().Check(store);

        if (staleness.StoreDate is not { } date)
            return;

        var cut = $"{date / 10000}-{date / 100 % 100:00}-{date % 100:00}";

        if (staleness.IsCurrent)
        {
            Console.WriteLine($"Wikipedia store: cut {cut}, up to date.");
            Console.WriteLine();
            return;
        }

        if (!staleness.IsStale)
        {
            // Wikimedia did not answer. Say so — never let silence pass for a clean bill of health.
            Console.WriteLine($"Wikipedia store: cut {cut} (could not check for updates).");
            Console.WriteLine();
            return;
        }

        Console.WriteLine(
            $"Wikipedia store: cut {cut} — {staleness.PendingDays.Count} daily update(s) published " +
            $"since (~{staleness.ApproximateGigabytes:F1} GB to download).");
        Console.WriteLine("Applying them now. Press Ctrl-C to skip and go straight to the prompt —");
        Console.WriteLine("every day that has fully landed is kept, and the next start resumes here.");
        Console.WriteLine();

        var result = ApplyPendingDays(store, databasePath, staleness);

        Console.WriteLine();

        switch (result.Outcome)
        {
            case StartupUpdateOutcome.Completed:
                Console.WriteLine($"Wikipedia store: up to date ({result.DaysApplied} day(s) applied).");
                break;

            case StartupUpdateOutcome.Skipped:
                Console.WriteLine(
                    $"Update skipped. {result.DaysApplied} day(s) applied and kept; the store is at " +
                    $"{store.GetDumpDate()}.");
                break;

            case StartupUpdateOutcome.Failed:
                Console.WriteLine(
                    $"Update stopped: {result.Failure} — {result.DaysApplied} day(s) applied and " +
                    $"kept; the store is at {store.GetDumpDate()}. Discovery still works: it is only " +
                    "as stale as it was.");
                break;
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Runs the pending days, with Ctrl-C wired to CANCEL the update rather than kill the process —
    /// the user pressing it wants their prompt, not the end of their application.
    /// </summary>
    private static StartupUpdateResult ApplyPendingDays(
        WikipediaStore store, string databasePath, DumpStaleness staleness)
    {
        var working = Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "incr");
        var updater = new IncrementalUpdater(store);

        using var interrupt = new CancellationTokenSource();

        void Skip(object? sender, ConsoleCancelEventArgs e)
        {
            // Without this the runtime tears the process down and the user loses the app they asked
            // for, having merely declined a download.
            e.Cancel = true;
            interrupt.Cancel();

            Console.WriteLine();
            Console.WriteLine("Skipping the rest of the update...");
        }

        Console.CancelKeyPress += Skip;

        try
        {
            return new StartupDumpUpdate().Run(
                staleness,
                (day, cancellation) => updater.UpdateDay(day, working, cancellation),
                interrupt.Token);
        }
        finally
        {
            // Ctrl-C means Ctrl-C again from here on.
            Console.CancelKeyPress -= Skip;
        }
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

            // Do NOT auto-select a project at startup. A leftover selection from
            // the persisted registry (or the first project that happened to be
            // registered) would silently capture every generated class and hide
            // it in an unexpected folder. New classes go to the workspace until
            // the user explicitly runs "use project X".
            ProjectManager.CurrentProject = null;

            Console.WriteLine(projects.Length > 0
                ? $"Found {projects.Length} project(s). No project selected — " +
                  "use 'use project <name>' to target one, or new classes save to the workspace."
                : "No projects found. New classes save to the workspace.");
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

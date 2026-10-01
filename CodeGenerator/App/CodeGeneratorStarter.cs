using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeGenerator.App.Relationships;
using CodeGenerator.App.Scripts;
using CodeIntelligence;
using DomainDiscovery;
using DomainDiscovery.Wikipedia.Dump;
using NaturalLanguageBridge;

namespace CodeGenerator.App;

/// <summary>
/// The main application class for the Code Generator console app.
/// </summary>
internal class CodeGeneratorStarter
{
    /// <summary>
    /// Where the tool is working: the root, the rules read from it, and the projects under it.
    /// </summary>
    /// <remarks>
    /// One object rather than the four loose fields this used to keep, because the chat page can
    /// re-root at runtime and the REPL has to read whatever it left behind.
    /// </remarks>
    private readonly Workspace _workspace;

    /// <summary>
    /// Whether <c>PROJECTPATH</c> chose the workspace root. Only used to word the
    /// does-not-exist notice: naming the variable helps when it is set and misleads when it is not.
    /// </summary>
    private readonly bool _projectPathSet =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECTPATH"));

    private readonly List<string> _commandHistory = [];

    private readonly NaturalLanguagePipeline _pipeline = new();
    private readonly BuildAppCommand _buildApp = new();
    private readonly BatchCommand _batch = new();
    private readonly ChatCommand _chat = new();
    private readonly ShowRelationshipsCommand _showRelationships = new();
    private readonly AiLayer _ai = AiLayer.Create();

    /// <summary>The input currently being handled, so the confirm callback can record
    /// it in the decision log. The callback is invoked from deep inside the pipeline
    /// and is handed only the resolution, not the text it came from.</summary>
    private string _lastInput = string.Empty;

    /// <summary>
    /// The relationship conversation for this session, or null when input is redirected.
    /// </summary>
    /// <remarks>
    /// Built once, because what it has already offered has to be remembered across commands — a
    /// candidate the user has refused must not come back after every command for the rest of the
    /// session. It reads the workspace root through a function rather than being handed one, since
    /// <c>load</c> and <c>use project</c> re-root the workspace under it.
    /// </remarks>
    private readonly RelationshipFlow? _relationships;

    internal CodeGeneratorStarter()
    {
        _workspace = new Workspace(ResolveWorkspaceRoot(), _projectPathSet);

        ProjectManager.LoadProject();

        // At startup, prefer an existing project in the workspace.
        InitializeExistingProjects();

        // The registry file is global; drop anything not in this workspace so
        // projects from other workspaces don't leak into list/use/generate.
        ProjectManager.RetainOnlyWithin(_workspace.Root);

        // Load per-workspace learned intent memory. Consult/capture honor
        // CG_NO_LEARN, but this init still loads the file regardless.
        LearnedIntentStore.Initialize(_workspace.Root);

        // No asker when input is redirected, and no asker means no flow: a piped or scripted run
        // must never be asked a question, or the script's next line is swallowed as the answer to
        // a menu it could not see.
        _relationships = RelationshipGate.For(
            () => _workspace.Root,
            Console.IsInputRedirected ? null : new ConsoleChoiceAsker(),
            Output.Line);

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

        var kind = CommandRoute.Classify(command);

        try
        {
            if (kind == CommandKind.BuildApp)
            {
                _buildApp.Handle(command, _workspace.Root);
                return true;
            }

            // Unlike chat, this has an answer that fits in a console, so a scripted run gets
            // it as lines instead of being turned away.
            if (kind == CommandKind.ShowRelationships)
            {
                _showRelationships.Handle(_workspace.Root);
                return true;
            }

            // A scripted single-shot run has no browser and nobody to type into one.
            if (kind == CommandKind.Chat)
            {
                Output.Warning("`chat` needs an interactive console. Start the tool and type it.");
                return false;
            }

            // Intercepted here too, so a scripted `help build app` gets the same answer as a
            // typed one rather than falling through to the NLP path.
            if (kind == CommandKind.Help)
            {
                Console.WriteLine(HelpCommand.Render(command));
                return true;
            }

            if (kind == CommandKind.Status)
            {
                PrintStatus();
                return true;
            }

            // One command and out: no session to save, and replaying one is a session's job.
            if (kind is CommandKind.Run or CommandKind.SaveSession)
            {
                Output.Warning("`run` and `save session` need a session. Start the tool and type them.");
                return false;
            }

            // No resolver and no confirm delegate: scripted single-shot invocations
            // stay rule-based only. _lastInput is set anyway so that if RunOnce is ever
            // given a confirm callback, the decision log records the right text rather
            // than an empty string.
            _lastInput = command;

            PipelineOutcome outcome;

            // The single-shot form goes quiet for the same seconds the typed one does, and has the
            // same nothing to show for it.
            using (Spinner.StartAfter(Spinner.WorkingLabel, Spinner.Threshold))
            {
                outcome = _pipeline.Handle(command, ref currentProject,
                    _workspace.Rules, _workspace.Root, r => ProjectManager.CurrentProject = r);
            }

            // Only offer the "did you mean" help for input we could not recognize.
            // A recognized command that failed (e.g. "No project selected") has
            // already reported its own reason; calling it unrecognized is wrong.
            if (!outcome.Recognized && !string.IsNullOrWhiteSpace(command))
            {
                // A warning, not an error: nothing failed, the user simply typed something the
                // tool does not know. Red here would read as a fault in the tool.
                Output.Warning(
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

        // This session's undo history. Deleted, shadow copy and all, when the loop ends. None in a
        // copy running a script's build: nobody can undo there, and copying the workspace would
        // only cost time — the parent already holds a snapshot.
        var journal = ScriptBuildApp.IsStep
            ? UndoJournal.Disabled(_workspace.Root)
            : UndoJournal.ForSession(_workspace.Root, Output.Warning);
        var recorder = new SessionRecorder(journal);

        while (true)
        {
            // Cleared as the prompt takes over, so a command cancelled with Ctrl-C does not leave
            // the next one starting on an already-cancelled token.
            Interrupt.Reset();

            Console.Write("CodeGenerator >> ");

            var editor = new CommandLineEditor(
                _commandHistory, _workspace.HistoryPath);


            var input = editor.ReadCommand();

            //if (string.IsNullOrWhiteSpace(input))
            //    continue;

            var kind = CommandRoute.Classify(input);

            if (kind == CommandKind.Exit)
                break;

            if (kind == CommandKind.Clear)
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

            if (kind == CommandKind.History)
            {
                Console.WriteLine(HistoryView.Format(_commandHistory, 20));
                continue;
            }

            // Ahead of the pipeline: LiteralCommandTable keys on the exact word "help", so
            // `help build app` matches nothing there. See HelpCommand.
            if (kind == CommandKind.Help)
            {
                // Paged: `help all` is around 250 lines, which otherwise scrolls the question and
                // the answer's first half off the top of the screen.
                Pager.Write(HelpCommand.Render(input).Split(Environment.NewLine));
                continue;
            }

            if (kind == CommandKind.Status)
            {
                PrintStatus();
                continue;
            }

            // A chat page opened from here shares this Workspace and can load another folder into
            // it; a history for the old folder must not write into the new one.
            if (!string.Equals(journal.WorkspaceRoot, Path.GetFullPath(_workspace.Root), StringComparison.OrdinalIgnoreCase))
            {
                journal.Dispose();
                journal = UndoJournal.ForSession(_workspace.Root, Output.Warning);
                recorder = new SessionRecorder(journal);
            }

            // Not journaled: the file is what was asked for, and undoing a save would delete it.
            if (kind == CommandKind.SaveSession)
            {
                var saved = SaveSessionCommand.Handle(input, _workspace.Root, recorder, DateTime.Now);
                if (saved.Saved)
                    Output.Success(saved.Message);
                else
                    Output.Warning(saved.Message);
                continue;
            }

            if (kind is CommandKind.Undo or CommandKind.Redo)
            {
                var result = UndoCommand.Handle(journal, kind);
                if (result.Done)
                    Console.WriteLine(result.Message);
                else
                    Output.Warning(result.Message);

                // The loop keeps its own current project; the journal restored ProjectManager's.
                currentProject = ProjectManager.CurrentProject;
                continue;
            }

            // One entry for everything this pass does, however it ends: a `continue` inside the
            // try below still disposes it. Null for the kinds that change nothing.
            using var entry = UndoCommand.Journaled(kind) ? journal.Begin(input) : null;

            if (kind == CommandKind.Demo)
            {
                RunDemo(ref currentProject);
                entry?.Keep([ScriptNotes.Demo]);
                continue;
            }

            if (kind == CommandKind.Ai)
            {
                Console.WriteLine(AiCommand.Handle(input, _ai));
                continue;
            }

            // Not buffered, and deliberately not inside CommandColour.Run: this prints an address
            // and then blocks, and buffering it would print the address after the session it was
            // for had already ended.
            if (kind == CommandKind.Chat)
            {
                _chat.Handle(_workspace, _ai, _commandHistory);
                continue;
            }

            // FIX #9: Improved exception handling with specific catch blocks
            try
            {
                if (kind == CommandKind.BuildApp)
                {
                    _buildApp.Handle(input, _workspace.Root, _ai);
                    entry?.Keep([ScriptNotes.InteractiveBuildApp(input)]);
                    continue;
                }

                // Streams live for the same reason build app does: it prints an address and then
                // holds the prompt while a page is open, and CommandColour would buffer both.
                if (kind == CommandKind.ShowRelationships)
                {
                    _showRelationships.Handle(_workspace.Root);
                    continue;
                }

                if (kind == CommandKind.Batch)
                {
                    var applied = _batch.Handle(ref currentProject, _workspace.Rules, _workspace.Root,
                        r => ProjectManager.CurrentProject = r, _pipeline);
                    entry?.Keep(applied);
                    continue;
                }

                if (kind == CommandKind.Run)
                {
                    RunScript(input, entry, ref currentProject);
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

                // Everything this command prints is captured and then written back in one colour —
                // green if it worked, red if it failed, amber if the words were not understood. The
                // colour depends on the result, so the output cannot be coloured as it is produced.
                //
                // Only the pipeline commands go through here. `build app`, `batch` and `demo` are
                // intercepted above and stream live: they prompt mid-flight, and buffering would
                // hide the question the user is meant to answer.
                var handled = false;
                var neededAi = false;

                CommandColour.Run(() =>
                {
                    var outcome = _pipeline.Handle(input, ref currentProject,
                         _workspace.Rules, _workspace.Root, r => ProjectManager.CurrentProject = r,
                         onAnalyzed: null,
                         aiResolver: Console.IsInputRedirected ? null : _ai.Resolver,
                         confirmAi: Console.IsInputRedirected ? null : Confirm,

                         // Threaded through so the work that already accepts a token honours Ctrl-C.
                         // Plenty of what runs below here is synchronous and takes none — a press
                         // cannot interrupt those, which is exactly why a second press still quits.
                         ct: Interrupt.Token);

                    if (!outcome.Recognized && !string.IsNullOrWhiteSpace(input))
                    {
                        Console.WriteLine(
                            UnrecognizedCommand.Describe(input, CommandSuggester.Closest(input)));
                    }

                    // Gated on recognition, not success: a recognized command that failed has
                    // already reported its own reason, and calling it unrecognized would be wrong.
                    // Inside the buffered region so it is part of the coloured block.
                    handled = outcome.Handled;
                    neededAi = outcome.NeededAi;
                    return CommandColour.Verdict(outcome.Recognized, outcome.Handled);
                });

                // What save session writes: the line as typed when it worked — or a note, when only
                // the AI could read it, since a script never asks the AI.
                if (handled)
                    entry?.Keep([ScriptNotes.SavedForm(input, neededAi)]);

                // After the buffered block, never inside it: CommandColour redirects Console.Out
                // for the whole command, so a menu drawn in there is drawn into a buffer nobody
                // sees while ReadKey waits on a blank screen. Creating a class is skipped so that
                // a run of them is not interrupted; every other command is a moment the offer may
                // appear. Only the interactive loop calls this — RunOnce handles one command and
                // exits, so there is no session in which a refusal could be remembered, and it is
                // the path scripts use.
                if (!RelationshipGate.IsClassCreation(input))
                    _relationships?.Run();

                // In the same entry as the command, as the files it wrote are.
                entry?.Keep(_relationships?.TakeApplied() ?? []);
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

        // Only `exit` ends the loop: every command's exceptions are caught inside it.
        journal.Dispose();
    }

    /// <summary>
    /// <c>run &lt;file&gt;</c>: every line, or — at the first failure — none of them.
    /// </summary>
    /// <remarks>
    /// Lines stream live like <c>batch</c>'s, not through <c>CommandColour</c>: a build block runs
    /// for seconds, and its output belongs on screen as it comes. Lines run without the AI resolver
    /// or a confirm prompt, as <c>batch</c> and <c>RunOnce</c> do — a script must not stop to ask.
    /// </remarks>
    private void RunScript(string input, UndoScope? entry, ref ProjectMetaData? currentProject)
    {
        var load = RunCommand.Load(input, _workspace.Root);
        if (load.Steps is null)
        {
            Output.Warning(load.Problem!);
            return;
        }

        var project = currentProject;

        var outcome = ScriptRunner.Run(
            load.Name,
            load.Steps,
            _workspace.Root,
            step =>
            {
                if (step is BuildAppStep build)
                    return ScriptBuildApp.Run(build.Lines, _workspace.Root, Console.WriteLine);

                var text = ((CommandStep)step).Text;
                var result = _pipeline.Handle(text, ref project, _workspace.Rules, _workspace.Root,
                    r => ProjectManager.CurrentProject = r);

                if (!result.Recognized)
                    Output.Warning(UnrecognizedCommand.Describe(text, CommandSuggester.Closest(text)));

                return result.Handled;
            },
            Console.WriteLine,
            () => BatchCommand.ConfirmSnapshotSize(_workspace.Root),

            // The first Ctrl-C stops between lines and puts everything back; a second still quits.
            stopRequested: () => Interrupt.Requested);

        currentProject = outcome.Succeeded ? project : ProjectManager.CurrentProject;

        if (outcome.Succeeded)
        {
            entry?.Keep(outcome.Ran);
            Output.Success(outcome.Message);
        }
        else
        {
            Output.Error(outcome.Message);
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
        // the built-in domains and the online API — but every one of them is something the user
        // needs to know.
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
    internal static (IReadOnlyList<string> Lines, bool Healthy) StoreReport()
    {
        var config = DiscoveryConfig.Load();
        return StoreReport(WikipediaStoreLocation.Resolve(config), config.Source);
    }

    /// <summary>As <see cref="StoreReport()"/>, for a given store path and discovery source.</summary>
    internal static (IReadOnlyList<string> Lines, bool Healthy) StoreReport(string databasePath, string source)
    {
        if (!File.Exists(databasePath))
        {
            // What a missing store costs depends on discovery.json's Source. Pinned to Wikipedia,
            // this store is the direct path, so its absence is worth a warning at every launch.
            if (string.Equals(source, DiscoveryConfig.WikipediaSource, StringComparison.OrdinalIgnoreCase))
            {
                return
                ([
                    $"Wikipedia store: not installed ({databasePath}).",
                    "  discovery.json pins discovery to Wikipedia, so 'build app:' uses online Wikipedia",
                    "  (slower, rate-limited, needs a connection), then the built-in domains.",
                    "  To import the store (a one-time job), run:",
                    string.Empty,
                    "      dotnet run --project WikipediaDumpImporter -c Release",
                ], false);
            }

            // The normal state for anyone who installed the tool: silence at startup, one line in
            // `status`. Before the built-in domains this warned at every launch and claimed 'build
            // app:' would give zero classes, which the code never did (it went online instead).
            return ([
                $"Wikipedia store: not installed ({databasePath}) - 'build app:' uses Ollama, then the built-in domains, then online Wikipedia."
            ], true);
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

    /// <summary>
    /// Runs the demo walkthrough: real commands, through the same pipeline as anything typed.
    /// </summary>
    /// <remarks>
    /// It writes a project to disk, so it asks first and names the path before asking. Two things it
    /// deliberately will not do: run without a console to confirm on, and write into a folder that
    /// already exists — the same reasoning as the overwrite guard, since a demo clobbering someone's
    /// work would be the worst possible first impression.
    /// </remarks>
    private void RunDemo(ref ProjectMetaData? currentProject)
    {
        var target = Path.Combine(_workspace.Root, DemoScript.ProjectName);

        if (Console.IsInputRedirected)
        {
            // Nothing written, and the steps printed so a scripted reader still learns what the
            // tool does. Silently creating a project in a piped run would surprise every script.
            Output.Warning("`demo` creates a project, so it needs a console to confirm on.");
            Console.WriteLine("Run it at the prompt, or run these yourself:");

            foreach (var step in DemoScript.Steps)
                Console.WriteLine($"    {step.Command}");

            Console.WriteLine();
            return;
        }

        foreach (var line in DemoScript.Intro(_workspace.Root))
            Console.WriteLine(line);

        if (Directory.Exists(target))
        {
            Output.Warning($"{target} already exists — delete or rename it, then run demo again.");
            Console.WriteLine();
            return;
        }

        Console.Write("Run it? [y/N]: ");

        var answer = Console.ReadLine()?.Trim() ?? string.Empty;

        if (!answer.StartsWith('y') && !answer.StartsWith('Y'))
        {
            Console.WriteLine("Nothing was created.");
            Console.WriteLine();
            return;
        }

        foreach (var step in DemoScript.Steps)
        {
            Console.WriteLine();
            Output.Detail($"# {step.Narration}");

            // Echoed as though typed, so the user can see — and repeat — exactly what ran.
            Console.WriteLine($"CodeGenerator >> {step.Command}");

            _pipeline.Handle(step.Command, ref currentProject, _workspace.Rules, _workspace.Root,
                r => ProjectManager.CurrentProject = r, ct: Interrupt.Token);

            if (!Interrupt.Requested)
                continue;

            Output.Warning($"Cancelled. {target} was left as far as the demo got.");
            Console.WriteLine();
            return;
        }

        foreach (var line in DemoScript.Outro(target))
            Console.WriteLine(line);

        Console.WriteLine();
    }

    /// <summary>Everything startup no longer prints, on demand.</summary>
    private void PrintStatus()
    {
        foreach (var line in StatusReport.Lines(
            _workspace.Root,
            _workspace.ProjectCount,
            ProjectManager.CurrentProject?.ProjectName,
            _workspace.Rules.Count,
            StoreReport().Lines,
            AiState(_ai),
            VersionInfo.Current,
            Output.Explain()))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
    }

    /// <summary>How the AI layer would describe itself. Static and internal so the chat page's
    /// `status` reads the same words this one does rather than a second copy of them.</summary>
    internal static string AiState(AiLayer ai)
        => ai.Resolver is not null
            ? "enabled (retrieval)"
            : $"off ({ai.Unavailable ?? "disabled"})";

    private void InitializeExistingProjects()
    {
        // Checked before the scan rather than caught after it: a missing directory is an ordinary
        // situation with a clear cause, and letting Directory.GetFiles throw turned it into the
        // tool reporting its own method failing.
        var notice = WorkspaceNotice.Lines(_workspace.Root, _projectPathSet);

        if (notice.Count > 0)
        {
            foreach (var line in notice)
                Output.Warning(line);

            Console.WriteLine();
            return;
        }

        // The same call the chat page makes when it re-roots, so registering projects and loading
        // rules has one code path. Startup is simply the first load, at the root it already has.
        var load = _workspace.LoadFrom(_workspace.Root);

        if (!load.Ok)
        {
            // Still reachable for a real fault — permissions, a path too long, a disk error — so
            // it keeps a message, just not one phrased as an internal method name.
            Output.Warning($"Could not scan {_workspace.Root} for projects: {load.Problem}");
            return;
        }

        // Nothing at all when there are none, rather than the empty WriteLine this used to
        // emit — a stray blank line above the banner on every first run.
        if (load.ProjectCount > 0)
        {
            Console.WriteLine(
                $"Found {load.ProjectCount} project(s), "
                + $"{ProjectManager.CurrentProject?.ProjectName} selected.");
        }
    }

    private void LoadCommandHistory()
    {
        _commandHistory.Clear();
        _commandHistory.AddRange(CommandHistoryFile.ReadLines(_workspace.HistoryPath));
    }
}

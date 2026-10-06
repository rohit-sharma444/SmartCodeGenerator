using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeGenerator.App;
using CodeGenerator.App.Relationships;
using CodeGenerator.App.Scripts;
using CodeIntelligence;
using NaturalLanguageBridge;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// One conversation with the tool: the transcript, and the running of what is typed into it.
/// </summary>
/// <remarks>
/// A singleton per host, so two tabs looking at the same session see one conversation — the same
/// arrangement <c>BuildSession</c> uses, and for the same reason: a second tab must join the
/// session rather than start a rival one against the same workspace.
/// <para>
/// Commands run one at a time. <c>OutputCapture</c> swaps <c>Console.Out</c>, which is
/// process-global, and the engine keeps static state — so a second command starting while the
/// first is mid-flight would capture into the wrong buffer and act on a tree the first one is
/// changing. The page disables its composer in every tab while <see cref="Running"/> is true;
/// nothing is queued, because a queued command is aimed at a workspace that no longer exists by
/// the time it runs.
/// </para>
/// <para>
/// Every command goes through the same pipeline the console REPL uses, with the same
/// classification (<c>CommandRoute</c>) and the same verdict (<c>CommandColour.Verdict</c>).
/// Nothing here changes what a command prints.
/// </para>
/// </remarks>
public sealed class ChatSession
{
    private readonly Workspace _workspace;
    private readonly AiLayer _ai;
    private readonly List<string> _history;
    private readonly List<ChatTurn> _turns = [];
    private readonly NaturalLanguagePipeline _pipeline = new();
    private readonly Lock _gate = new();

    /// <summary>Carried between commands the way the REPL carries it: a `use project` in one
    /// message has to still be in effect in the next.</summary>
    private ProjectMetaData? _currentProject;

    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// The relationship conversation for this session, built on first use.
    /// </summary>
    /// <remarks>
    /// Lazily, because the asker needs this session and the session cannot hand out a reference to
    /// itself from a field initialiser. Built once all the same: what it has already offered has to
    /// be remembered across messages, or a candidate the user refused comes back after every one.
    /// </remarks>
    private RelationshipFlow? _relationships;

    /// <summary>This page's undo history — its own, not the console's. Replaced when another folder
    /// is loaded, so an undo can never write into the folder the page has left.</summary>
    private UndoJournal _journal;
    private SessionRecorder _recorder;

    // Whether the last pipeline command was read by the AI; set by Pipeline, read by TurnsFor.
    private bool _neededAi;

    public ChatSession(Workspace workspace, AiLayer ai, List<string> history)
    {
        _workspace = workspace;
        _ai = ai;
        _history = history;
        _journal = UndoJournal.ForSession(workspace.Root, Narrate);
        _recorder = new SessionRecorder(_journal);
    }

    public bool CanUndo => _journal.CanUndo;
    public bool CanRedo => _journal.CanRedo;
    public string? NextUndoLabel => _journal.NextUndoLabel;
    public string? NextRedoLabel => _journal.NextRedoLabel;

    public IReadOnlyList<ChatTurn> Turns => _turns;

    /// <summary>Whether a command is in flight. The composer is disabled while it is.</summary>
    public bool Running { get; private set; }

    /// <summary>Whether the session is over — `exit`, or every tab gone.</summary>
    public bool Ended { get; private set; }

    /// <summary>What is running, for the indicator to name.</summary>
    public string? InFlight { get; private set; }

    /// <summary>Raised whenever anything a tab draws has changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Whether the project tree is showing.
    /// </summary>
    /// <remarks>
    /// On the session rather than in the browser, because the browser cannot hold it: the host
    /// binds <c>127.0.0.1:0</c>, so the origin has a different port on every <c>chat</c> and
    /// anything kept in local storage belongs to a page that will never be served again. Here it
    /// survives a refresh, which is what it has to do; that both tabs then agree about it is the
    /// same bargain everything else on this page makes.
    /// </remarks>
    public bool TreeOpen { get; private set; } = true;

    public void ShowTree(bool open)
    {
        TreeOpen = open;
        Raise();
    }

    public string Root => _workspace.Root;

    /// <summary>The solution this workspace was loaded from, or empty when it was a folder.</summary>
    /// <remarks>
    /// Empty rather than null, because the tree takes it as a Razor parameter and a component
    /// parameter that may be null is one every reader has to check.
    /// </remarks>
    public string Solution => _workspace.SolutionPath ?? string.Empty;

    public int ProjectCount => _workspace.ProjectCount;

    public string? CurrentProject => ProjectManager.CurrentProject?.ProjectName;

    public int RuleCount => _workspace.Rules.Count;

    public string AiState => _ai.Resolver is not null ? "on" : "off";

    public IReadOnlyList<string> History => _history;

    private readonly Lock _questionGate = new();

    private TaskCompletionSource<int?>? _answer;

    /// <summary>The question every tab is showing, or null.</summary>
    public SessionQuestion? Question { get; private set; }

    /// <summary>
    /// Puts a question on every tab and blocks until one of them answers.
    /// </summary>
    /// <remarks>
    /// Called from inside the pipeline, on the worker thread, because that is how
    /// <c>confirmAi</c> is shaped: a synchronous <c>Func&lt;AiResolution, bool&gt;</c> invoked
    /// from deep inside the command. Blocking here is the point, since the command genuinely
    /// cannot continue until the answer arrives.
    /// <para>
    /// The one thing that must never happen is this thread waiting forever. A tab closed while the
    /// question is on screen ends the session (the host abandons it after its grace period), and
    /// <see cref="End"/> releases this with a cancellation. Without that, the command hangs and the
    /// console that started the chat hangs behind it.
    /// </para>
    /// <para>
    /// Null is that cancellation, and it is not an answer: an ended session, a closed tab or a
    /// dismissed question all produce it, and no caller may read it as a choice. That is why this
    /// returns an index rather than a bool — with two options, "false" is both "the user said No"
    /// and "nobody said anything", and the second must never write code.
    /// </para>
    /// </remarks>
    internal int? Ask(SessionQuestion question)
    {
        TaskCompletionSource<int?> pending;

        lock (_questionGate)
        {
            if (Ended)
                return null;

            pending = new TaskCompletionSource<int?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _answer = pending;
            Question = question;
        }

        Raise();

        var answered = pending.Task.GetAwaiter().GetResult();

        lock (_questionGate)
        {
            Question = null;
            _answer = null;
        }

        Raise();

        return answered;
    }

    /// <summary>Answers the question on screen. Does nothing if there is none.</summary>
    /// <remarks>Null is a cancellation — a closed tab, or a dismissed question — and is never
    /// treated as a choice by any caller.</remarks>
    public void Answer(int? index)
    {
        lock (_questionGate)
            _answer?.TrySetResult(index);
    }

    /// <summary>
    /// Runs one message and adds its turn.
    /// </summary>
    /// <remarks>
    /// The command itself is synchronous and some of it is slow, so it runs on a worker rather than
    /// on the circuit: a page that stops repainting while a command runs cannot show that a
    /// command is running.
    /// </remarks>
    public async Task Send(string input)
    {
        lock (_gate)
        {
            if (Running || Ended)
                return;

            Running = true;
            InFlight = input;
        }

        Raise();

        try
        {
            var kind = CommandRoute.Classify(input);

            switch (kind)
            {
                case CommandKind.Exit:
                    End();
                    return;

                case CommandKind.Clear:
                    ClearTranscript();
                    return;
            }

            Record(input);

            // One entry for the command and for anything the relationship offer after it writes.
            using var entry = UndoCommand.Journaled(kind) ? _journal.Begin(input) : null;

            _cancellation = new CancellationTokenSource();

            var turns = await Task.Run(() => TurnsFor(input, kind, entry, _cancellation.Token));

            _turns.AddRange(turns);

            // After the turn is in the transcript, so the command's own output is on screen behind
            // the question, and before Running is cleared, so the composer stays disabled while
            // the question is up. A second message starting here would put a second question on
            // the page and orphan the thread waiting on the first one.
            // Not after undo or redo, as on the console: the undone command may have been the
            // answer to it, and putting the question straight back would undo the undo.
            if (kind is not (CommandKind.Undo or CommandKind.Redo))
                await Offer(input);

            // In the same entry as the command, as the files it wrote are.
            entry?.Keep(_relationships?.TakeApplied() ?? []);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;

            lock (_gate)
            {
                Running = false;
                InFlight = null;
            }

            Raise();
        }
    }

    /// <summary>Best-effort stop. Most of what runs below honours no token, which is why the page
    /// says "stopping" and never says it stopped.</summary>
    public void Cancel() => _cancellation?.Cancel();

    public void ClearTranscript()
    {
        _turns.Clear();
        Raise();
    }

    public void End()
    {
        Ended = true;
        _journal.Dispose();

        // Before the repaint, and with a cancellation: a question nobody is left to answer must
        // not hold the command, and the console behind it, open. Not index 1 and not "No" - an
        // abandoned question was never answered, and only a null can say so.
        lock (_questionGate)
            _answer?.TrySetResult(null);

        Raise();
    }

    /// <summary>
    /// Puts the relationship offer up, once the message that may have created one is done with.
    /// </summary>
    /// <remarks>
    /// On a worker rather than on the circuit: the flow blocks the thread it runs on until an
    /// answer arrives, and the thread it would otherwise block is the one that has to repaint the
    /// page with the question. Creating a class is skipped for the same reason the console skips
    /// it — so that a run of them is not interrupted.
    /// </remarks>
    private async Task Offer(string input)
    {
        if (Ended || RelationshipGate.IsClassCreation(input))
            return;

        _relationships ??= RelationshipGate.For(
            () => _workspace.Root, new ChatChoiceAsker(this), Narrate);

        await Task.Run(() => _relationships?.Run());
    }

    /// <summary>Puts a line in the transcript that no command produced.</summary>
    /// <remarks>
    /// The relationship flow runs after a command's output has been captured and written, so it
    /// cannot print into that turn. It gets its own, the way <see cref="Load"/> does.
    /// </remarks>
    public void Narrate(string text)
    {
        _turns.Add(new ChatTurn(
            string.Empty,
            CommandKind.Pipeline,
            ChatVerdict.Succeeded,
            [new TextBlock(text)],
            DateTimeOffset.Now));

        Raise();
    }

    /// <summary>
    /// Re-roots the workspace at a solution, a project or a folder.
    /// </summary>
    /// <remarks>
    /// Recorded as a turn, and worded to say what it did to the whole session: the project
    /// registry is static, so this moved the console the chat was started from too. A page that
    /// quietly changed what the next command acts on would be worse than one that cannot change
    /// it at all.
    /// </remarks>
    public WorkspaceLoad Load(string path)
    {
        var load = _workspace.LoadFrom(path);

        if (load.Ok)
        {
            _journal.Dispose();
            _journal = UndoJournal.ForSession(_workspace.Root, Narrate);
            _recorder = new SessionRecorder(_journal);
        }

        var text = load.Ok
            ? load.SolutionPath is null
                ? $"Loaded {load.Root} — {load.ProjectCount} project(s). "
                  + "The console session is now working here too."
                : $"Loaded {Path.GetFileName(load.SolutionPath)} — {load.ProjectCount} "
                  + $"project(s), rooted at {load.Root}. "
                  + "The console session is now working here too."
            : $"Could not load that: {load.Problem}";

        _turns.Add(new ChatTurn(
            path,
            CommandKind.Pipeline,
            load.Ok ? ChatVerdict.Succeeded : ChatVerdict.Failed,
            [new TextBlock(text)],
            DateTimeOffset.Now));

        Raise();

        return load;
    }

    // A run makes a turn per line and one for the outcome; everything else makes one. Collected
    // and added together once the worker is done, never from it: the page reads _turns while
    // rendering.
    private IReadOnlyList<ChatTurn> TurnsFor(string input, CommandKind kind, UndoScope? entry, CancellationToken ct)
    {
        if (kind == CommandKind.Run)
            return RunScript(input, entry, ct);

        var turn = Run(input, kind, ct);

        if (kind == CommandKind.Pipeline && turn.Verdict == ChatVerdict.Succeeded)
            entry?.Keep([ScriptNotes.SavedForm(input, _neededAi)]);

        return [turn];
    }

    private IReadOnlyList<ChatTurn> RunScript(string input, UndoScope? entry, CancellationToken ct)
    {
        var load = RunCommand.Load(input, _workspace.Root);
        if (load.Steps is null)
            return [Said(input, ChatVerdict.Failed, load.Problem!)];

        var turns = new List<ChatTurn>();
        var tooLarge = string.Empty;

        var outcome = ScriptRunner.Run(
            load.Name,
            load.Steps,
            _workspace.Root,
            step =>
            {
                var turn = StepTurn(step);
                turns.Add(turn);
                return turn.Verdict == ChatVerdict.Succeeded;
            },
            say: _ => { },
            confirmSnapshot: () =>
            {
                // No prompt here: a page cannot ask the console's [p]roceed question mid-run.
                var sizing = WorkspaceSnapshot.Measure(_workspace.Root);
                if (sizing.FileCount <= WorkspaceSnapshot.MaxFiles && sizing.Bytes <= WorkspaceSnapshot.MaxBytes)
                    return true;
                tooLarge = $"The workspace is {sizing.FileCount:N0} files ({sizing.Bytes / (1024.0 * 1024.0):N0} MB), "
                         + "too large to put back if a line fails. Run it from the console, which asks first. ";
                return false;
            },
            stopRequested: () => ct.IsCancellationRequested);

        _currentProject = ProjectManager.CurrentProject;

        if (outcome.Succeeded)
            entry?.Keep(outcome.Ran);

        // What was typed first, holding the outcome, then the lines it ran under it.
        turns.Insert(0, Said(input, outcome.Succeeded ? ChatVerdict.Succeeded : ChatVerdict.Failed, tooLarge + outcome.Message));
        return turns;
    }

    // One script line as a turn. No AI and no questions, as on the console: a script must not stop.
    private ChatTurn StepTurn(ScriptStep step)
    {
        var text = ScriptRunner.Text(step);
        var kind = CommandRoute.Classify(text);
        var verdict = ChatVerdict.Failed;
        string captured;

        using var capture = OutputCapture.Start();
        try
        {
            if (step is BuildAppStep build)
            {
                verdict = ScriptBuildApp.Run(build.Lines, _workspace.Root, Console.WriteLine)
                    ? ChatVerdict.Succeeded
                    : ChatVerdict.Failed;
            }
            else
            {
                var project = _currentProject;
                var outcome = _pipeline.Handle(text, ref project, _workspace.Rules, _workspace.Root,
                    r => ProjectManager.CurrentProject = r);
                _currentProject = project;

                if (!outcome.Recognized)
                    Console.WriteLine(UnrecognizedCommand.Describe(text, CommandSuggester.Closest(text)));

                verdict = CommandColour.Verdict(outcome.Recognized, outcome.Handled) switch
                {
                    CommandVerdict.Succeeded => ChatVerdict.Succeeded,
                    CommandVerdict.Failed => ChatVerdict.Failed,
                    _ => ChatVerdict.Unrecognized
                };
            }
        }
        catch (Exception ex)
        {
            foreach (var line in ErrorText.Lines(ex, ErrorText.Verbose))
                Console.WriteLine(line);
            verdict = ChatVerdict.Failed;
        }
        finally
        {
            captured = capture.Text;
        }

        return new ChatTurn(text, kind, verdict, OutputBlocks.Parse(text, captured), DateTimeOffset.Now,
            Label: $"Line {step.Line}");
    }

    private static ChatTurn Said(string input, ChatVerdict verdict, string text) =>
        new(input, CommandRoute.Classify(input), verdict, [new TextBlock(text)], DateTimeOffset.Now);

    /// <summary>
    /// Runs one classified input and turns what it printed into a turn.
    /// </summary>
    /// <remarks>
    /// The capture is written to the turn whatever happens, including a throw: the same
    /// discipline <c>CommandColour.Run</c> keeps in its finally. A command that crashed after
    /// printing half its work is much easier to diagnose with that half in front of you.
    /// </remarks>
    private ChatTurn Run(string input, CommandKind kind, CancellationToken ct)
    {
        if (Declined(kind) is { } decline)
        {
            return new ChatTurn(
                input, kind, ChatVerdict.Declined, [new TextBlock(decline)], DateTimeOffset.Now);
        }

        var verdict = ChatVerdict.Failed;
        string captured;
        _drawn = null;

        using var capture = OutputCapture.Start();

        try
        {
            verdict = Execute(input, kind, ct);
        }
        catch (Exception ex)
        {
            // The console reporting, word for word, rather than new phrasing for a crash.
            foreach (var line in ErrorText.Lines(ex, ErrorText.Verbose))
                Console.WriteLine(line);

            verdict = ChatVerdict.Failed;
        }
        finally
        {
            captured = capture.Text;
        }

        var blocks = OutputBlocks.Parse(input, captured);

        // Drawn, not printed: a picture has no text for the parser to find.
        if (_drawn is { } drawn)
            blocks = [drawn, .. blocks.Where(b => b is not TextBlock { Text.Length: 0 })];

        return new ChatTurn(input, kind, verdict, blocks, DateTimeOffset.Now);
    }

    // What the command just run asked to have drawn rather than printed. One command at a time
    // runs, so one slot is enough; Run clears it before each.
    private OutputBlock? _drawn;

    /// <summary>
    /// The reason a kind cannot be run here, or null if it can.
    /// </summary>
    /// <remarks>
    /// All three of these prompt mid-flight, which is the same reason <c>CommandColour</c> does not
    /// buffer them: a buffered question is a question nobody can see. `chat` is declined too,
    /// because this is it.
    /// </remarks>
    private static string? Declined(CommandKind kind) => kind switch
    {
        CommandKind.BuildApp =>
            "`build app` asks questions as it goes and opens a workflow page of its own. "
            + "Run it from the console.",

        CommandKind.Batch =>
            "`batch` asks you to review what it detected before it runs anything. "
            + "Run it from the console.",

        CommandKind.Demo =>
            "`demo` asks before it writes anything. Run it from the console.",

        CommandKind.Chat => "You are already in the chat.",

        _ => null
    };

    private ChatVerdict Execute(string input, CommandKind kind, CancellationToken ct)
    {
        switch (kind)
        {
            case CommandKind.History:
                Console.WriteLine(HistoryView.Format([.. _history], 20));
                return ChatVerdict.Succeeded;

            case CommandKind.Help:
                // No pager: the page scrolls, and Pager needs a console to measure.
                Console.WriteLine(HelpCommand.Render(input));
                return ChatVerdict.Succeeded;

            case CommandKind.Status:
                // The report the console prints, called with the values this session has rather
                // than reworded. Two `status` outputs that disagree would be worse than none.
                foreach (var line in StatusReport.Lines(
                    _workspace.Root,
                    _workspace.ProjectCount,
                    ProjectManager.CurrentProject?.ProjectName,
                    _workspace.Rules.Count,
                    CodeGeneratorStarter.StoreReport().Lines,
                    CodeGeneratorStarter.AiState(_ai),
                    VersionInfo.Current,
                    Output.Explain()))
                {
                    Console.WriteLine(line);
                }

                return ChatVerdict.Succeeded;

            case CommandKind.Ai:
                Console.WriteLine(AiCommand.Handle(input, _ai));
                return ChatVerdict.Succeeded;

            case CommandKind.SaveSession:
                var saved = SaveSessionCommand.Handle(input, _workspace.Root, _recorder, DateTime.Now);
                Console.WriteLine(saved.Message);
                return saved.Saved ? ChatVerdict.Succeeded : ChatVerdict.Failed;

            case CommandKind.ShowRelationships:
                // The diagram the console opens a page for, drawn in the turn instead: the chat is
                // already a page.
                var sources = WorkspaceRelationships.Of(_workspace.Root);
                if (sources.Count == 0)
                    Console.WriteLine("No classes in this workspace yet, so there is nothing to relate.");
                else
                    _drawn = new RelationshipsBlock(sources);
                return ChatVerdict.Succeeded;

            case CommandKind.Undo:
            case CommandKind.Redo:
                var result = UndoCommand.Handle(_journal, kind);
                Console.WriteLine(result.Message);

                // Carried between commands like the console's; the journal restored ProjectManager's.
                _currentProject = ProjectManager.CurrentProject;
                return result.Done ? ChatVerdict.Succeeded : ChatVerdict.Failed;

            default:
                return Pipeline(input, ct);
        }
    }

    private ChatVerdict Pipeline(string input, CancellationToken ct)
    {
        var project = _currentProject;

        var outcome = _pipeline.Handle(
            input, ref project, _workspace.Rules, _workspace.Root,
            r => ProjectManager.CurrentProject = r,
            onAnalyzed: null,
            aiResolver: _ai.Resolver,
            confirmAi: resolution => AiDecisionRecorder.ConfirmAndRecord(
                resolution, input, _ai.DecisionLog,
                // Only index 0 is a yes, which is AiConfirmPrompt.Accepted's rule without a
                // second spelling of it. A cancelled question is null, which is not 0.
                r => Ask(SessionQuestion.YesNo(AiConfirmPrompt.Title(r))) == 0),
            ct: ct);

        _currentProject = project;
        _neededAi = outcome.NeededAi;

        if (!outcome.Recognized && !string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine(
                UnrecognizedCommand.Describe(input, CommandSuggester.Closest(input)));
        }

        return CommandColour.Verdict(outcome.Recognized, outcome.Handled) switch
        {
            CommandVerdict.Succeeded => ChatVerdict.Succeeded,
            CommandVerdict.Failed => ChatVerdict.Failed,
            _ => ChatVerdict.Unrecognized
        };
    }

    /// <summary>
    /// Adds the input to the shared history, minus what the console never records.
    /// </summary>
    /// <remarks>
    /// The same list and the same file the console up-arrow walks, so a command typed here is
    /// found there afterwards. <c>clear</c> and <c>cls</c> are excluded for the reason
    /// <c>CommandLineEditor.ShouldRecordInHistory</c> excludes them: clearing the screen is not a
    /// command you want to repeat by pressing up.
    /// </remarks>
    private void Record(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || ClearCommand.IsClear(input))
            return;

        _history.Add(input);

        try
        {
            CommandHistoryFile.AppendLine(_workspace.HistoryPath, input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A history file that cannot be written is not a reason to refuse the command. The
            // in-memory list above still serves the up-arrow for the rest of the session.
        }
    }

    private void Raise() => Changed?.Invoke();
}

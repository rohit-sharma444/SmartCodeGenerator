using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeGenerator.App;
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

    public ChatSession(Workspace workspace, AiLayer ai, List<string> history)
    {
        _workspace = workspace;
        _ai = ai;
        _history = history;
    }

    public IReadOnlyList<ChatTurn> Turns => _turns;

    /// <summary>Whether a command is in flight. The composer is disabled while it is.</summary>
    public bool Running { get; private set; }

    /// <summary>Whether the session is over — `exit`, or every tab gone.</summary>
    public bool Ended { get; private set; }

    /// <summary>What is running, for the indicator to name.</summary>
    public string? InFlight { get; private set; }

    /// <summary>Raised whenever anything a tab draws has changed.</summary>
    public event Action? Changed;

    public string Root => _workspace.Root;

    public int ProjectCount => _workspace.ProjectCount;

    public string? CurrentProject => ProjectManager.CurrentProject?.ProjectName;

    public int RuleCount => _workspace.Rules.Count;

    public string AiState => _ai.Resolver is not null ? "on" : "off";

    public IReadOnlyList<string> History => _history;

    private readonly Lock _questionGate = new();

    private TaskCompletionSource<bool>? _answer;

    /// <summary>The question every tab is showing, or null.</summary>
    public AiQuestion? Question { get; private set; }

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
    /// <see cref="End"/> releases this with a no. Without that, the command hangs and the console
    /// that started the chat hangs behind it.
    /// </para>
    /// </remarks>
    internal bool Ask(AiQuestion question)
    {
        TaskCompletionSource<bool> pending;

        lock (_questionGate)
        {
            if (Ended)
                return false;

            pending = new TaskCompletionSource<bool>(
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
    public void Answer(bool yes)
    {
        lock (_questionGate)
            _answer?.TrySetResult(yes);
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

            _cancellation = new CancellationTokenSource();

            var turn = await Task.Run(() => Run(input, kind, _cancellation.Token));

            _turns.Add(turn);
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

        // Before the repaint, and with a no: a question nobody is left to answer must not hold
        // the command, and the console behind it, open.
        lock (_questionGate)
            _answer?.TrySetResult(false);

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
            [new TextBlock(text)]));

        Raise();

        return load;
    }

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
                input, kind, ChatVerdict.Declined, [new TextBlock(decline)]);
        }

        var verdict = ChatVerdict.Failed;
        string captured;

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

        return new ChatTurn(input, kind, verdict, OutputBlocks.Parse(input, captured));
    }

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
                r => Ask(new AiQuestion(AiConfirmPrompt.Title(r)))),
            ct: ct);

        _currentProject = project;

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
    }

    private void Raise() => Changed?.Invoke();
}

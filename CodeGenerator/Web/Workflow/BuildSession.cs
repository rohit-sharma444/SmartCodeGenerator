using AppScaffolding;
using CodeGenerator.Web;
using DataIngestion;
using DomainDiscovery.Model;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// Every decision a `build app` run makes, and the rules for changing one's mind.
/// </summary>
/// <remarks>
/// Deliberately free of Razor, Kestrel and the console: the components read these properties and
/// call these methods, and the whole workflow is therefore assertable as plain C#. This is the
/// same split <c>SelectionState</c> / <c>SelectionPrompt</c> already uses, one layer up.
/// <para>
/// Registered as a singleton for the host's lifetime, so a second browser tab attaches to the
/// running workflow rather than starting a rival one against the same workspace.
/// </para>
/// <para>
/// Public, along with the rest of <c>CodeGenerator.Web.Workflow</c>, only because Razor generates
/// every component as a public class and a <c>[Parameter]</c> may not be less accessible than the
/// component holding it. Nothing outside this executable references any of it.
/// </para>
/// </remarks>
public sealed class BuildSession
{
    private readonly Dictionary<BuildStage, StageState> _states = new();

    /// <summary>Whether "no data" was chosen, which a null <see cref="Source"/> cannot say on its
    /// own — it also means nobody has answered that node yet.</summary>
    private bool _noDataChosen;

    /// <summary>Whether the look node was answered. The customization always HAS a value, so this
    /// is the only way to tell a decision from a default.</summary>
    private bool _lookAnswered;

    /// <param name="description">
    /// What followed `build app:`, or empty. A description typed inline has already answered
    /// node 1, so the workflow opens at node 2.
    /// </param>
    /// <param name="start">
    /// The remembered profile with anything typed on the command line layered over it —
    /// <c>BuildAppCommand.Merge(CustomizationDefaults.Load(root), inline)</c>.
    /// </param>
    /// <param name="source">
    /// A `from &lt;format&gt; &lt;location&gt;` typed inline, which has answered node 3.
    /// </param>
    public BuildSession(
        string description, string workspaceRoot, AppCustomization start,
        DataSourceSpec? source = null)
    {
        WorkspaceRoot = workspaceRoot;
        Description = description ?? string.Empty;
        Customization = start;
        Source = source;

        foreach (var node in StageGraph.Nodes)
            _states[node.Stage] = StageState.Pending;

        // Inline answers arrive already made, so the nodes that hold them open green and the
        // workflow parks on the first thing it does not know.
        if (Description.Length > 0)
            _states[BuildStage.Describe] = StageState.Done;

        if (source is not null)
            _states[BuildStage.Data] = StageState.Done;

        Current = FirstUnanswered();
        _states[Current] = StageState.Active;
    }

    /// <summary>
    /// Raised after anything here moves.
    /// </summary>
    /// <remarks>
    /// One workflow can have more than one page looking at it — the session is a singleton so
    /// that a second tab attaches rather than starting a rival run against the same workspace —
    /// and a Blazor circuit only repaints when it is told to. Without this, the tab nobody
    /// touched goes on drawing a node the workflow has left, with a live Continue button on it.
    /// <para>
    /// Raised on whichever circuit's thread made the change, which is not the subscriber's: a
    /// page handling this must marshal onto its own renderer before touching its state.
    /// </para>
    /// </remarks>
    public event Action? Changed;

    /// <summary>
    /// Invoked whenever this session has changed in a way worth persisting.
    /// </summary>
    /// <remarks>
    /// A callback rather than a call to a store, for two reasons. This type is in
    /// <c>Web.Workflow</c> and knows nothing about disk - the seam keeps it that way. And
    /// every existing test constructs a session; null-by-default means none of them starts
    /// writing files.
    /// </remarks>
    public Action<BuildSession>? Autosave { get; set; }

    /// <summary>
    /// Says that the model has been edited in place, so the session can be saved.
    /// </summary>
    /// <remarks>
    /// <see cref="Change"/> cannot see these. <c>ReviewStage</c> calls <c>ModelEditor</c>
    /// directly - rename, remove, add field, promote - and every one of them mutates the
    /// model object this session already holds. Without this method an autosave hooked to
    /// answers alone would save every stage transition and lose every edit, which is
    /// precisely the work worth keeping.
    /// <para>
    /// It deliberately does not raise <see cref="Changed"/>: the panel that made the edit has
    /// already re-rendered, and the other tab is repainted by the edit's own broadcast.
    /// </para>
    /// </remarks>
    public void ModelEdited() => Autosave?.Invoke(this);

    public string WorkspaceRoot { get; }

    public BuildStage Current { get; private set; }

    public SessionPhase Phase { get; private set; } = SessionPhase.Running;

    public string Description { get; private set; }

    public ITargetScaffolder? Target { get; private set; }

    public DataSourceSpec? Source { get; private set; }

    /// <summary>Whether node 3 was answered "I have a file". Distinct from
    /// <c>Source is not null</c> only in intent, and read by node 5 to choose its shape.</summary>
    public bool HasData => Source is not null;

    public AppCustomization Customization { get; private set; }

    public CandidateDomainModel? Model { get; private set; }

    /// <summary>The rows read from the user's file, or <see cref="ImportedData.None"/> on the
    /// discovery path. Read by the generate step, and its notes — the row caps — are said out
    /// loud rather than swallowed.</summary>
    public ImportedData ImportedData { get; private set; } = ImportedData.None;

    /// <summary>Whether the generated application was actually started. Distinct from a green
    /// build: compiling is not the same as having seen it run.</summary>
    public bool Started { get; private set; }

    /// <summary>What the generate stage wrote, once it has. Null until then, and again whenever a
    /// changed answer invalidates it — the run stage must not offer to start an app that no longer
    /// matches the answers on screen.</summary>
    public GeneratedApp? Generated { get; private set; }

    /// <summary>Why the node that failed, failed. Null whenever nothing has.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>What the console prints, set by the generate step. Null until it runs.</summary>
    public BuildOutcome? Outcome { get; private set; }

    /// <summary>
    /// The question on screen, when a changed answer is waiting to be confirmed. Null the rest of
    /// the time, which is nearly always.
    /// </summary>
    /// <remarks>
    /// A sentence rather than a flag, because the page has nothing else to say it with and the
    /// count is the part that matters: "the model" is an abstraction, and "14 classes" is the
    /// afternoon the user is about to lose.
    /// </remarks>
    public string? PendingDiscard { get; private set; }

    /// <summary>The change <see cref="PendingDiscard"/> is asking about, held unapplied.</summary>
    private Action? _pending;

    public StageState StateOf(BuildStage stage) => _states[stage];

    public bool SetDescription(string text)
    {
        var value = (text ?? string.Empty).Trim();

        return Change(BuildStage.Describe, value != Description, () => Description = value);
    }

    public bool SetTarget(ITargetScaffolder target)
        => Change(
            BuildStage.AppType,
            !string.Equals(target.Key, Target?.Key, StringComparison.OrdinalIgnoreCase),
            () => Target = target);

    public bool SetSource(DataSourceSpec source)
        => Change(BuildStage.Data, source != Source, () =>
        {
            Source = source;
            _noDataChosen = false;
        });

    public bool SetNoData()
        => Change(BuildStage.Data, Source is not null, () =>
        {
            Source = null;
            _noDataChosen = true;
        });

    /// <remarks>
    /// Called even when the user changed nothing — the look panel applies its answer either way —
    /// so this is what records that the node was answered at all.
    /// </remarks>
    public bool SetCustomization(AppCustomization customization)
        => Change(BuildStage.Look, customization != Customization, () =>
        {
            Customization = customization;
            _lookAnswered = true;
        });

    public bool SetModel(CandidateDomainModel model)
        => Change(BuildStage.Discover, changed: true, () => Model = model);

    public void SetImportedData(ImportedData imported)
    {
        ImportedData = imported;

        Changed?.Invoke();
    }

    /// <summary>Records what the generate stage produced, for the run stage to start.</summary>
    public void SetGenerated(GeneratedApp app)
    {
        Generated = app;
        Started = false;

        Changed?.Invoke();
    }

    /// <summary>
    /// The process the run stage started, while it is still ours to stop. Null before a run, and
    /// again once it has been stopped.
    /// </summary>
    /// <remarks>
    /// On the session rather than the panel because a reload builds a new panel, and an app the
    /// user can no longer stop from the page is an app holding a <c>bin</c> folder that the next
    /// build needs.
    /// </remarks>
    public int? StartedProcessId { get; private set; }

    /// <summary>Records that the run stage started it, and what it started.</summary>
    public void MarkStarted(int? processId = null)
    {
        Started = true;
        StartedProcessId = processId is > 0 ? processId : null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Forgets the process, the app having been stopped.
    /// </summary>
    /// <remarks>
    /// <see cref="Started"/> stays true: the run happened, and the node holds that answer. What is
    /// gone is only the offer to stop something that is no longer there.
    /// </remarks>
    public void MarkStopped()
    {
        StartedProcessId = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Whether <paramref name="stage"/> holds something: an answer given, or work done.
    /// </summary>
    /// <remarks>
    /// This is what <see cref="GoTo"/> uses to decide whether the node being left has been
    /// answered, and it is asked of the session rather than inferred from the node's state because
    /// a state can lie. Marking a node Done merely because the user passed through it is how a
    /// diagram ends up claiming a project was generated when nothing was written.
    /// </remarks>
    public bool HoldsAnAnswer(BuildStage stage)
        => stage switch
        {
            BuildStage.Describe => Description.Length > 0,
            BuildStage.AppType => Target is not null,
            BuildStage.Data => Source is not null || _noDataChosen,
            BuildStage.Look => _lookAnswered,

            // The review node holds the model it reviews, so it stays answered for as long as
            // there is one — even though its own step is an action rather than an answer.
            BuildStage.Discover => Model is not null,
            BuildStage.Review => Model is not null,
            BuildStage.Generate => Generated is not null,
            BuildStage.Run => Started,
            _ => false,
        };

    /// <summary>
    /// Applies an answer, and discards whatever it invalidated — or holds it, and asks first.
    /// </summary>
    /// <remarks>
    /// <paramref name="changed"/> is what makes clicking back into a node harmless: re-answering
    /// with the same value is not a change, so looking at an answer and pressing Next does not
    /// throw away a Wikipedia fetch.
    /// <para>
    /// A change that would cost the model is not applied at all until it is confirmed. Holding it
    /// rather than applying-and-offering-undo is what keeps this honest: while the question is on
    /// screen the session still holds the model, so a user who closes the tab, or answers the
    /// question with the browser's Back button, loses nothing.
    /// </para>
    /// </remarks>
    /// <returns>
    /// Whether the answer was applied. False means it is held and <see cref="PendingDiscard"/> is
    /// the question to put on screen — which is also why the panel must not advance.
    /// </returns>
    private bool Change(BuildStage stage, bool changed, Action apply)
    {
        RequireRunning();

        if (changed && WouldDiscardModel(stage))
        {
            _pending = () => Apply(stage, changed: true, apply);
            PendingDiscard = DiscardWarning();

            Changed?.Invoke();

            return false;
        }

        Apply(stage, changed, apply);

        Changed?.Invoke();

        return true;
    }

    /// <summary>
    /// A change is only worth a question when there is a model to lose and this answer is what
    /// discovery was run from. The table in <see cref="StageGraph.Invalidates"/> is the authority
    /// — asking anywhere else would be caution the rules do not support, and users who are asked
    /// needlessly stop reading the question.
    /// </summary>
    private bool WouldDiscardModel(BuildStage stage)
        => Model is not null && StageGraph.Invalidates(stage).Contains(BuildStage.Discover);

    private string DiscardWarning()
    {
        var count = Model!.Entities.Count;

        return $"This discards the {count} {(count == 1 ? "class" : "classes")}, "
            + "and discovery will run again.";
    }

    /// <summary>Applies a held change. The page calls this when the user says to go ahead.</summary>
    /// <exception cref="InvalidOperationException">Nothing is held. The page drew a question the
    /// session was not asking, which is a wiring bug rather than something to shrug at.</exception>
    public void ConfirmPending()
    {
        RequireRunning();

        var pending = _pending
            ?? throw new InvalidOperationException("No change is waiting to be confirmed.");

        _pending = null;
        PendingDiscard = null;

        pending();

        Changed?.Invoke();
    }

    /// <summary>Drops a held change, model intact. The answer on screen stays as it was.</summary>
    public void CancelPending()
    {
        _pending = null;
        PendingDiscard = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// The question on screen when a cancel is waiting to be answered. Null the rest of the time.
    /// </summary>
    /// <remarks>
    /// On the session rather than the page for the same reason <see cref="PendingDiscard"/> is:
    /// the workflow is one run with possibly two tabs looking at it, and the tab that did not
    /// click Cancel must not go on offering a step forward through a run that is being ended.
    /// </remarks>
    public string? PendingCancel { get; private set; }

    /// <summary>
    /// Ends the workflow — asking first, when there is anything to lose.
    /// </summary>
    /// <remarks>
    /// Cancel is the most destructive control on the page and it was the only unguarded one, while
    /// re-typing a description had been asking since the round before. Nothing is held here the
    /// way <see cref="Change"/> holds a change, because nothing needs to be: cancelling is not
    /// applied until it is confirmed, so waiting costs the user nothing.
    /// </remarks>
    /// <returns>Whether the workflow was cancelled outright, there being nothing to ask about.</returns>
    public bool RequestCancel()
    {
        RequireRunning();

        // An empty first page has nothing to confirm losing, and a wizard that asks anyway teaches
        // the user that these questions are noise to be clicked away.
        if (!StageGraph.Nodes.Any(node => HoldsAnAnswer(node.Stage)))
        {
            Cancel();

            return true;
        }

        PendingCancel = CancelWarning();

        Changed?.Invoke();

        return false;
    }

    /// <summary>
    /// What cancelling would actually cost, which is not the same at every node.
    /// </summary>
    /// <remarks>
    /// A written project is not deleted by cancelling, so a warning that implied it was would be
    /// overstating the stakes — the same failure as understating them, and the faster way to teach
    /// someone to ignore the question.
    /// </remarks>
    private string CancelWarning()
    {
        if (Generated is not null)
            return $"The project at {Generated.Scaffold.ProjectPath} stays on disk, "
                + "but the workflow ends here and the console reports it cancelled.";

        if (Model is not null)
        {
            var count = Model.Entities.Count;

            return $"This discards the {count} {(count == 1 ? "class" : "classes")} discovered, "
                + "and nothing will be generated.";
        }

        return "This discards the answers so far, and nothing will be generated.";
    }

    /// <summary>Ends the workflow, the question having been answered.</summary>
    /// <exception cref="InvalidOperationException">Nothing was asked. As with
    /// <see cref="ConfirmPending"/>, the page drew a question the session was not asking.</exception>
    public void ConfirmCancel()
    {
        RequireRunning();

        if (PendingCancel is null)
            throw new InvalidOperationException("No cancel is waiting to be confirmed.");

        PendingCancel = null;

        Cancel();
    }

    /// <summary>Drops the question. Every answer stays exactly as it was.</summary>
    public void KeepBuilding()
    {
        PendingCancel = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Writes the answer in, and clears out what it invalidated.
    /// </summary>
    /// <remarks>
    /// The value is written back even when nothing changed — that is how the look node records
    /// that it was answered at all — but only a real change costs anything downstream.
    /// </remarks>
    private void Apply(BuildStage stage, bool changed, Action apply)
    {
        apply();

        if (!changed)
            return;

        FailureMessage = null;

        foreach (var invalidated in StageGraph.Invalidates(stage))
        {
            if (_states[invalidated] == StageState.Active)
                continue;

            _states[invalidated] = StageState.Pending;

            if (invalidated == BuildStage.Discover)
            {
                Model = null;
                ImportedData = ImportedData.None;
            }

            if (invalidated == BuildStage.Generate)
                Generated = null;
        }

        // Here rather than in Change, which returns early while it is holding a pending
        // discard - a snapshot taken then would record an answer this session has not
        // accepted yet.
        Autosave?.Invoke(this);
    }

    /// <summary>Marks the open node answered and opens the next one.</summary>
    public void Advance()
    {
        RequireRunning();

        _states[Current] = StageState.Done;

        var next = StageGraph.Next(Current);

        if (next is null)
            return;

        Current = next.Value;
        _states[Current] = StageState.Active;

        Changed?.Invoke();
    }

    /// <summary>Reopens an answered node. This is what a click on a green node does.</summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="stage"/> has not been reached. A Pending node has nothing to show and no
    /// answer to change, so a click on one is a wiring bug rather than user input.
    /// </exception>
    public void GoTo(BuildStage stage)
    {
        RequireRunning();

        if (_states[stage] == StageState.Pending)
            throw new InvalidOperationException($"{stage} has not been reached yet.");

        // The node being left is recorded for what it actually holds. Done where it was answered,
        // so it stays green and can be clicked back into; Pending where it was only passed
        // through — walking into the generate node and leaving again writes nothing, and a green
        // tick there would say a project exists.
        if (_states[Current] == StageState.Active)
            _states[Current] = HoldsAnAnswer(Current) ? StageState.Done : StageState.Pending;

        Current = stage;
        _states[stage] = StageState.Active;

        Changed?.Invoke();
    }

    /// <summary>Records that the open node could not do its work. Not terminal: the panel offers
    /// a way back, because a mistyped path is not a reason to abandon an interview.</summary>
    public void Fail(string message)
    {
        RequireRunning();

        FailureMessage = message;
        _states[Current] = StageState.Failed;

        Changed?.Invoke();
    }

    public void Cancel()
    {
        Phase = SessionPhase.Cancelled;

        Changed?.Invoke();
    }

    /// <summary>The tab was closed. Distinct from <see cref="Cancel"/>: nothing was decided.</summary>
    public void Abandon()
    {
        Phase = SessionPhase.Abandoned;

        Changed?.Invoke();
    }

    /// <summary>
    /// Ends the workflow with what the console should print.
    /// </summary>
    /// <remarks>
    /// Marks whichever node it was called from, not <see cref="BuildStage.Run"/>: a generation
    /// that failed finishes on <see cref="BuildStage.Generate"/>, and claiming the app was run
    /// would be a lie the diagram tells.
    /// </remarks>
    public void Complete(BuildOutcome outcome)
    {
        Outcome = outcome;
        _states[Current] = StageState.Done;
        Phase = SessionPhase.Generated;

        Changed?.Invoke();
    }

    private void RequireRunning()
    {
        if (Phase != SessionPhase.Running)
            throw new InvalidOperationException($"The workflow has already ended ({Phase}).");
    }

    /// <summary>The first node with no answer, which is where an inline-answered run opens.</summary>
    private BuildStage FirstUnanswered()
    {
        foreach (var node in StageGraph.Nodes)
            if (_states[node.Stage] != StageState.Done)
                return node.Stage;

        return BuildStage.Generate;
    }
}

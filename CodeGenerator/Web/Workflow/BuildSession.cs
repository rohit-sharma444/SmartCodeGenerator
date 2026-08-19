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

    public StageState StateOf(BuildStage stage) => _states[stage];

    public void SetDescription(string text)
    {
        var value = (text ?? string.Empty).Trim();

        Change(BuildStage.Describe, value != Description, () => Description = value);
    }

    public void SetTarget(ITargetScaffolder target)
        => Change(
            BuildStage.AppType,
            !string.Equals(target.Key, Target?.Key, StringComparison.OrdinalIgnoreCase),
            () => Target = target);

    public void SetSource(DataSourceSpec source)
        => Change(BuildStage.Data, source != Source, () =>
        {
            Source = source;
            _noDataChosen = false;
        });

    public void SetNoData()
        => Change(BuildStage.Data, Source is not null, () =>
        {
            Source = null;
            _noDataChosen = true;
        });

    /// <remarks>
    /// Called even when the user changed nothing — the look panel applies its answer either way —
    /// so this is what records that the node was answered at all.
    /// </remarks>
    public void SetCustomization(AppCustomization customization)
        => Change(BuildStage.Look, customization != Customization, () =>
        {
            Customization = customization;
            _lookAnswered = true;
        });

    public void SetModel(CandidateDomainModel model)
        => Change(BuildStage.Discover, changed: true, () => Model = model);

    public void SetImportedData(ImportedData imported) => ImportedData = imported;

    /// <summary>Records what the generate stage produced, for the run stage to start.</summary>
    public void SetGenerated(GeneratedApp app)
    {
        Generated = app;
        Started = false;
    }

    /// <summary>Records that the run stage started it.</summary>
    public void MarkStarted() => Started = true;

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
    /// Applies an answer, and discards whatever it invalidated.
    /// </summary>
    /// <remarks>
    /// <paramref name="changed"/> is what makes clicking back into a node harmless: re-answering
    /// with the same value is not a change, so looking at an answer and pressing Next does not
    /// throw away a Wikipedia fetch.
    /// </remarks>
    private void Change(BuildStage stage, bool changed, Action apply)
    {
        RequireRunning();

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
    }

    /// <summary>Records that the open node could not do its work. Not terminal: the panel offers
    /// a way back, because a mistyped path is not a reason to abandon an interview.</summary>
    public void Fail(string message)
    {
        RequireRunning();

        FailureMessage = message;
        _states[Current] = StageState.Failed;
    }

    public void Cancel() => Phase = SessionPhase.Cancelled;

    /// <summary>The tab was closed. Distinct from <see cref="Cancel"/>: nothing was decided.</summary>
    public void Abandon() => Phase = SessionPhase.Abandoned;

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

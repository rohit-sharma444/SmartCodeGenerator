using AppScaffolding;
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
/// </remarks>
internal sealed class BuildSession
{
    private readonly Dictionary<BuildStage, StageState> _states = new();

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

    /// <summary>Why the node that failed, failed. Null whenever nothing has.</summary>
    public string? FailureMessage { get; private set; }

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
        => Change(BuildStage.Data, source != Source, () => Source = source);

    public void SetNoData()
        => Change(BuildStage.Data, Source is not null, () => Source = null);

    public void SetCustomization(AppCustomization customization)
        => Change(BuildStage.Look, customization != Customization, () => Customization = customization);

    public void SetModel(CandidateDomainModel model)
        => Change(BuildStage.Discover, changed: true, () => Model = model);

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
                Model = null;
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

        // The node being left keeps whatever it had. Marking it Pending would erase an answer
        // the user only navigated away from, and every answer downstream is still valid — that
        // is what Invalidates is for.
        if (_states[Current] == StageState.Active)
            _states[Current] = StageState.Done;

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

    public void Complete()
    {
        _states[BuildStage.Generate] = StageState.Done;
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

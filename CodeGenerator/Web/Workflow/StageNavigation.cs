namespace CodeGenerator.Web.Workflow;

/// <summary>
/// How the open panel offers its forward step to the diagram.
/// </summary>
/// <remarks>
/// The diagram is the navigation, but the answer being committed belongs to the panel: only
/// <c>DescribeStage</c> knows what is in its textarea, and only <c>LookStage</c> knows whether the
/// accent it holds is a colour. So the panel hands over a delegate and the box draws the control —
/// the alternative was lifting every draft field into the session, which would put a validation
/// rule in two places.
/// <para>
/// Cascaded, and deliberately not a component: what it decides is assertable without rendering
/// anything.
/// </para>
/// </remarks>
public sealed class StageNavigation
{
    private Func<Task<bool>>? _apply;
    private Func<bool>? _ready;

    /// <summary>What the forward control should say. "Continue" unless a panel names its own step
    /// — the review stage moves on by generating, and calling that "Continue" would hide it.
    /// </summary>
    public string Label { get; private set; } = DefaultLabel;

    public const string DefaultLabel = "Continue";

    /// <summary>Raised when a panel offers, withdraws or relabels its forward step, so the diagram
    /// can redraw. The panel and the diagram are siblings; neither renders the other.</summary>
    public event Action? Changed;

    /// <summary>
    /// Offers a forward step.
    /// </summary>
    /// <param name="apply">
    /// Applies the panel's answer to the session and reports whether it took. It does NOT advance:
    /// the page owns that, so that leaving a node by clicking another box can keep the answer
    /// without stepping into the stage after it — which would mark an untouched stage as done.
    /// False means the panel refused its own input and is showing why.
    /// </param>
    /// <param name="ready">
    /// Whether the answer is usable yet. Null means always — a panel that reports its own errors
    /// after the click (a path that does not exist) is better than one that silently refuses to
    /// light up.
    /// </param>
    /// <param name="label">What the control says.</param>
    public void Offer(
        Func<Task<bool>> apply, Func<bool>? ready = null, string label = DefaultLabel)
    {
        _apply = apply;
        _ready = ready;
        Label = label;

        Changed?.Invoke();
    }

    /// <summary>Withdraws the forward step — nothing to move on to while a fetch is running.</summary>
    public void Withdraw()
    {
        _apply = null;
        _ready = null;
        Label = DefaultLabel;

        Changed?.Invoke();
    }

    /// <summary>
    /// Tells the diagram that the offered step's readiness may have changed.
    /// </summary>
    /// <remarks>
    /// The <c>ready</c> delegate is only read when the page redraws, and a field edit inside a panel
    /// redraws the panel alone. Without this, correcting a bad field cleared its error but left the
    /// forward control disabled until something else redrew the page.
    /// </remarks>
    public void ReadinessChanged() => Changed?.Invoke();

    /// <summary>Whether the diagram should draw an enabled forward control.</summary>
    public bool CanContinue => _apply is not null && (_ready?.Invoke() ?? true);

    /// <summary>Whether a panel has offered a step at all — an offered-but-not-ready step is drawn
    /// disabled rather than missing, so the user can see what they are working towards.</summary>
    public bool HasStep => _apply is not null;

    /// <summary>Applies the open panel's answer. False when the panel refused it — a path that is
    /// not there — in which case the workflow stays where it is and the panel says why.</summary>
    public Task<bool> ApplyAsync() => CanContinue ? _apply!() : Task.FromResult(false);

    /// <summary>
    /// Whether the step is ordinary movement rather than an action with a name of its own.
    /// </summary>
    /// <remarks>
    /// The distinction earns its keep twice. A named step — the review stage's <c>Generate</c> —
    /// is never taken by clicking somewhere else, and the stage it leads to is not offered as a
    /// place to click ahead to: until the project has actually been written there is nothing there,
    /// and a box that invites a click it cannot honour is worse than a box that stays dark.
    /// </remarks>
    public bool IsPlainStep => Label == DefaultLabel;

    /// <summary>
    /// Whether leaving the open node by clicking another box should keep what the panel holds.
    /// </summary>
    /// <remarks>
    /// Yes for a plain step: a user who picks an application type and then clicks a box to go back
    /// and check something should not silently lose the pick. No for a named one, for the reason
    /// <see cref="IsPlainStep"/> gives. An unusable step is not applied either; there the box keeps
    /// showing the answer that still stands, which is the honest thing to see.
    /// </remarks>
    public bool ShouldCommitBeforeLeaving => CanContinue && IsPlainStep;

    /// <summary>Whether the stage this step leads to may be clicked as a way of taking it.</summary>
    public bool LeadsSomewhereClickable => CanContinue && IsPlainStep;
}

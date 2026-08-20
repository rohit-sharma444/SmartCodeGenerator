using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Web;

/// <summary>
/// What the console prints once the browser is done with the workflow.
/// </summary>
/// <remarks>
/// Lines rather than a formatted block, for the reason <c>BuildReport</c> and <c>NextSteps</c>
/// return lines: the caller decides the colour, and a test can assert the content without
/// parsing a screen.
/// </remarks>
public sealed record BuildOutcome(SessionPhase Phase, IReadOnlyList<string> Lines)
{
    public static BuildOutcome Cancelled { get; } =
        new(SessionPhase.Cancelled, ["Cancelled. Nothing was generated."]);

    /// <summary>The tab was closed without a decision — deliberately worded differently from
    /// <see cref="Cancelled"/>, which was one.</summary>
    public static BuildOutcome Abandoned { get; } =
        new(SessionPhase.Abandoned,
            ["The build workflow was closed before it finished. Nothing was generated."]);
}

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

    /// <summary>
    /// Cancelled, but not before the project was written — so it is still there.
    /// </summary>
    /// <remarks>
    /// <see cref="Cancelled"/> cannot be reused here: "Nothing was generated" is false once the
    /// generate stage has run, and the path is the one thing the user needs from a run they have
    /// just ended.
    /// </remarks>
    public static BuildOutcome CancelledLeaving(string projectPath)
        => new(SessionPhase.Cancelled,
            ["Cancelled.", $"The project at {projectPath} was already written, and is still there."]);

    /// <summary>
    /// Cancelled having written nothing, over a project an earlier run wrote - which stays.
    /// </summary>
    public static BuildOutcome CancelledKeeping(string projectPath)
        => new(SessionPhase.Cancelled,
            ["Cancelled. Nothing more was generated.",
             $"The project an earlier run wrote at {projectPath} is still there, unchanged."]);

    /// <summary>The same, for a tab closed without a decision.</summary>
    public static BuildOutcome AbandonedKeeping(string projectPath)
        => new(SessionPhase.Abandoned,
            ["The build workflow was closed before it finished. Nothing more was generated.",
             $"The project an earlier run wrote at {projectPath} is still there, unchanged."]);

    /// <summary>The same, for a tab closed after the project was written.</summary>
    public static BuildOutcome AbandonedLeaving(string projectPath)
        => new(SessionPhase.Abandoned,
            ["The build workflow was closed before it finished.",
             $"The project at {projectPath} was already written, and is still there."]);
}

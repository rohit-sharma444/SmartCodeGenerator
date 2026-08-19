namespace CodeGenerator.Web.Workflow;

/// <summary>How one node draws, and whether it can be revisited.</summary>
public enum StageState
{
    /// <summary>Not reached, or invalidated by a change upstream.</summary>
    Pending,

    /// <summary>The node whose panel is open.</summary>
    Active,

    /// <summary>Answered. Clickable — this is what makes the workflow revisitable.</summary>
    Done,

    /// <summary>Tried and failed: a fetch that threw, a file that would not read. Carries its
    /// own message, and offers a way back rather than ending the workflow.</summary>
    Failed
}

/// <summary>Whether the workflow is still going, and if not, how it ended.</summary>
/// <remarks>
/// <see cref="Abandoned"/> is distinct from <see cref="Cancelled"/> on purpose: a user who
/// closed the tab did not decide anything, and the console says so differently from one who
/// pressed Cancel.
/// </remarks>
public enum SessionPhase
{
    Running,
    Cancelled,
    Generated,
    Abandoned
}

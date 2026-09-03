namespace CodeGenerator.Web.Workflow;

/// <summary>
/// The nine nodes of the `build app` workflow, in the order the diagram draws them.
/// </summary>
/// <remarks>
/// The order matters twice: <see cref="StageGraph.Next"/> and
/// <see cref="StageGraph.Previous"/> are declaration-order walks, and the diagram lays the
/// nodes out by index. A stage inserted in the middle therefore needs no other change here —
/// but it does need a row in <see cref="StageGraph.Invalidates"/>, which is deliberately a
/// total switch so the compiler asks for one.
/// </remarks>
public enum BuildStage
{
    Describe,
    AppType,
    Data,
    Look,
    Discover,
    Review,

    /// <summary>
    /// What will be written: how the classes connect, and which of them get a screen.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="Review"/>, which carried four separate jobs on one page: editing
    /// the classes, reading the relationship diagram, choosing each class's screen, and
    /// confirming what would be generated — the last of which the user had to scroll past the
    /// whole model to reach. Structure is settled at <see cref="Review"/>; what comes out of it
    /// is settled here.
    /// <para>
    /// Inserting a member here does NOT invalidate a saved run. <c>BuildSnapshot</c> writes the
    /// stage as <c>session.Current.ToString()</c> and <c>ResumedSession</c> reads it back with
    /// <c>Enum.TryParse&lt;BuildStage&gt;</c> — by name, never by ordinal — so a snapshot written
    /// before this existed still resolves.
    /// </para>
    /// <para>
    /// The same property makes RENAMING one of these a migration rather than a refactor: the name
    /// is what is on disk, so a snapshot holding the old spelling stops parsing and the resume
    /// falls all the way back to <see cref="Describe"/>. This member was renamed from
    /// "Relationships" while it had never shipped, so no snapshot could hold the old name. Do not
    /// rename one that has.
    /// </para>
    /// </remarks>
    Output,

    Generate,

    /// <summary>
    /// Starting what was written. Its own node rather than a button on the generate panel: a
    /// project that compiles is not yet a thing the user has seen work, and the diagram should
    /// say that the run is still ahead of them.
    /// </summary>
    Run
}

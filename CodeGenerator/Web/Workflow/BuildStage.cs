namespace CodeGenerator.Web.Workflow;

/// <summary>
/// The eight nodes of the `build app` workflow, in the order the diagram draws them.
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
    Generate,

    /// <summary>
    /// Starting what was written. Its own node rather than a button on the generate panel: a
    /// project that compiles is not yet a thing the user has seen work, and the diagram should
    /// say that the run is still ahead of them.
    /// </summary>
    Run
}

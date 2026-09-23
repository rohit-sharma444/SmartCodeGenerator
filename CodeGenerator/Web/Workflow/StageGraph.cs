namespace CodeGenerator.Web.Workflow;

/// <summary>One node as the diagram needs it: what to call it, and one line of what it does.</summary>
public sealed record StageNode(BuildStage Stage, string Title, string Blurb);

/// <summary>
/// The workflow's shape. Pure: nodes, the walk between them, and what changing an answer costs.
/// </summary>
/// <remarks>
/// Separate from <see cref="BuildSession"/> for the reason <c>SelectionState</c> is separate
/// from <c>SelectionPrompt</c> — everything decidable is lifted out of the thing that holds
/// mutable state, so it can be asserted directly.
/// </remarks>
public static class StageGraph
{
    public static IReadOnlyList<StageNode> Nodes { get; } =
    [
        new(BuildStage.Describe, "Describe", "What are we building?"),
        new(BuildStage.AppType,  "Application Type", "Which kind of application"),
        new(BuildStage.Data,     "Data",     "A file to read, or discover from the web"),
        new(BuildStage.Look,     "Look",     "Branding, theme and features"),
        new(BuildStage.Discover, "Discover", "Work out the classes"),
        new(BuildStage.Review,        "Review classes", "Change the model before it is written"),
        new(BuildStage.Output,        "Output",         "What gets built, and how it connects"),
        new(BuildStage.EntryTemplate, "Entry Template", "How master and transaction screens arrange their fields"),
        new(BuildStage.Generate, "Generate", "Write it and build it"),
        new(BuildStage.Run,      "Run",      "Start it and watch it come up"),
    ];

    /// <summary>What the browser tab should say.</summary>
    /// <remarks>
    /// It said "Build an application" and nothing else — for all ten stages, and for both tabs
    /// at once, which is precisely when a title has work to do. The position goes first because
    /// that is the part still legible in a narrow tab.
    /// </remarks>
    public static string TabTitle(BuildSession session)
    {
        var at = Nodes.ToList().FindIndex(n => n.Stage == session.Current);
        var node = Nodes[at < 0 ? 0 : at];

        return $"{(at < 0 ? 0 : at) + 1} of {Nodes.Count} · {node.Title} — Build an application";
    }

    public static BuildStage? Next(BuildStage stage)
    {
        var index = IndexOf(stage);

        return index + 1 < Nodes.Count ? Nodes[index + 1].Stage : null;
    }

    public static BuildStage? Previous(BuildStage stage)
    {
        var index = IndexOf(stage);

        return index > 0 ? Nodes[index - 1].Stage : null;
    }

    /// <summary>
    /// The stages whose results are worthless once <paramref name="changed"/> is answered
    /// differently.
    /// </summary>
    /// <remarks>
    /// Not simply "everything downstream". An application type and a customization are not read
    /// until <see cref="BuildStage.Generate"/>, so changing either leaves a discovered model
    /// perfectly valid — and re-running a Wikipedia fetch because a checkbox moved would be the
    /// worst version of this feature. A description, by contrast, is the discovery input AND the
    /// app name, so changing it invalidates everything discovery produced.
    /// <para>
    /// A total switch, so a stage added to <see cref="BuildStage"/> cannot compile without a
    /// decision being made here.
    /// </para>
    /// <para>
    /// <see cref="BuildStage.Discover"/> lists itself: a second completed discovery is exactly
    /// as worthless to a model already discovered as a changed description is, and without this
    /// entry <c>BuildSession.WouldDiscardModel</c> can never be true for a second
    /// <c>SetModel</c> call — which is how a second, unguarded discovery call used to silently
    /// overwrite a good model instead of asking first. This is safe on the ordinary first
    /// completion: <c>Apply</c> already skips resetting whichever stage is <c>Active</c>, which
    /// is Discover itself for as long as its own panel is the one calling <c>SetModel</c>.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<BuildStage> Invalidates(BuildStage changed)
        => changed switch
        {
            BuildStage.Describe =>
                [BuildStage.Discover, BuildStage.Review, BuildStage.Output,
                 BuildStage.Generate, BuildStage.Run],
            BuildStage.Data     =>
                [BuildStage.Discover, BuildStage.Review, BuildStage.Output,
                 BuildStage.Generate, BuildStage.Run],
            BuildStage.Discover =>
                [BuildStage.Discover, BuildStage.Review, BuildStage.Output,
                 BuildStage.Generate, BuildStage.Run],
            BuildStage.AppType       => [],
            BuildStage.Look          => [],
            BuildStage.Review        => [],

            // Nothing downstream reads a screen choice until Generate, and Generate has no stored
            // answer to discard — so changing one here costs nothing, exactly as Look does.
            BuildStage.Output => [],

            // Nothing downstream reads this until Generate, and Generate has no stored answer to
            // discard — so changing it costs nothing, exactly as Output and Look do.
            BuildStage.EntryTemplate => [],
            BuildStage.Generate      => [],
            BuildStage.Run           => [],
            _ => throw new InvalidOperationException($"No invalidation rule for {changed}."),
        };

    /// <summary>Where <paramref name="stage"/> sits in the walk. A miss is a wiring bug: every
    /// member of the enum has a node.</summary>
    private static int IndexOf(BuildStage stage)
    {
        for (var i = 0; i < Nodes.Count; i++)
            if (Nodes[i].Stage == stage)
                return i;

        throw new InvalidOperationException($"No node for stage {stage}.");
    }
}

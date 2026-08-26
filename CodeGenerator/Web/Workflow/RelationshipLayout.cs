namespace CodeGenerator.Web.Workflow;

/// <summary>
/// One class and what it points at, which is all the diagram needs to know about it.
/// </summary>
/// <remarks>
/// Deliberately not <c>CandidateEntity</c>. The review page feeds this from the model discovery
/// produced, and <c>show relationships</c> feeds it from the classes already on disk, which share
/// no type at all. A layout that named either one would have to be duplicated for the other.
/// </remarks>
public sealed record RelationshipSource(string Name, IReadOnlyList<string> Targets);

/// <summary>One class, as a box on the diagram.</summary>
public sealed record RelationshipBox(string Name, int X, int Y);

/// <summary>One relationship, as an arrow between two boxes.</summary>
/// <summary>
/// One relationship, as an arrow between two boxes.
/// </summary>
/// <param name="Turn">
/// Where the elbow turns, between the two columns. Every arrow crossing the same gap gets its own
/// x here, which is not cosmetic: with a single turn in the middle of the gap, five arrows drew
/// their vertical segments on top of one another and became one line that could not be traced back
/// to any source. That was invisible to every assertion and obvious in the first screenshot.
/// </param>
public sealed record RelationshipArrow(
    string From, string To, int X1, int Y1, int X2, int Y2, int Turn);

/// <summary>
/// Where the boxes and arrows go on the review diagram.
/// </summary>
/// <remarks>
/// Coordinates are worked out here rather than left to CSS, which is a deliberate departure from
/// <c>StageDiagram</c>. That one draws its chain as a CSS grid with pseudo-element connectors
/// precisely so that a node whose title wraps never needs the arrows re-measured — and that works
/// because the pipeline is a straight line, where every connector joins one box to the next. A
/// relationship graph is not a line: one class can have three arrows into it from three different
/// columns, which no amount of grid can express. So the coordinates are real, and the determinism
/// comes back from a fixed box size and a truncated name instead of from the layout engine.
/// <para>
/// Pure, and no reference to the browser: layering, cycles and the unrelated band are the parts
/// that can be wrong, and they are testable as arithmetic. Whether the result is legible is the
/// part no assertion can answer, which is what a screenshot is for.
/// </para>
/// <para>
/// What it draws is what <c>DomainModelMapper</c> will write, so it applies the same two rules the
/// mapper does: a target it cannot resolve to another entity is skipped, and a class cannot be
/// related to itself (<c>RelationshipConvention.CanAdd</c> refuses a member named after its own
/// class). A picture that showed either would be promising code that will not appear.
/// </para>
/// </remarks>
public sealed class RelationshipLayout
{
    public const int BoxWidth = 150;
    public const int BoxHeight = 40;

    private const int ColumnGap = 90;
    private const int RowGap = 18;
    private const int BandGap = 46;

    private RelationshipLayout(
        IReadOnlyList<RelationshipBox> boxes,
        IReadOnlyList<RelationshipArrow> arrows,
        IReadOnlyList<RelationshipBox> unrelated,
        int width,
        int height)
    {
        Boxes = boxes;
        Arrows = arrows;
        Unrelated = unrelated;
        Width = width;
        Height = height;
    }

    /// <summary>The classes that are related to something, placed in columns.</summary>
    public IReadOnlyList<RelationshipBox> Boxes { get; }

    public IReadOnlyList<RelationshipArrow> Arrows { get; }

    /// <summary>The classes related to nothing at all, placed in a band of their own.</summary>
    /// <remarks>
    /// Shown rather than hidden. That five of eleven classes have no relationship is the single
    /// most useful thing this picture can tell someone, and a diagram that quietly dropped them
    /// would read as though the model were fully connected.
    /// </remarks>
    public IReadOnlyList<RelationshipBox> Unrelated { get; }

    public int Width { get; }

    public int Height { get; }

    public static RelationshipLayout Of(IReadOnlyList<RelationshipSource> entities)
    {
        var names = entities
            .Select(e => e.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var canonical = names.ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);

        var edges = Edges(entities, canonical);

        var connected = names
            .Where(n => edges.Any(e => Same(e.From, n) || Same(e.To, n)))
            .ToList();

        var layers = Layer(connected, edges);

        var boxes = Place(layers);
        var placed = boxes.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);

        var graphHeight = boxes.Count == 0 ? 0 : boxes.Max(b => b.Y) + BoxHeight;

        var unrelated = Band(
            names.Where(n => !placed.ContainsKey(n)).ToList(),
            graphHeight == 0 ? 0 : graphHeight + BandGap);

        var arrows = boxes.Count == 0 ? [] : Route(edges, placed);

        var all = boxes.Concat(unrelated).ToList();

        return new RelationshipLayout(
            boxes,
            arrows,
            unrelated,
            all.Count == 0 ? 0 : all.Max(b => b.X) + BoxWidth,
            all.Count == 0 ? 0 : all.Max(b => b.Y) + BoxHeight);
    }

    /// <summary>Every distinct pair, in the order the columns will read.</summary>
    private static List<(string From, string To)> Edges(
        IReadOnlyList<RelationshipSource> entities, Dictionary<string, string> canonical)
    {
        var edges = new List<(string From, string To)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (!canonical.TryGetValue(entity.Name ?? string.Empty, out var from))
                continue;

            foreach (var target in entity.Targets)
            {
                if (!canonical.TryGetValue(target ?? string.Empty, out var to))
                    continue;

                if (Same(from, to) || !seen.Add($"{from}->{to}"))
                    continue;

                edges.Add((from, to));
            }
        }

        return edges;
    }

    /// <summary>
    /// Which column each class sits in: the longest path to it, so an arrow always points right.
    /// </summary>
    /// <remarks>
    /// A relaxation pass rather than a topological sort, capped at one round per class. Two classes
    /// pointing at each other is a model the miner can produce and has no valid layering at all;
    /// the cap ends the relaxation and the back edge is simply drawn pointing left, which is worth
    /// more than a diagram that throws or loops on a model the user is still allowed to fix.
    /// </remarks>
    private static Dictionary<string, int> Layer(
        IReadOnlyList<string> connected, List<(string From, string To)> edges)
    {
        var layer = connected.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);

        for (var pass = 0; pass < connected.Count; pass++)
        {
            var moved = false;

            foreach (var (from, to) in edges)
            {
                if (layer[to] >= layer[from] + 1)
                    continue;

                layer[to] = layer[from] + 1;
                moved = true;
            }

            if (!moved)
                break;
        }

        return layer;
    }

    private static List<RelationshipBox> Place(Dictionary<string, int> layers) =>
    [
        .. layers
            .GroupBy(pair => pair.Value)
            .OrderBy(column => column.Key)
            .SelectMany(column => column
                .Select(pair => pair.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select((name, row) => new RelationshipBox(
                    name,
                    column.Key * (BoxWidth + ColumnGap),
                    row * (BoxHeight + RowGap))))
    ];

    /// <summary>The unrelated classes, wrapped across rows under the graph.</summary>
    private static List<RelationshipBox> Band(IReadOnlyList<string> names, int top)
    {
        const int PerRow = 5;

        return
        [
            .. names.Select((name, i) => new RelationshipBox(
                name,
                i % PerRow * (BoxWidth + RowGap),
                top + i / PerRow * (BoxHeight + RowGap)))
        ];
    }

    /// <summary>
    /// The arrows, each with its own lane through the gap it crosses.
    /// </summary>
    /// <remarks>
    /// Arrows are grouped by the gap they cross and spaced evenly across it, so no two share a
    /// vertical segment. A single arrow still turns in the middle, which is where it would have
    /// been anyway.
    /// </remarks>
    private static List<RelationshipArrow> Route(
        List<(string From, string To)> edges, Dictionary<string, RelationshipBox> placed)
    {
        var routed = new List<RelationshipArrow>();

        var byGap = edges
            .Select(e => (Edge: e, From: placed[e.From], To: placed[e.To]))
            .GroupBy(a => (a.From.X, a.To.X));

        foreach (var gap in byGap)
        {
            var lanes = gap.Count();
            var start = gap.Key.Item1 + BoxWidth;
            var span = gap.Key.Item2 - start;

            var lane = 0;

            foreach (var (edge, from, to) in gap.OrderBy(a => a.From.Y).ThenBy(a => a.To.Y))
            {
                lane++;

                routed.Add(new RelationshipArrow(
                    edge.From, edge.To,
                    start, from.Y + BoxHeight / 2,
                    to.X, to.Y + BoxHeight / 2,
                    start + span * lane / (lanes + 1)));
            }
        }

        return routed;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

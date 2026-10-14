namespace CodeGenerator.Web.Workflow;

/// <summary>
/// One class and what it points at, which is all the diagram needs to know about it.
/// </summary>
/// <remarks>
/// Deliberately not <c>CandidateEntity</c>. The review page feeds this from the model discovery
/// produced, and <c>show relationships</c> feeds it from the classes already on disk, which share
/// no type at all. A layout that named either one would have to be duplicated for the other.
/// <para>
/// <paramref name="Fields"/> is every field the class has. The layout picks which of them to draw —
/// the key, and the foreign key behind each relationship — and counts the rest. Passing the whole
/// list rather than a pre-filtered one keeps that decision in one place.
/// </para>
/// </remarks>
public sealed record RelationshipSource(
    string Name,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Fields)
{
    /// <summary>A class whose fields are not known, or not worth drawing.</summary>
    public RelationshipSource(string name, IReadOnlyList<string> targets)
        : this(name, targets, []) { }

    /// <summary>
    /// The key behind each relationship, when the caller knows it; null when every target is held
    /// in <c>&lt;Target&gt;Id</c>.
    /// </summary>
    /// <remarks>
    /// A class read from a database can hold two keys to one class (a payment's <c>PaidById</c>
    /// and <c>ApprovedById</c>, both to Staff) or a key to itself (a member of staff's
    /// <c>ManagerId</c>). A list of targets says neither: it is one Staff, and no line can leave
    /// from <c>PaidById</c> when the only name it knows is <c>StaffId</c>.
    /// </remarks>
    public IReadOnlyList<RelationshipLink>? Links { get; init; }

    /// <summary>Every relationship with the key that holds it.</summary>
    public IReadOnlyList<RelationshipLink> Keys =>
        Links ?? [.. Targets.Select(t => new RelationshipLink(t + "Id", t))];

    /// <summary>A class whose relationships are known key by key.</summary>
    public static RelationshipSource WithLinks(
        string name, IReadOnlyList<RelationshipLink> links, IReadOnlyList<string> fields) =>
        new(name, [.. links.Select(l => l.Target).Distinct(StringComparer.OrdinalIgnoreCase)], fields)
        {
            Links = links
        };
}

/// <summary>One relationship: the key field that holds it, and the class it points at.</summary>
public sealed record RelationshipLink(string Field, string Target)
{
    /// <summary>Whether the key is named for a role (<c>PaidById</c>, <c>ManagerId</c>) rather
    /// than for its target (<c>CustomerId</c>).</summary>
    public bool IsRole => !string.Equals(Field, Target + "Id", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One line inside a box: a field worth showing, and what marks it.</summary>
/// <param name="Marker">"PK", "FK", or empty. Drawn in the gutter beside the name.</param>
/// <param name="Y">The centre of the row, in diagram coordinates — so a line can leave from the
/// exact field that carries the relationship rather than from the middle of the box.</param>
public sealed record RelationshipRow(string Name, string Marker, int Y);

/// <summary>One class, as a table on the diagram.</summary>
public sealed record RelationshipBox(
    string Name,
    int X,
    int Y,
    int Height,
    IReadOnlyList<RelationshipRow> Rows,
    int Hidden);

/// <summary>
/// One relationship, as a line between two tables.
/// </summary>
/// <param name="Turn">
/// Where the elbow turns, between the two columns. Every line crossing the same gap gets its own
/// x here, which is not cosmetic: with a single turn in the middle of the gap, five lines drew
/// their vertical segments on top of one another and became one line that could not be traced back
/// to any source.
/// </param>
/// <param name="Field">The key the line leaves from.</param>
/// <param name="Self">A class pointing at itself: the line leaves the right side of the box and
/// comes back into it, so its one end faces the other way.</param>
public sealed record RelationshipArrow(
    string From, string To, int X1, int Y1, int X2, int Y2, int Turn,
    string Field = "", bool Self = false)
{
    /// <summary>Whether the key is named for a role rather than for its target.</summary>
    public bool IsRole => new RelationshipLink(Field, To).IsRole;
}

/// <summary>
/// Where the tables and lines go on the relationship diagram.
/// </summary>
/// <remarks>
/// Coordinates are worked out here rather than left to CSS, which is a deliberate departure from
/// <c>StageDiagram</c>. That one draws its chain as a CSS grid with pseudo-element connectors
/// precisely so that a node whose title wraps never needs the arrows re-measured — and that works
/// because the pipeline is a straight line, where every connector joins one box to the next. A
/// relationship graph is not a line: one class can have three lines into it from three different
/// columns, which no amount of grid can express.
/// <para>
/// Pure, and no reference to the browser: layering, cycles, row selection and the unrelated band
/// are the parts that can be wrong, and they are testable as arithmetic. Whether the result is
/// legible is the part no assertion can answer, which is what a screenshot is for.
/// </para>
/// <para>
/// What it draws is what <c>DomainModelMapper</c> will write, so it applies the same rules the
/// mapper does: a target it cannot resolve to another entity is skipped, and a class is related
/// to itself only through a key named for a role (<c>ManagerId</c> on Staff, which the mapper
/// gives a <c>Manager</c> navigation) - <c>RelationshipConvention.CanAdd</c> refuses a member
/// named after its own class, so a <c>StaffId</c> on Staff is written as nothing. A picture that
/// showed it would be promising code that will not appear.
/// </para>
/// <para>
/// One line per key, not per pair of classes: a payment's <c>PaidById</c> and
/// <c>ApprovedById</c> are two relationships to Staff, each its own navigation in the generated
/// code, and drawing them as one line would hide the second.
/// </para>
/// </remarks>
public sealed class RelationshipLayout
{
    public const int BoxWidth = 208;

    /// <summary>The title band carrying the class name.</summary>
    public const int HeaderHeight = 30;

    /// <summary>One field line.</summary>
    public const int RowHeight = 20;

    /// <summary>The height of a box in the unrelated band, which has no rows.</summary>
    public const int BandBoxHeight = 30;

    /// <summary>
    /// The width of a box in the unrelated band.
    /// </summary>
    /// <remarks>
    /// Narrower than an entity table, because it holds a name and nothing else. At the table width
    /// a band of five was wider than the graph above it and put a horizontal scrollbar under a
    /// diagram that otherwise fitted.
    /// </remarks>
    public const int BandBoxWidth = 150;

    private const int RowsTop = 6;
    private const int RowsBottom = 8;
    private const int ColumnGap = 120;
    private const int RowGap = 22;
    private const int BandGap = 52;

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
        var byName = entities
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var names = byName.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

        var edges = Edges(entities, byName);

        var connected = names
            .Where(n => edges.Any(e => Same(e.From, n) || Same(e.To, n)))
            .ToList();

        var boxes = Place(Layer(connected, edges), byName, edges);
        var placed = boxes.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);

        var graphHeight = boxes.Count == 0 ? 0 : boxes.Max(b => b.Y + b.Height);

        var unrelated = Band(
            names.Where(n => !placed.ContainsKey(n)).ToList(),
            graphHeight == 0 ? 0 : graphHeight + BandGap);

        var arrows = boxes.Count == 0 ? [] : Route(edges, placed);

        var all = boxes.Concat(unrelated).ToList();

        return new RelationshipLayout(
            boxes,
            arrows,
            unrelated,
            all.Count == 0
                ? 0
                : Math.Max(
                    boxes.Count == 0
                        ? 0
                        : boxes.Max(b => b.X + BoxWidth
                                         + LoopReach(edges.Count(e => e.Self && Same(e.From, b.Name)))),
                    unrelated.Count == 0 ? 0 : unrelated.Max(b => b.X) + BandBoxWidth),
            all.Count == 0 ? 0 : all.Max(b => b.Y + b.Height));
    }

    /// <summary>One relationship, by the key that holds it.</summary>
    private sealed record Edge(string From, string To, string Field)
    {
        public bool Self => Same(From, To);
    }

    /// <summary>How far past a box's right side its self-loops reach: one lane each.</summary>
    private static int LoopReach(int loops) => loops == 0 ? 0 : LoopFirst + (loops - 1) * LoopLane + 4;

    private const int LoopFirst = 26;
    private const int LoopLane = 12;

    /// <summary>The height a box needs for the rows it will show.</summary>
    public static int HeightFor(int rows, bool hasHidden) =>
        HeaderHeight + RowsTop + rows * RowHeight + (hasHidden ? RowHeight : 0) + RowsBottom;

    /// <summary>Every distinct key, in the order the columns will read.</summary>
    /// <remarks>
    /// Distinct by the key, not by the pair: two keys to one class are two relationships. A key to
    /// the class itself counts only when it is named for a role - see the class remarks.
    /// </remarks>
    private static List<Edge> Edges(
        IReadOnlyList<RelationshipSource> entities, Dictionary<string, RelationshipSource> byName)
    {
        var edges = new List<Edge>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (!byName.TryGetValue(entity.Name ?? string.Empty, out var from))
                continue;

            foreach (var link in entity.Keys)
            {
                if (!byName.TryGetValue(link.Target ?? string.Empty, out var to))
                    continue;

                var field = link.IsRole ? link.Field : to.Name + "Id";

                if (Same(from.Name, to.Name) && !link.IsRole)
                    continue;

                if (!seen.Add($"{from.Name}.{field}"))
                    continue;

                edges.Add(new Edge(from.Name, to.Name, field));
            }
        }

        return edges;
    }

    /// <summary>
    /// Which column each class sits in: the longest path to it, so a line always points right.
    /// </summary>
    /// <remarks>
    /// A relaxation pass rather than a topological sort, capped at one round per class. Two classes
    /// pointing at each other is a model the miner can produce and has no valid layering at all;
    /// the cap ends the relaxation and the back edge is simply drawn pointing left, which is worth
    /// more than a diagram that throws or loops on a model the user is still allowed to fix.
    /// </remarks>
    private static Dictionary<string, int> Layer(
        IReadOnlyList<string> connected, List<Edge> edges)
    {
        var layer = connected.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);

        for (var pass = 0; pass < connected.Count; pass++)
        {
            var moved = false;

            // A self-loop has no column to push its class into: it leaves and returns to one box.
            foreach (var (from, to, _) in edges.Where(e => !e.Self))
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

    /// <summary>
    /// The boxes, stacked down each column, each as tall as the rows it holds.
    /// </summary>
    /// <remarks>
    /// Heights vary, so a row's position cannot be derived from an index the way it could when
    /// every box was the same size — the next box starts below the previous one's real bottom.
    /// </remarks>
    private static List<RelationshipBox> Place(
        Dictionary<string, int> layers,
        Dictionary<string, RelationshipSource> byName,
        List<Edge> edges)
    {
        var boxes = new List<RelationshipBox>();

        foreach (var column in layers.GroupBy(pair => pair.Value).OrderBy(c => c.Key))
        {
            var x = column.Key * (BoxWidth + ColumnGap);
            var y = 0;

            foreach (var name in column.Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal))
            {
                var box = Box(byName[name], x, y, edges);

                boxes.Add(box);

                y += box.Height + RowGap;
            }
        }

        return boxes;
    }

    /// <summary>
    /// One class as a table: its key, the foreign key behind each relationship it owns, and a count
    /// of everything else.
    /// </summary>
    /// <remarks>
    /// Only the fields that explain the picture are drawn. Listing all eleven of Sale's would make
    /// the box the tallest thing on the diagram while saying nothing about what it is joined to —
    /// and the ones that do explain it are exactly the key and the foreign keys.
    /// <para>
    /// The foreign keys are named by the convention rather than looked for in the field list. On the
    /// review page the class does not carry them yet, and showing what generating will add is the
    /// whole point of the picture being there.
    /// </para>
    /// </remarks>
    private static RelationshipBox Box(
        RelationshipSource source, int x, int y, List<Edge> edges)
    {
        var shown = new List<string>();

        var key = source.Fields.FirstOrDefault(
            f => string.Equals(f, "Id", StringComparison.OrdinalIgnoreCase));

        if (key is not null)
            shown.Add(key);

        var foreign = edges
            .Where(e => Same(e.From, source.Name))
            .Select(e => e.Field)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        shown.AddRange(foreign);

        var hidden = source.Fields.Count(f => !shown.Contains(f, StringComparer.OrdinalIgnoreCase));

        var rows = new List<RelationshipRow>();
        var top = y + HeaderHeight + RowsTop;

        for (var i = 0; i < shown.Count; i++)
        {
            var marker = i == 0 && key is not null ? "PK" : "FK";

            rows.Add(new RelationshipRow(shown[i], marker, top + i * RowHeight + RowHeight / 2));
        }

        return new RelationshipBox(
            source.Name, x, y, HeightFor(shown.Count, hidden > 0), rows, hidden);
    }

    /// <summary>The unrelated classes, wrapped across rows under the graph.</summary>
    private static List<RelationshipBox> Band(IReadOnlyList<string> names, int top)
    {
        const int PerRow = 5;
        const int Gap = 18;

        return
        [
            .. names.Select((name, i) => new RelationshipBox(
                name,
                i % PerRow * (BandBoxWidth + Gap),
                top + i / PerRow * (BandBoxHeight + Gap),
                BandBoxHeight,
                [],
                0))
        ];
    }

    /// <summary>
    /// The lines, each leaving the row of the foreign key that carries it.
    /// </summary>
    /// <remarks>
    /// Leaving from the field rather than from the middle of the box is the difference between a
    /// picture that says these two are related and one that says which column relates them — which
    /// is the thing that otherwise means opening the file.
    /// <para>
    /// Lines are grouped by the gap they cross and spaced evenly across it, so no two share a
    /// vertical segment. A self-loop crosses no gap: it leaves the key's row on the right side of
    /// the box and comes back to the key row, each loop of one box in its own lane.
    /// </para>
    /// </remarks>
    private static List<RelationshipArrow> Route(
        List<Edge> edges, Dictionary<string, RelationshipBox> placed)
    {
        var routed = new List<RelationshipArrow>();

        foreach (var loops in edges.Where(e => e.Self).GroupBy(e => placed[e.From]))
        {
            var box = loops.Key;
            var side = box.X + BoxWidth;
            var lane = 0;

            foreach (var edge in loops.OrderBy(e => RowY(box, e.Field)))
            {
                routed.Add(new RelationshipArrow(
                    edge.From, edge.To,
                    side, RowY(box, edge.Field),
                    side, RowY(box, "Id"),
                    side + LoopFirst + lane * LoopLane,
                    edge.Field, Self: true));

                lane++;
            }
        }

        var byGap = edges
            .Where(e => !e.Self)
            .Select(e => (Edge: e, From: placed[e.From], To: placed[e.To]))
            .GroupBy(a => (a.From.X, a.To.X));

        foreach (var gap in byGap)
        {
            var lanes = gap.Count();
            var start = gap.Key.Item1 + BoxWidth;
            var span = gap.Key.Item2 - start;

            var lane = 0;

            foreach (var (edge, from, to) in gap
                         .OrderBy(a => RowY(a.From, a.Edge.Field))
                         .ThenBy(a => a.To.Y))
            {
                lane++;

                routed.Add(new RelationshipArrow(
                    edge.From, edge.To,
                    start, RowY(from, edge.Field),
                    to.X, RowY(to, "Id"),
                    start + span * lane / (lanes + 1),
                    edge.Field));
            }
        }

        return routed;
    }

    /// <summary>The centre of a named row, or of the title band when the box has no such row.</summary>
    private static int RowY(RelationshipBox box, string field) =>
        box.Rows.FirstOrDefault(r => Same(r.Name, field))?.Y
        ?? box.Rows.FirstOrDefault()?.Y
        ?? box.Y + HeaderHeight / 2;

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

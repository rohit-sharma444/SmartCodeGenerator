namespace DataIngestion.Schema;

/// <summary>
/// One column of a read data source: the name as the source spells it, and the C# type its
/// values were found to carry. <see cref="Type"/> is settable because a reader produces the
/// name and <see cref="ColumnTypeInference"/> fills the type in afterwards, over the rows the
/// same reader produced.
/// </summary>
public sealed class TabularColumn
{
    public required string Name { get; init; }

    /// <summary>One of string, int, decimal, bool, DateTime — the five
    /// <c>ModelEditor.SupportedTypes</c> allows. Defaults to string so a column is never
    /// untyped.</summary>
    public string Type { get; set; } = "string";

    /// <summary>
    /// What the source declared about this column. Settable for the same reason
    /// <see cref="Type"/> is: a reader produces the name, and the schema source or
    /// <see cref="ColumnTypeInference"/> fills the facts in afterwards.
    /// </summary>
    public CodeGenerationEngine.Models.TypeFacts? Facts { get; set; }

    /// <summary>
    /// Whether this column's values are the 1-based ROW NUMBERS of the rows it points at — which
    /// is exactly the Id each of those rows is seeded with — rather than values read from the
    /// source. Set only on the foreign keys this layer makes up itself: a JSON child's parent key
    /// and a normalization split's. <c>TabularImport</c> rewrites every OTHER foreign key from
    /// the source key it holds to the seeded Id of the row carrying it, and must leave these alone.
    /// </summary>
    public bool HoldsRowNumbers { get; init; }

    /// <summary>
    /// Whether the source DECLARED this column its table's primary key, on its own. Only SQL Server
    /// declares one; every file reader leaves this false, and the key is then found by its name.
    /// </summary>
    /// <remarks>
    /// Needed because a key is not always called <c>Id</c> or <c>&lt;Table&gt;Id</c>: a ticket
    /// keyed by its <c>TicketNo</c> has its rows renumbered 1..N like any other, and the
    /// references to it must follow, which only the declaration says.
    /// </remarks>
    public bool IsKey { get; init; }
}

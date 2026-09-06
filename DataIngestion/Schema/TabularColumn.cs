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
}

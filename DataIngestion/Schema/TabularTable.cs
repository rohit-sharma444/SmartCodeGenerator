namespace DataIngestion.Schema;

/// <summary>
/// The one shape every data-source reader produces, and the only shape the normalization
/// analyser, <see cref="DataIngestion.DataModelBuilder"/> and the seed importer consume. That
/// is what makes Excel and SQL Server cheap later: they add a reader and touch nothing else.
/// </summary>
/// <remarks>
/// Rows hold RAW STRINGS, not typed values, because CSV, TXT and JSON all arrive as text and
/// typing is an inference over that text. Conversion to the column's type happens once, at
/// seed-writing time, so no stage has to agree with any other about when a value became typed.
/// A cell is null when the source had no value there.
/// </remarks>
public sealed class TabularTable
{
    /// <summary>The table's own name — a SQL table, a spreadsheet sheet, a JSON property, or a
    /// file's base name. Becomes the entity name.</summary>
    public required string Name { get; init; }

    public List<TabularColumn> Columns { get; } = new();

    /// <summary>One entry per row, each as long as <see cref="Columns"/>.</summary>
    public List<string?[]> Rows { get; } = new();

    /// <summary>What to call the source in a message to the user ("Orders.csv"). Falls back to
    /// <see cref="Name"/>.</summary>
    public string? SourceLabel { get; init; }

    public string Label => SourceLabel ?? Name;

    public int IndexOf(string column)
        => Columns.FindIndex(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));

    /// <summary>The cell at <paramref name="row"/> in <paramref name="column"/>, or null when
    /// either is absent. Never throws: a ragged source is normal, not exceptional.</summary>
    public string? Value(int row, string column)
    {
        var index = IndexOf(column);
        if (index < 0 || row < 0 || row >= Rows.Count || index >= Rows[row].Length)
            return null;

        return Rows[row][index];
    }

    public IEnumerable<string?> ColumnValues(string column)
        => Enumerable.Range(0, Rows.Count).Select(r => Value(r, column));
}

using CodeGenerationEngine.Models;

namespace DataIngestion.Schema.Readers.Sql;

/// <summary>
/// Reads the tables the user picked out of a SQL Server database.
/// </summary>
/// <remarks>
/// The one reader that never calls <see cref="ColumnTypeInference"/>. Every other reader produces
/// strings and then asks what they were, which is where the leading-zero and thousands-separator
/// corruptions come from; a database column has a declared type, so the question is never put.
/// </remarks>
/// <param name="maxRowsPerTable">How many rows of one table to read at most. See
/// <see cref="MaxRowsPerTable"/>; a test passes a small one.</param>
public sealed class SqlServerReader(ISqlSchemaSource source, int maxRowsPerTable = SqlServerReader.MaxRowsPerTable)
    : IDataSourceReader
{
    /// <summary>
    /// The most rows read from one table.
    /// </summary>
    /// <remarks>
    /// Only <see cref="ImportedData.MaxSeedRows"/> of them become seed data; the rest are read
    /// for the rule scan, which is why this is far above that and not equal to it. A table past
    /// it is read up to it and marked <see cref="TabularTable.IsPartial"/>, so every rule offered
    /// from it says it came from the rows read rather than claiming the whole table.
    /// </remarks>
    public const int MaxRowsPerTable = 100_000;

    private readonly List<string> _lossNotes = [];

    /// <summary>What the five-type target set cost this read, or empty. See
    /// <see cref="SqlTypeMap.LossNote"/>.</summary>
    public IReadOnlyList<string> LossNotes => _lossNotes;

    public IReadOnlyList<TabularTable> Read(DataSourceSpec spec)
    {
        _lossNotes.Clear();

        var available = source.Tables(spec.Location);
        var wanted = Wanted(spec, available);
        var keys = source.ForeignKeys(spec.Location);

        var read = new List<TabularTable>();

        foreach (var info in wanted)
        {
            var table = new TabularTable
            {
                Name = info.Name,
                SourceLabel = $"{info.Schema}.{info.Name}"
            };

            foreach (var column in source.Columns(spec.Location, info.Schema, info.Name))
            {
                table.Columns.Add(new TabularColumn
                {
                    Name = column.Name,
                    Type = SqlTypeMap.ToModelType(column.SqlType),
                    Facts = new TypeFacts
                    {
                        IsNullable = column.Nullable,
                        // -1 is INFORMATION_SCHEMA's spelling of nvarchar(max): a known
                        // absence of a bound, not a bound of -1.
                        MaxLength = column.MaxLength is > 0 ? column.MaxLength : null,
                        IsUnbounded = column.MaxLength == -1,
                        Precision = column.Precision,
                        Scale = column.Scale,
                        DeclaredType = column.SqlType
                    }
                });

                var note = SqlTypeMap.LossNote(column.SqlType, column.Name);

                if (note is not null)
                {
                    _lossNotes.Add(note);
                    table.Notes.Add($"In {table.Label}, {note}");
                }
            }

            // One past the cap, so a full read PROVES there was more: the catalogue's count is an
            // estimate, and may be stale or missing altogether.
            var rows = source.Rows(spec.Location, info.Schema, info.Name, maxRowsPerTable + 1);
            var partial = rows.Count > maxRowsPerTable;

            table.Rows.AddRange(partial ? rows.Take(maxRowsPerTable) : rows);

            if (partial)
            {
                table.IsPartial = true;

                // Quoted only when it is believable: a count at or below what was just read is
                // stale, and "read 100,000 of 90,000 rows" would be worse than no count.
                table.SourceRowCount = info.Rows > maxRowsPerTable ? info.Rows : null;
            }

            foreach (var key in Carried(keys, table, wanted))
                table.ForeignKeys.Add(key);

            read.Add(table);
        }

        return read;
    }

    /// <summary>
    /// The keys belonging to <paramref name="table"/> that point at something also being read.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The key has to be ON this table — <c>ForeignKeys</c> returns every one
    /// in the database, so without the column test every table would claim all of them. And its
    /// target has to be a table being read, or the relationship becomes a navigation property
    /// typed as a class that is never generated, which does not compile. <c>DataModelBuilder</c>
    /// drops those too; dropping them here as well keeps the table honest about what it carries.
    /// </remarks>
    private static IEnumerable<TabularForeignKey> Carried(
        IReadOnlyList<TabularForeignKey> keys,
        TabularTable table,
        List<SqlTableInfo> wanted)
        => keys.Where(key =>
            key.Column.Length > 0
            && table.Columns.Any(
                c => string.Equals(c.Name, key.Column, StringComparison.OrdinalIgnoreCase))
            && wanted.Any(
                t => string.Equals(t.Name, key.TargetTable, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The tables to read: the ones named, or all of them when none were.
    /// </summary>
    /// <remarks>
    /// Naming nothing can only mean everything — it is what a piped run does, where there is
    /// nobody to ask. A name that is not there is refused rather than skipped: silently producing
    /// a smaller model than was asked for is the failure this codebase keeps guarding against.
    /// </remarks>
    private static List<SqlTableInfo> Wanted(
        DataSourceSpec spec, IReadOnlyList<SqlTableInfo> available)
    {
        if (spec.SqlTables is not { Count: > 0 } names)
            return [.. available];

        var wanted = new List<SqlTableInfo>();

        foreach (var name in names)
        {
            var match = available.FirstOrDefault(
                t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)
                  || string.Equals($"{t.Schema}.{t.Name}", name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                throw new InvalidDataException(
                    $"The database has no table called {name}. It may have been dropped or "
                    + "renamed since the list was read.");

            wanted.Add(match);
        }

        return wanted;
    }
}

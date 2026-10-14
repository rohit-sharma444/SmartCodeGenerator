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
                // -1 is INFORMATION_SCHEMA's spelling of nvarchar(max): a known absence of a
                // bound, not a bound of -1. See TextLength for the lengths that are not what
                // they say.
                var (maxLength, unbounded) = SqlTypeMap.TextLength(column.SqlType, column.MaxLength);

                table.Columns.Add(new TabularColumn
                {
                    Name = column.Name,
                    Type = SqlTypeMap.ToModelType(column.SqlType),
                    IsKey = column.IsPrimaryKey,
                    Facts = new TypeFacts
                    {
                        IsNullable = column.Nullable,

                        // A column the schema declares NULL may be empty whether or not the rows
                        // read happen to hold a NULL: typed int rather than int?, the app had no
                        // way to leave a ticket's number unset, and saved 0 where the database
                        // would have held nothing.
                        HasMissingValues = column.Nullable,
                        MaxLength = maxLength,
                        IsUnbounded = unbounded,
                        Precision = column.Precision,
                        Scale = column.Scale,
                        DeclaredType = column.SqlType
                    }
                });
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

            NoteWhatTheTypesCost(table);

            foreach (var key in Carried(keys, info, table, wanted))
                table.ForeignKeys.Add(key);

            read.Add(table);
        }

        return read;
    }

    /// <summary>
    /// What each of <paramref name="table"/>'s columns loses by being mapped to one of the five
    /// model types, said in its notes, and the two losses that can be avoided avoided.
    /// </summary>
    /// <remarks>
    /// After the rows are read, because both avoidable losses are about the values. A bigint
    /// holding a value beyond int becomes a decimal of whole numbers, which holds every bigint
    /// exactly: as an int, the value was left out of the seed and a NOT NULL row loaded 0, a
    /// number the source never held. A float or real can hold what no decimal can (1.79E+308);
    /// such a value is still left out, but the property becomes nullable so the row loads with
    /// the value MISSING rather than a made-up 0.
    /// </remarks>
    private void NoteWhatTheTypesCost(TabularTable table)
    {
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            var declared = column.Facts?.DeclaredType ?? string.Empty;
            var values = table.Rows.Select(r => i < r.Length ? r[i] : null).Where(v => v is not null).ToList();

            string? note;

            if (string.Equals(declared, "bigint", StringComparison.OrdinalIgnoreCase)
                && values.Any(v => !AppScaffolding.ImportedValues.TryInt(v!, out _)))
            {
                column.Type = "decimal";
                column.Facts = column.Facts! with { Precision = 19, Scale = 0 };
                note = $"{column.Name} is bigint and holds values beyond int, so it becomes a decimal of "
                       + "whole numbers — every value kept.";
            }
            else
            {
                note = SqlTypeMap.LossNote(declared, column.Name);
            }

            if (column.Type == "decimal"
                && values.FirstOrDefault(v => !AppScaffolding.ImportedValues.TryDecimal(v!, out _)) is { } unheld)
            {
                column.Facts = (column.Facts ?? new TypeFacts()) with { HasMissingValues = true };
                note = (note is null ? string.Empty : note + " ")
                       + $"Some of {column.Name}'s values are beyond what a decimal holds (the first being "
                       + $"{unheld}); they are left empty.";
            }

            if (note is null)
                continue;

            _lossNotes.Add(note);
            table.Notes.Add($"In {table.Label}, {note}");
        }
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
    /// <para>
    /// A key that says which table declares it, and which schema its target is in, is matched on
    /// those as well. By column name alone, Customers was handed the Orders key on CustomerID as a
    /// relationship to itself, and every table with a RegionID was given every key on one.
    /// </para>
    /// </remarks>
    private static IEnumerable<TabularForeignKey> Carried(
        IReadOnlyList<TabularForeignKey> keys,
        SqlTableInfo info,
        TabularTable table,
        List<SqlTableInfo> wanted)
        => keys.Where(key =>
            key.Column.Length > 0
            && (key.Table is null || Same(key.Table, key.Schema, info))
            && table.Columns.Any(
                c => string.Equals(c.Name, key.Column, StringComparison.OrdinalIgnoreCase))
            && wanted.Any(t => Same(key.TargetTable, key.TargetSchema, t)));

    /// <summary>Whether <paramref name="name"/> in <paramref name="schema"/> (any schema, when
    /// null) is the table <paramref name="info"/> describes.</summary>
    private static bool Same(string name, string? schema, SqlTableInfo info)
        => string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase)
           && (schema is null || string.Equals(info.Schema, schema, StringComparison.OrdinalIgnoreCase));

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

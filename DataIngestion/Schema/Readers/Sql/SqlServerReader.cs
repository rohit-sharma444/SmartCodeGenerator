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
        var names = TableNames(wanted);

        var read = new List<TabularTable>();

        foreach (var info in wanted)
        {
            var table = new TabularTable
            {
                Name = names[info],
                SourceLabel = $"{info.Schema}.{info.Name}",
                SourceName = string.Equals(names[info], info.Name, StringComparison.Ordinal) ? null : info.Name
            };

            if (!string.Equals(names[info], info.Name, StringComparison.Ordinal))
                table.Notes.Add($"{info.Schema}.{info.Name} is read as {names[info]}, because another schema "
                                + $"also has a table called {info.Name}.");

            var columns = source.Columns(spec.Location, info.Schema, info.Name);

            // A computed column holds no data of its own: the database works it out from the
            // others. As an ordinary field the app let it be typed over, saved what was typed, and
            // proposed rules from it, so it is left out and its expression said instead.
            var stored = new List<int>();

            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].ComputedAs is { } expression)
                {
                    table.Notes.Add($"In {table.Label}, {columns[i].Name} is computed by the database as "
                                    + $"{expression}, so it is left out of the app: it holds no data of its own.");
                    continue;
                }

                stored.Add(i);
            }

            foreach (var column in stored.Select(i => columns[i]))
            {
                // A DEFAULT is what a new row starts with; without it, a new employee began
                // inactive where the database would have made them active.
                var initial = SqlDefault.Parse(column.Default);

                if (initial is null && column.Default is { } unread && !IsNullDefault(unread))
                    table.Notes.Add($"In {table.Label}, {column.Name}'s DEFAULT {unread} was not carried: "
                                    + "a new record starts empty instead.");

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
                        DeclaredType = column.SqlType,
                        DefaultValue = initial?.Value,
                        DefaultFunction = initial?.Function
                    }
                });
            }

            // One past the cap, so a full read PROVES there was more: the catalogue's count is an
            // estimate, and may be stale or missing altogether.
            var rows = source.Rows(spec.Location, info.Schema, info.Name, maxRowsPerTable + 1);
            var partial = rows.Count > maxRowsPerTable;

            // SELECT * returns every column, the computed ones too, in the order Columns lists them.
            table.Rows.AddRange((partial ? rows.Take(maxRowsPerTable) : rows)
                .Select(row => stored.Count == columns.Count
                    ? row
                    : stored.Select(i => i < row.Length ? row[i] : null).ToArray()));

            if (partial)
            {
                table.IsPartial = true;

                // Quoted only when it is believable: a count at or below what was just read is
                // stale, and "read 100,000 of 90,000 rows" would be worse than no count.
                table.SourceRowCount = info.Rows > maxRowsPerTable ? info.Rows : null;
            }

            NoteWhatTheTypesCost(table);
            CarryConstraints(table, source.Constraints(spec.Location, info.Schema, info.Name));

            // A key names its target as the database does; a target read under its schema's name
            // (HrNote) must be named that way here, or the key points at the other schema's table.
            foreach (var key in Carried(keys, info, table, wanted))
                table.ForeignKeys.Add(key with
                {
                    TargetTable = names[wanted.First(t => Same(key.TargetTable, key.TargetSchema, t))]
                });

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

            NoteYearOneDates(table, column, values);

            if (note is null)
                continue;

            _lossNotes.Add(note);
            table.Notes.Add($"In {table.Label}, {note}");
        }
    }

    /// <summary>
    /// What the table's CHECK constraints allow, carried onto the columns they bound as the
    /// range or list of values the generated app enforces, and every one that cannot be carried
    /// said in the notes.
    /// </summary>
    /// <remarks>
    /// Without this the app offered the column TYPE's range instead: a tinyint age with
    /// CHECK (Age BETWEEN 16 AND 99) said "Age must be 0..255", and a status limited to three
    /// words took any text. A check is carried whole or not at all: half of one would claim the
    /// app enforces a rule it only partly does.
    /// </remarks>
    private static void CarryConstraints(TabularTable table, IReadOnlyList<SqlConstraintInfo> constraints)
    {
        // Unique only where its filter holds (WHERE Badge IS NOT NULL), which the app has no
        // rule for: carried as a plain unique, it would refuse rows the database accepts.
        foreach (var filtered in constraints.Where(c => c.Kind == SqlConstraintKind.FilteredUnique))
            table.Notes.Add($"In {table.Label}, the unique index {filtered.Name} ({string.Join(", ", filtered.Columns)}) "
                            + $"applies only {filtered.Definition} and was not carried: the app does not enforce it.");

        foreach (var key in constraints.Where(c => c.Kind is SqlConstraintKind.PrimaryKey or SqlConstraintKind.Unique))
        {
            var members = key.Columns.Select(table.IndexOf).ToList();

            if (members.Count == 0 || members.Any(i => i < 0))
            {
                table.Notes.Add($"In {table.Label}, {key.Name} ({string.Join(", ", key.Columns)}) was not carried: "
                                + "not every one of its columns is in the app.");
                continue;
            }

            foreach (var column in members.Select(i => table.Columns[i]))
            {
                var facts = column.Facts ?? new TypeFacts();
                column.Facts = facts with
                {
                    UniqueKeys = [.. facts.UniqueKeys ?? [], new UniqueKey(key.Name, members.Count)]
                };
            }
        }

        foreach (var check in constraints.Where(c => c.Kind == SqlConstraintKind.Check))
        {
            var bounded = SqlCheck.Parse(check.Definition ?? string.Empty) is { Count: > 0 } bounds
                ? Bounded(table, bounds)
                : null;

            if (bounded is null)
            {
                table.Notes.Add($"In {table.Label}, the check {check.Name} {check.Definition} was not carried: "
                                + "the app does not enforce it, and the database will refuse what breaks it.");
                continue;
            }

            foreach (var (column, facts) in bounded)
                column.Facts = facts;
        }
    }

    /// <summary>
    /// Each column <paramref name="bounds"/> names, with its facts narrowed to them; null when
    /// any column is not in the table or its type cannot carry what the check says.
    /// </summary>
    private static List<(TabularColumn Column, TypeFacts Facts)>? Bounded(
        TabularTable table, IReadOnlyDictionary<string, CheckBounds> bounds)
    {
        var bounded = new List<(TabularColumn, TypeFacts)>();

        foreach (var (name, bound) in bounds)
        {
            var index = table.IndexOf(name);
            if (index < 0)
                return null;

            var column = table.Columns[index];
            var facts = column.Facts ?? new TypeFacts();

            if (bound.Allowed is { } allowed)
            {
                // A list of words for a text column; a list of numbers has nothing to become.
                if (!bound.AllowedAreText || column.Type != "string" || bound.Min is not null || bound.Max is not null)
                    return null;

                bounded.Add((column, facts with
                {
                    AllowedValues = facts.AllowedValues is { } earlier
                        ? [.. earlier.Intersect(allowed, StringComparer.Ordinal)]
                        : allowed
                }));
                continue;
            }

            // The smallest step the column's values move by: a strict bound is the next value
            // past it. Without a known scale, a strict decimal bound has no next value.
            decimal? step = column.Type switch
            {
                "int" => 1m,
                "decimal" when facts.Scale is { } scale and >= 0 and <= 28 => 1m / Pow10(scale),
                _ => null
            };

            if (column.Type is not ("int" or "decimal")
                || (step is null && ((bound.Min is not null && !bound.MinInclusive) || (bound.Max is not null && !bound.MaxInclusive))))
                return null;

            var min = bound.Min is not { } lo ? (decimal?)null
                : step is { } up ? (bound.MinInclusive ? Math.Ceiling(lo / up) * up : (Math.Floor(lo / up) + 1) * up)
                : lo;

            var max = bound.Max is not { } hi ? (decimal?)null
                : step is { } down ? (bound.MaxInclusive ? Math.Floor(hi / down) * down : (Math.Ceiling(hi / down) - 1) * down)
                : hi;

            bounded.Add((column, facts with
            {
                MinValue = min is null ? facts.MinValue : facts.MinValue is { } a ? Math.Max(a, min.Value) : min,
                MaxValue = max is null ? facts.MaxValue : facts.MaxValue is { } b ? Math.Min(b, max.Value) : max
            }));
        }

        return bounded;
    }

    private static decimal Pow10(int power)
    {
        var result = 1m;
        for (var i = 0; i < power; i++)
            result *= 10m;

        return result;
    }

    /// <summary>
    /// Says how many rows of a NOT NULL date column hold 0001-01-01 00:00, when any do.
    /// </summary>
    /// <remarks>
    /// The generated app reads that value as a date never filled in: it is what a non-nullable
    /// DateTime holds when nobody set it, and the required check refuses it on purpose, so a blank
    /// record cannot be saved. A row the source really stored it in is seeded as stored, and fails
    /// that check the first time it is saved, which the user should know before it happens.
    /// </remarks>
    private static void NoteYearOneDates(TabularTable table, TabularColumn column, List<string?> values)
    {
        if (column.Type != "DateTime" || column.Facts?.IsNullable != false)
            return;

        var count = values.Count(v => DateTime.TryParse(
            v, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var date) && date == DateTime.MinValue);

        if (count > 0)
            table.Notes.Add($"In {table.Label}, {count:N0} row(s) of {column.Name} hold 0001-01-01, which the app "
                            + "reads as a date not filled in; those rows fail its required check until the date is changed.");
    }

    /// <summary>Whether a DEFAULT definition is NULL, which gives a new row nothing.</summary>
    private static bool IsNullDefault(string definition)
        => definition.Trim().Trim('(', ')').Trim().Equals("null", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name each table is read under: its own, unless a table in another schema has the same
    /// one, when each outside dbo takes its schema's name in front (hr.Note as HrNote).
    /// </summary>
    /// <remarks>
    /// Read under one name, the second table became the same entity as the first and was skipped,
    /// rows and all, while the summary still counted it as read. When neither is in dbo, every one
    /// of them is renamed: there is no reason to prefer one.
    /// </remarks>
    private static Dictionary<SqlTableInfo, string> TableNames(List<SqlTableInfo> wanted)
    {
        var names = new Dictionary<SqlTableInfo, string>();

        foreach (var group in wanted.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var tables = group.ToList();
            var keepsItsName = tables.Count == 1
                ? tables[0]
                : tables.FirstOrDefault(t => string.Equals(t.Schema, "dbo", StringComparison.OrdinalIgnoreCase));

            foreach (var table in tables)
                names[table] = table == keepsItsName
                    ? table.Name
                    : DomainDiscovery.SeedExtraction.NameNormalizer.ToFieldName(table.Schema) + table.Name;
        }

        return names;
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

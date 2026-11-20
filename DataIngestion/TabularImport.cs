using System.Globalization;
using AppScaffolding;
using DataIngestion.Schema;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion;

/// <summary>
/// Turns read tables into the row set the scaffolder seeds from. Keys are normalized the same way
/// <see cref="DataModelBuilder"/> normalizes them, so every seed row's keys match the properties
/// of the class built from the same table.
/// </summary>
public static class TabularImport
{
    public static ImportedData ToImportedData(IReadOnlyList<TabularTable> tables)
    {
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>(
            StringComparer.OrdinalIgnoreCase);

        var notes = new List<string>();

        // Entity name -> the table its rows came from, for the foreign-key passes below.
        var sources = new Dictionary<string, TabularTable>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            // What the reader had to give up is true whether or not the table becomes a class.
            notes.AddRange(table.Notes);

            var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);

            // The same three guards DataModelBuilder applies, for the same reasons. They must agree:
            // rows imported for a table the builder skipped belong to a class that will never exist.
            if (entityName.Length == 0 || !NameNormalizer.IsValidIdentifier(entityName))
                continue;

            // Said here as well as in `why`: the summary counts the table as read, and its rows
            // are about to go nowhere.
            if (sources.TryGetValue(entityName, out var first))
            {
                notes.Add($"{table.Label} was left out: it would become {entityName}, which {first.Label} "
                          + "already is. Rename one of them to keep both.");
                continue;
            }

            // The FINAL field names, from the same resolution DataModelBuilder uses. Keying by the
            // raw normalized column name instead would silently lose any column that had to be
            // renamed off its class's name: the generated class declares OrderName, the seed row
            // would say Order, and SeedFileWriter — which walks the class — would find nothing.
            var fieldNames = DataModelBuilder.FieldNames(table);

            var imported = new List<IReadOnlyDictionary<string, string?>>();

            // Column -> how many seeded values were re-cased to the spelling its CHECK lists.
            var recased = new Dictionary<TabularColumn, int>();

            // Indexed rather than iterating the rows themselves: List.IndexOf would be O(n²) and,
            // worse, would return the FIRST equal row, so duplicate rows would all read from one.
            for (var rowIndex = 0;
                 rowIndex < Math.Min(table.Rows.Count, ImportedData.MaxSeedRows);
                 rowIndex++)
            {
                var cells = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                foreach (var column in table.Columns)
                {
                    if (fieldNames.TryGetValue(column.Name, out var fieldName))
                        cells[fieldName] = AsListed(column, table.Value(rowIndex, column.Name), recased);
                }

                imported.Add(cells);
            }

            foreach (var (column, count) in recased)
            {
                notes.Add(
                    $"In {table.Label}, {count:N0} {column.Name} value(s) were re-cased to the spelling its CHECK "
                    + $"constraint lists ({string.Join(", ", column.Facts!.AllowedValues!)}), which the database "
                    + "accepted ignoring case and the app compares exactly.");
            }

            if (table.Rows.Count > ImportedData.MaxSeedRows)
            {
                // The table's own count when only part of it was read: "the first 1000 of
                // 100000" would understate a 47-million-row table by the same factor.
                notes.Add(
                    $"Imported the first {ImportedData.MaxSeedRows} of {table.SourceRowCount ?? table.Rows.Count} rows "
                    + $"from {table.Label} as seed data.");
            }

            if (table.IsPartial)
            {
                notes.Add(
                    $"Read {table.Rows.Count:N0} of "
                    + (table.SourceRowCount is { } total ? $"the {total:N0} rows" : "the rows")
                    + $" in {table.Label}, not all of them; any rule offered for it was found in those rows only.");
            }

            rows[entityName] = imported;
            sources[entityName] = table;
        }

        // The same links DataModelBuilder makes relationships of, so every relationship's values
        // are renumbered and nothing that is not one is touched.
        var links = DataModelBuilder.Links(tables);

        var unlinked = new Unlinked();
        RewriteSourceKeys(rows, sources, links, notes, unlinked);
        ClearDanglingForeignKeys(rows, sources, links, unlinked);
        unlinked.AddNotes(notes);

        return new ImportedData(rows, notes);
    }

    /// <summary>References that had to be left unset, by why — each reason is its own note, so
    /// the user is never told a row cap lost a link that their data never had.</summary>
    private sealed class Unlinked
    {
        public int PastTheCap;
        public int NamedNoRow;
        public int NamedTwoRows;

        public void AddNotes(List<string> notes)
        {
            if (PastTheCap > 0)
            {
                notes.Add(
                    $"{PastTheCap} reference(s) could not be linked because the rows they pointed at fell "
                    + $"outside the first {ImportedData.MaxSeedRows} imported, and were left unset.");
            }

            if (NamedNoRow > 0)
            {
                notes.Add(
                    $"{NamedNoRow} reference(s) could not be linked because no row in your data carries "
                    + "the key they name, and were left unset.");
            }

            if (NamedTwoRows > 0)
            {
                notes.Add(
                    $"{NamedTwoRows} reference(s) could not be linked because more than one row carries "
                    + "the key they name, so which was meant is unknown; they were left unset.");
            }
        }
    }

    /// <summary>
    /// Rewrites every foreign key read from the source — a value of the parent's key — to the Id
    /// that parent row is seeded with.
    /// </summary>
    /// <remarks>
    /// <c>SeedFileWriter</c> numbers every seeded row 1..N, whatever key the source gave it (the
    /// key column is dropped, or for a text key kept in a field: <see cref="DataModelBuilder"/>
    /// records which). A child's key left as the source spelled it would then name whichever row
    /// sits at that position: the wrong parent when the keys happen to fall in 1..N, and no parent
    /// at all when they do not, or when they are text ("EUR", "C002").
    /// <para>
    /// Remapped rather than kept: row numbers are what every foreign key this layer makes up
    /// already holds (a JSON child's, a normalization split's), and what
    /// <see cref="ClearDanglingForeignKeys"/> checks against, so bringing the source's own keys
    /// into line leaves one numbering, not two. Those made-up keys have no target column to look
    /// their values up in, and are skipped here.
    /// </para>
    /// <para>
    /// Only links <see cref="DataModelBuilder.Links"/> found: a text column that names no row of
    /// a text-keyed table is the user's data, and rewriting it would replace their value with a
    /// number. A link to the row's own table (a manager) is renumbered like any other.
    /// </para>
    /// </remarks>
    private static void RewriteSourceKeys(
        Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> rows,
        Dictionary<string, TabularTable> sources,
        IReadOnlyDictionary<string, IReadOnlyList<DataModelBuilder.KeyLink>> links,
        List<string> notes,
        Unlinked unlinked)
    {
        // (Entity, column) -> each value of that column -> the 1-based position of the row
        // carrying it, over EVERY row read, so a key past the cap can be told apart from a key
        // that names no row. Built once per column a link names.
        var positions = new Dictionary<(string, string), KeyPositions>();

        KeyPositions PositionsOf(string entity, string column)
        {
            var at = (entity.ToUpperInvariant(), column.ToUpperInvariant());

            if (positions.TryGetValue(at, out var known))
                return known;

            var table = sources[entity];
            var found = new KeyPositions(table.KeysMatchIgnoringCase);

            for (var row = 0; row < table.Rows.Count; row++)
            {
                if (KeyText(table.Value(row, column)) is { } key)
                    found.Add(key, row + 1);
            }

            if (found.SpelledOnlyByCase.Count > 0)
            {
                notes.Add(
                    $"In {table.Label}, {found.SpelledOnlyByCase.Count} {column} value(s) differ from another "
                    + $"only by case (for example {found.SpelledOnlyByCase.First()}), so a reference to one "
                    + "was linked only when it matched it exactly.");
            }

            return positions[at] = found;
        }

        // Target -> the columns its references were renumbered through, for the notes.
        var rewritten = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entity, held) in links)
        {
            if (!rows.TryGetValue(entity, out var entityRows))
                continue;

            foreach (var link in held)
            {
                if (link.TargetColumn is not { } targetColumn
                    || !sources.TryGetValue(link.Target, out var targetTable)
                    || targetTable.IndexOf(targetColumn) < 0
                    || !rows.TryGetValue(link.Target, out var targetRows))
                    continue;

                var keys = PositionsOf(link.Target, targetColumn);
                var imported = targetRows.Count;

                foreach (var row in entityRows)
                {
                    // Mutable in practice: every row in here was built as a Dictionary above.
                    if (row is not Dictionary<string, string?> cells
                        || !cells.TryGetValue(link.Field, out var raw)
                        || KeyText(raw) is not { } key)
                        continue;

                    var position = keys.Find(key);

                    if (position == KeyPositions.Twice)
                    {
                        cells[link.Field] = null;
                        unlinked.NamedTwoRows++;
                    }
                    else if (position == KeyPositions.None)
                    {
                        cells[link.Field] = null;
                        unlinked.NamedNoRow++;
                    }
                    else if (position > imported)
                    {
                        cells[link.Field] = null;
                        unlinked.PastTheCap++;
                    }
                    else
                    {
                        cells[link.Field] = position.ToString(CultureInfo.InvariantCulture);
                    }
                }

                if (!rewritten.TryGetValue(link.Target, out var columns))
                    rewritten[link.Target] = columns = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

                columns.Add(targetColumn);
            }
        }

        foreach (var (target, columns) in rewritten)
        {
            notes.Add(
                $"Each {target} is numbered 1, 2, 3... in the app rather than by its "
                + $"{string.Join(" or ", columns)} in {sources[target].Label}; "
                + $"the references to it were renumbered to match.");
        }
    }

    /// <summary>
    /// <paramref name="value"/> as the CHECK on <paramref name="column"/> spells it, when it equals
    /// exactly one listed value ignoring case: a case-insensitive database stores "open" under
    /// a CHECK listing 'Open', and the app, which compares exactly, refused that seeded record.
    /// </summary>
    /// <remarks>
    /// A value no listed value equals, even ignoring case, is left as it is: it was already
    /// outside the list, and changing it would invent data.
    /// </remarks>
    private static string? AsListed(TabularColumn column, string? value, Dictionary<TabularColumn, int> recased)
    {
        if (value is null || column.Facts?.AllowedValues is not { Count: > 0 } allowed
            || allowed.Contains(value, StringComparer.Ordinal))
            return value;

        var matches = allowed.Where(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        if (matches.Count != 1)
            return value;

        recased[column] = recased.GetValueOrDefault(column) + 1;
        return matches[0];
    }

    /// <summary>
    /// Where each key value of one column sits, 1-based over every row read, and how a
    /// reference finds its row: exactly, else -- for a table whose keys match ignoring case, as
    /// SQL Server's default collation does -- by the one row it equals ignoring case.
    /// </summary>
    private sealed class KeyPositions(bool ignoreCase)
    {
        public const int None = 0;
        public const int Twice = -1;

        private readonly Dictionary<string, int> _exact = new(StringComparer.Ordinal);
        private readonly HashSet<string> _exactTwice = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _folded = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _foldedTwice = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Each key's spellings, by the key ignoring case, when case is ignored.</summary>
        private readonly Dictionary<string, SortedSet<string>> _spellings = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Key values another one equals ignoring case but not exactly, when case is
        /// ignored: a case-sensitive database's A1 and a1.</summary>
        public IReadOnlyList<string> SpelledOnlyByCase
            => [.. _spellings.Values.Where(s => s.Count > 1).SelectMany(s => s)];

        public void Add(string key, int position)
        {
            if (!_exact.TryAdd(key, position))
                _exactTwice.Add(key);

            if (!ignoreCase)
                return;

            if (!_folded.TryAdd(key, position))
                _foldedTwice.Add(key);

            if (!_spellings.TryGetValue(key, out var spellings))
                _spellings[key] = spellings = new SortedSet<string>(StringComparer.Ordinal);

            spellings.Add(key);
        }

        /// <summary>The row <paramref name="key"/> names, <see cref="None"/>, or
        /// <see cref="Twice"/> when more than one row carries it.</summary>
        public int Find(string key)
        {
            if (_exactTwice.Contains(key))
                return Twice;

            if (_exact.TryGetValue(key, out var exact))
                return exact;

            if (!ignoreCase || !_folded.TryGetValue(key, out var folded))
                return None;

            return _foldedTwice.Contains(key) ? Twice : folded;
        }
    }

    /// <summary>A key as one comparable spelling: integers in their plain form, so "007" and
    /// "7" name the same row; anything else trimmed. Null for a blank cell.</summary>
    private static string? KeyText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();

        return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n.ToString(CultureInfo.InvariantCulture)
            : trimmed;
    }

    /// <summary>
    /// Clears any foreign key pointing past the rows its target entity actually contributed.
    /// </summary>
    /// <remarks>
    /// The <see cref="ImportedData.MaxSeedRows"/> cap is applied per entity, so truncating a parent
    /// can leave a child row referencing a parent row that was never imported. Left alone, the
    /// generated app shows an orphan whose lookup silently resolves to nothing.
    /// <para>
    /// The key is CLEARED rather than the row dropped, because seeded Ids are positional — assigned
    /// 1..N in row order at generation time — so removing a row from the middle would renumber every
    /// row after it and break every other foreign key pointing at them. A cleared key writes as an
    /// absent value, which the generated class reads as its default.
    /// </para>
    /// <para>
    /// Runs after every table is imported, not during: an extracted parent (`Customer`) is appended
    /// AFTER the table that references it, so a forward pass would not yet know its row count.
    /// Every link is checked, named for its target or not: a manager's key is a key to Staff.
    /// </para>
    /// </remarks>
    private static void ClearDanglingForeignKeys(
        Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> rows,
        Dictionary<string, TabularTable> sources,
        IReadOnlyDictionary<string, IReadOnlyList<DataModelBuilder.KeyLink>> links,
        Unlinked unlinked)
    {
        foreach (var (entity, held) in links)
        {
            if (!rows.TryGetValue(entity, out var entityRows))
                continue;

            foreach (var link in held)
            {
                if (!rows.TryGetValue(link.Target, out var targetRows))
                    continue;

                foreach (var row in entityRows)
                {
                    // Mutable in practice: every row in here was built as a Dictionary above.
                    if (row is not Dictionary<string, string?> cells
                        || !cells.TryGetValue(link.Field, out var raw)
                        || !int.TryParse(raw, out var key))
                        continue;

                    if (key >= 1 && key <= targetRows.Count)
                        continue;

                    cells[link.Field] = null;

                    // The cap is to blame only for a row that was read and then not imported.
                    if (key > targetRows.Count && key <= sources[link.Target].Rows.Count)
                        unlinked.PastTheCap++;
                    else
                        unlinked.NamedNoRow++;
                }
            }
        }
    }
}

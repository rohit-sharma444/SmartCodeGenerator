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
            if (entityName.Length == 0
                || !NameNormalizer.IsValidIdentifier(entityName)
                || rows.ContainsKey(entityName))
                continue;

            // The FINAL field names, from the same resolution DataModelBuilder uses. Keying by the
            // raw normalized column name instead would silently lose any column that had to be
            // renamed off its class's name: the generated class declares OrderName, the seed row
            // would say Order, and SeedFileWriter — which walks the class — would find nothing.
            var fieldNames = DataModelBuilder.FieldNames(table);

            var imported = new List<IReadOnlyDictionary<string, string?>>();

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
                        cells[fieldName] = table.Value(rowIndex, column.Name);
                }

                imported.Add(cells);
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

        var unlinked = new Unlinked();
        RewriteSourceKeys(rows, sources, notes, unlinked);
        ClearDanglingForeignKeys(rows, sources, unlinked);
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
    /// Rewrites every foreign key read from the source — the parent's own key value — to the Id
    /// that parent row is seeded with.
    /// </summary>
    /// <remarks>
    /// The parent's key column is dropped (<see cref="DataModelBuilder"/> records why) and
    /// <c>SeedFileWriter</c> numbers the seeded rows 1..N instead. A child's key left as the
    /// source spelled it would then name whichever row sits at that position: the wrong parent
    /// when the keys happen to fall in 1..N, and no parent at all when they do not.
    /// <para>
    /// Remapped rather than kept: row numbers are what every foreign key this layer makes up
    /// already holds (a JSON child's, a normalization split's), and what
    /// <see cref="ClearDanglingForeignKeys"/> checks against, so bringing the source's own keys
    /// into line leaves one numbering, not two. Those made-up keys carry
    /// <see cref="TabularColumn.HoldsRowNumbers"/> and are skipped here.
    /// </para>
    /// <para>
    /// Only int columns: a key held as text ("ALFKI") is not something the generated app treats
    /// as a foreign key, and rewriting it would replace the user's value with a number.
    /// </para>
    /// </remarks>
    private static void RewriteSourceKeys(
        Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> rows,
        Dictionary<string, TabularTable> sources,
        List<string> notes,
        Unlinked unlinked)
    {
        // Entity -> its source key -> the 1-based position of the row carrying it, over EVERY
        // row read, so a key past the cap can be told apart from a key that names no row.
        var positions = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        var repeated = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entity, table) in sources)
        {
            if (DataModelBuilder.KeyColumn(table) is not { } keyColumn)
                continue;

            var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var twice = new HashSet<string>(StringComparer.Ordinal);

            for (var row = 0; row < table.Rows.Count; row++)
            {
                if (KeyText(table.Value(row, keyColumn)) is not { } key)
                    continue;

                if (!byKey.TryAdd(key, row + 1))
                    twice.Add(key);
            }

            positions[entity] = byKey;
            repeated[entity] = twice;
        }

        var rewritten = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entity, table) in sources)
        {
            var fieldNames = DataModelBuilder.FieldNames(table);

            foreach (var column in table.Columns)
            {
                if (column.HoldsRowNumbers
                    || column.Type != "int"
                    || !fieldNames.TryGetValue(column.Name, out var field)
                    || !field.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = field[..^2];

                if (string.Equals(target, entity, StringComparison.OrdinalIgnoreCase)
                    || !positions.TryGetValue(target, out var byKey))
                    continue;

                var imported = rows[target].Count;

                foreach (var row in rows[entity])
                {
                    // Mutable in practice: every row in here was built as a Dictionary above.
                    if (row is not Dictionary<string, string?> cells
                        || !cells.TryGetValue(field, out var raw)
                        || KeyText(raw) is not { } key)
                        continue;

                    if (repeated[target].Contains(key))
                    {
                        cells[field] = null;
                        unlinked.NamedTwoRows++;
                    }
                    else if (!byKey.TryGetValue(key, out var position))
                    {
                        cells[field] = null;
                        unlinked.NamedNoRow++;
                    }
                    else if (position > imported)
                    {
                        cells[field] = null;
                        unlinked.PastTheCap++;
                    }
                    else
                    {
                        cells[field] = position.ToString(CultureInfo.InvariantCulture);
                    }
                }

                rewritten.Add(target);
            }
        }

        foreach (var target in rewritten)
        {
            notes.Add(
                $"Each {target} is numbered 1, 2, 3... in the app rather than by its "
                + $"{DataModelBuilder.KeyColumn(sources[target])} in {sources[target].Label}; "
                + $"the references to it were renumbered to match.");
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
    /// </para>
    /// </remarks>
    private static void ClearDanglingForeignKeys(
        Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> rows,
        Dictionary<string, TabularTable> sources,
        Unlinked unlinked)
    {
        foreach (var (_, entityRows) in rows)
        {
            foreach (var row in entityRows)
            {
                // Mutable in practice: every row in here was built as a Dictionary above.
                if (row is not Dictionary<string, string?> cells)
                    continue;

                foreach (var field in cells.Keys.ToList())
                {
                    if (!field.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var target = field[..^2];

                    // Not a foreign key at all if it names no imported entity — an ordinary int
                    // that happens to end in "Id" must be left exactly as it was read.
                    if (target.Length == 0 || !rows.TryGetValue(target, out var targetRows))
                        continue;

                    if (!int.TryParse(cells[field], out var key))
                        continue;

                    if (key >= 1 && key <= targetRows.Count)
                        continue;

                    cells[field] = null;

                    // The cap is to blame only for a row that was read and then not imported.
                    if (key > targetRows.Count && key <= sources[target].Rows.Count)
                        unlinked.PastTheCap++;
                    else
                        unlinked.NamedNoRow++;
                }
            }
        }
    }
}

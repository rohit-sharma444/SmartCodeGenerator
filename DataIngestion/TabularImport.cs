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

        foreach (var table in tables)
        {
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
                notes.Add(
                    $"Imported the first {ImportedData.MaxSeedRows} of {table.Rows.Count} rows "
                    + $"from {table.Label} as seed data.");
            }

            rows[entityName] = imported;
        }

        ClearDanglingForeignKeys(rows, notes);

        return new ImportedData(rows, notes);
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
        List<string> notes)
    {
        var cleared = 0;

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
                    cleared++;
                }
            }
        }

        if (cleared > 0)
        {
            notes.Add(
                $"{cleared} reference(s) could not be linked because the rows they pointed at fell "
                + $"outside the first {ImportedData.MaxSeedRows} imported, and were left unset.");
        }
    }
}

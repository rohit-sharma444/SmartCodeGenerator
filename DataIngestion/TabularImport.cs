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
            if (entityName.Length == 0 || rows.ContainsKey(entityName))
                continue;   // DataModelBuilder skipped this table for the same reason

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
                    var fieldName = NameNormalizer.ToFieldName(column.Name);
                    if (fieldName.Length > 0)
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

        return new ImportedData(rows, notes);
    }
}

using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>
/// The imported rows, keyed by the names the model has now rather than the names the file was read
/// under.
/// </summary>
/// <remarks>
/// The rows are keyed at read time, and a rename on Review changes the model and nothing else - so
/// a renamed field's column, or a renamed class's rows, were silently left behind and the
/// generated app came out without them. Each class and field read from data remembers the name it
/// was imported under (<see cref="CandidateEntity.ImportedAs"/>,
/// <see cref="CandidateField.ImportedAs"/>), and this follows those names at generate time. One
/// place, so the web, the console and a resumed build all get it.
/// </remarks>
public static class ImportedDataRenames
{
    public static ImportedData Follow(ImportedData data, CandidateDomainModel model)
    {
        if (data.IsEmpty)
            return data;

        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>(
            data.Rows, StringComparer.OrdinalIgnoreCase);

        // Every renamed class's old key goes BEFORE any is written, for the reason Moved reads
        // from the original row: two classes that trade names would otherwise have the second
        // rename remove the rows the first had just written under that name.
        foreach (var entity in model.Entities)
        {
            if (entity.ImportedAs is { } source
                && !string.Equals(source, entity.Name, StringComparison.OrdinalIgnoreCase))
                rows.Remove(source);
        }

        foreach (var entity in model.Entities)
        {
            var source = entity.ImportedAs ?? entity.Name;

            if (!data.Rows.TryGetValue(source, out var imported))
                continue;

            var moves = entity.Fields
                .Where(f => f.ImportedAs is not null
                            && !string.Equals(f.ImportedAs, f.Name, StringComparison.OrdinalIgnoreCase))
                .Select(f => (From: f.ImportedAs!, To: f.Name))
                .ToList();

            rows[entity.Name] = moves.Count == 0 ? imported : [.. imported.Select(row => Moved(row, moves))];
        }

        return new ImportedData(rows, data.Notes);
    }

    /// <summary>
    /// One row with each moved value under its new name.
    /// </summary>
    /// <remarks>
    /// Read from the ORIGINAL row and written to a new one, so two fields that trade names trade
    /// values too, instead of the second move reading what the first had just written.
    /// </remarks>
    private static IReadOnlyDictionary<string, string?> Moved(
        IReadOnlyDictionary<string, string?> row, List<(string From, string To)> moves)
    {
        var moved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in row)
        {
            if (!moves.Any(m => string.Equals(m.From, key, StringComparison.OrdinalIgnoreCase)))
                moved[key] = value;
        }

        foreach (var (from, to) in moves)
        {
            if (row.TryGetValue(from, out var value))
                moved[to] = value;
        }

        return moved;
    }
}

namespace DataIngestion.Schema;

/// <summary>What one pass over one column found.</summary>
/// <param name="DistinctValues">Up to <see cref="ColumnScan.DistinctCap"/> distinct non-blank
/// values, in the order first seen.</param>
/// <param name="ExceededDistinctCap">True when the cap was reached, which is how "more than 50"
/// is known without keeping every value of a million-row column.</param>
/// <param name="DuplicateValueCount">How many distinct values occur more than once. Zero is what
/// a uniqueness proposal needs. Counted over EVERY value, not only the capped list: a column of a
/// million references is always past the cap, and that is exactly where uniqueness matters.
/// </param>
public sealed record ColumnScanResult(
    IReadOnlyList<string> DistinctValues,
    bool ExceededDistinctCap,
    int LongestValueLength,
    int DuplicateValueCount,
    int NonBlankRowCount,
    int RowCount,
    string PartialNote = "");

/// <summary>
/// One pass per column over the COMPLETE table, producing every count the rule proposals need.
/// </summary>
/// <remarks>
/// Runs over <see cref="TabularTable"/> and never over <c>ImportedData</c>, which
/// <c>TabularImport</c> caps at <c>ImportedData.MaxSeedRows</c>. A rule inferred from the first
/// thousand rows is a rule inferred from a sample, and the app it generates would reject data its
/// own source contains — the single failure this whole layer exists to prevent.
/// <para>
/// One pass for every output rather than one pass per candidate: the readers have already
/// materialised every row, and walking them four times to answer four questions costs four times
/// as much for nothing.
/// </para>
/// </remarks>
public static class ColumnScan
{
    /// <summary>One more than the largest distinct count that can be a status, so reaching it is
    /// the answer "more than 50".</summary>
    public const int DistinctCap = 51;

    /// <summary>What <paramref name="column"/> of <paramref name="table"/> holds.</summary>
    public static ColumnScanResult Of(TabularTable table, string column)
    {
        var index = table.IndexOf(column);

        if (index < 0)
            return new ColumnScanResult([], false, 0, 0, 0, 0);

        // Ordinal, because the emitted uniqueness check compares with Equals — which is ordinal
        // for strings. A scan that folded case would propose a rule the generated app then
        // refuses the source's own rows against.
        //
        // Two structures, deliberately: `order` is the capped list a status proposal reads, and
        // `seen` is every value, uncapped, so a repeat past the cap still counts. A column of a
        // million references is always past the cap, and it is the one uniqueness matters most
        // for. `seen` costs the memory of the column's distinct values, which the reader has
        // already paid for the rows themselves.
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();

        var type = table.Columns[index].Type;

        var exceeded = false;
        var longest = 0;
        var nonBlank = 0;

        foreach (var row in table.Rows)
        {
            var cell = index < row.Length ? row[index] : null;

            if (string.IsNullOrWhiteSpace(cell))
                continue;

            // Trimmed, because SeedFileWriter trims every value it writes: the generated app
            // holds "Active", never SQL Server's nchar-padded "Active    ". Scanning the padded
            // form proposes an allowed-values list the seed's own rows are not in, and counts
            // "A1" and "A1 " as two values where the app sees one twice.
            var value = cell.Trim();

            nonBlank++;

            if (value.Length > longest)
                longest = value.Length;

            var key = TypedKey(value, type);

            if (seen.TryGetValue(key, out var already))
            {
                seen[key] = already + 1;
                continue;
            }

            seen[key] = 1;

            // Past the cap only the LIST stops growing. Every other count carries on, which is
            // what makes a uniqueness proposal possible for a column that is not a status.
            if (order.Count >= DistinctCap)
                exceeded = true;
            else
                order.Add(value);
        }

        var duplicates = 0;
        foreach (var count in seen.Values)
            if (count > 1)
                duplicates++;

        return new ColumnScanResult(
            order, exceeded, longest, duplicates, nonBlank, table.Rows.Count, table.PartialNote);
    }

    /// <summary>
    /// What <paramref name="value"/> is compared by: its parsed value for a number or a date,
    /// because the generated uniqueness check compares typed values (1.5m equals 1.50m, +5 equals
    /// 5), and the text itself for anything else. Compared as text, "1.5" and "1.50" were two
    /// values, a unique rule was offered, and the seeded rows then failed it.
    /// </summary>
    private static string TypedKey(string value, string type)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;

        return type switch
        {
            "int" when AppScaffolding.ImportedValues.TryInt(value, out var n) => n.ToString(invariant),
            "decimal" when AppScaffolding.ImportedValues.TryPlainDecimal(value, out var d) => d.ToString("G29", invariant),
            "DateTime" when AppScaffolding.ImportedValues.TryDateTime(value, out var t) => t.Ticks.ToString(invariant),
            _ => value
        };
    }
}

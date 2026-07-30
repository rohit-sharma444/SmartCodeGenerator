using System.Globalization;
using DomainDiscovery.Enrichment;

namespace DataIngestion.Schema;

/// <summary>
/// The C# type a column's VALUES carry. Real data is better evidence than a column's name, so
/// this supersedes <see cref="TypeInference"/> on the data path — but only where there is
/// evidence: an all-empty column has none, and falls back to that name heuristic.
/// </summary>
/// <remarks>
/// Only the five types <c>ModelEditor.SupportedTypes</c> allows can be produced. A column of
/// GUIDs is therefore a string and an integer past <see cref="int.MaxValue"/> a decimal, since
/// emitting Guid or long would produce a model the review loop cannot edit.
/// </remarks>
public static class ColumnTypeInference
{
    /// <summary>How many non-empty values are examined. A column's type is a property of its
    /// domain, not of its length, and scanning a million rows to learn "int" is waste.</summary>
    public const int TypeSampleSize = 200;

    /// <summary>Words that mean true or false. A bare 0/1 is deliberately NOT here: a column
    /// wrongly typed bool cannot round-trip a 2, while one wrongly typed int loses nothing.</summary>
    private static readonly HashSet<string> BooleanWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "y", "n"
    };

    public static string Infer(IEnumerable<string?> values, string columnName)
    {
        var sample = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Take(TypeSampleSize)
            .ToList();

        if (sample.Count == 0)
            return TypeInference.Infer(columnName);

        // Ordered narrowest-first. int before decimal so "3" is a count, not money; bool after
        // both so the word forms are all that can reach it; DateTime last of the non-strings,
        // because a yyyymmdd date parses as int and is caught above (visible in the review loop,
        // and fixable there with `field`).
        if (sample.All(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            return "int";

        if (sample.All(v => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
            return "decimal";

        if (sample.All(BooleanWords.Contains))
            return "bool";

        if (sample.All(v => DateTime.TryParse(
                v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            return "DateTime";

        return "string";
    }
}

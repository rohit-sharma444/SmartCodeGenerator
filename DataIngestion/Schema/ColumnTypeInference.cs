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

    /// <summary>
    /// What a decimal may look like: a sign, digits, and a decimal point. Deliberately WITHOUT
    /// <see cref="NumberStyles.AllowThousands"/> — see <see cref="Infer"/>.
    /// </summary>
    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite
        | NumberStyles.AllowTrailingWhite;

    /// <summary>
    /// The date formats a column may be typed <c>DateTime</c> from. Every one is UNAMBIGUOUS —
    /// year-first, or with a spelled-out month.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTime.TryParse(string, IFormatProvider, DateTimeStyles, out DateTime)"/> is far
    /// too permissive for inference: it accepts <c>"1,2"</c> and <c>"1/2"</c> as dates, so a column
    /// of fractions becomes a date column. Worse, it reads a slash date as MONTH-first, so a UK
    /// <c>01/02/2026</c> silently becomes 2 January when the user meant 1 February.
    /// <para>
    /// Slash dates are therefore deliberately absent. <c>dd/MM</c> and <c>MM/dd</c> cannot be told
    /// apart from the data, and a value left as a string is preserved exactly and retypeable in the
    /// review loop — where a misread date is simply wrong, with nothing to show that it is.
    /// </para>
    /// </remarks>
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/MM/dd",
        "d MMM yyyy",
        "d MMMM yyyy",
        "MMM d, yyyy",
        "MMMM d, yyyy",
    ];

    private static bool IsDate(string value)
        => DateTime.TryParseExact(
            value, DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out _);

    /// <summary>
    /// Whether <paramref name="value"/> carries a significant leading zero — <c>007</c>, <c>01</c> —
    /// as opposed to a lone <c>0</c> or an ordinary <c>0.99</c>, where the zero is arithmetic rather
    /// than padding.
    /// </summary>
    private static bool IsZeroPadded(string value)
    {
        var digits = value.Length > 0 && (value[0] == '-' || value[0] == '+')
            ? value[1..]
            : value;

        return digits.Length >= 2 && digits[0] == '0' && char.IsAsciiDigit(digits[1]);
    }

    public static string Infer(IEnumerable<string?> values, string columnName)
    {
        var sample = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Take(TypeSampleSize)
            .ToList();

        if (sample.Count == 0)
            return TypeInference.Infer(columnName);

        // A zero-padded value is a code, not a quantity, and its padding is meaning: "007" stored
        // as the number 7 is a silent change to the user's own data. One such value disqualifies
        // the whole column from being numeric at all.
        if (sample.Any(IsZeroPadded))
            return "string";

        // Ordered narrowest-first. int before decimal so "3" is a count, not money; bool after
        // both so the word forms are all that can reach it; DateTime last of the non-strings,
        // because a yyyymmdd date parses as int and is caught above (visible in the review loop,
        // and fixable there with `field`).
        if (sample.All(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            return "int";

        // NumberStyles.Number MINUS AllowThousands. With thousands separators allowed, "1,2" reads
        // as the number twelve — a silent corruption — where without them it stays text, which is
        // merely imprecise and is exactly what the review loop's `field` verb exists to correct.
        if (sample.All(v => decimal.TryParse(v, DecimalStyles, CultureInfo.InvariantCulture, out _)))
            return "decimal";

        if (sample.All(BooleanWords.Contains))
            return "bool";

        if (sample.All(IsDate))
            return "DateTime";

        return "string";
    }
}

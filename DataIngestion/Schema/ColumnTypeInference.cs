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

    /// <remarks>
    /// EVERY value is examined, in one pass. A type chosen from the first rows is a guess about the
    /// rest, and a wrong guess is paid for by the seed writer, which drops a value its type cannot
    /// hold — so a later "n/a" in an "int" column simply vanished. The pass is linear, keeps no
    /// copy of the column, and stops as soon as nothing but string is left.
    /// </remarks>
    public static string Infer(IEnumerable<string?> values, string columnName)
    {
        var seen = false;
        var isInt = true;
        var isDecimal = true;
        var isBool = true;
        var isDate = true;

        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var value = raw.Trim();
            seen = true;

            // A zero-padded value is a code, not a quantity, and its padding is meaning: "007"
            // stored as the number 7 is a silent change to the user's own data. One such value
            // disqualifies the whole column from being numeric at all.
            if (IsZeroPadded(value))
                return "string";

            isInt = isInt && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

            // NumberStyles.Number MINUS AllowThousands. With thousands separators allowed, "1,2"
            // reads as the number twelve — a silent corruption — where without them it stays text,
            // which is merely imprecise and is exactly what the review loop's `field` verb exists
            // to correct.
            isDecimal = isDecimal
                && decimal.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out _);

            isBool = isBool && BooleanWords.Contains(value);
            isDate = isDate && IsDate(value);

            if (!isInt && !isDecimal && !isBool && !isDate)
                return "string";
        }

        if (!seen)
            return TypeInference.Infer(columnName);

        // Ordered narrowest-first. int before decimal so "3" is a count, not money; bool after
        // both so the word forms are all that can reach it; DateTime last of the non-strings,
        // because a yyyymmdd date parses as int and is caught above (visible in the review loop,
        // and fixable there with `field`).
        if (isInt)
            return "int";

        if (isDecimal)
            return "decimal";

        if (isBool)
            return "bool";

        return isDate ? "DateTime" : "string";
    }
}

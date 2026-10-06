using AppScaffolding;
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

            // The seed writer's own parsers, so whatever type is chosen here, every value in the
            // column is one it can write. See ImportedValues for why each is as strict as it is.
            // Decimal is the PLAIN form: exponent-form text ("1E3") is a code, not a number.
            isInt = isInt && ImportedValues.TryInt(value, out _);
            isDecimal = isDecimal && ImportedValues.TryPlainDecimal(value, out _);
            isBool = isBool && ImportedValues.TryBoolean(value, out _);
            isDate = isDate && ImportedValues.TryDateTime(value, out _);

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

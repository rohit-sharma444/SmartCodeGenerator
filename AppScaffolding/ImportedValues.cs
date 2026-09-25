using System.Globalization;

namespace AppScaffolding;

/// <summary>
/// What an imported value may look like to be read as each model type — the ONE definition that
/// both column type inference (in DataIngestion) and <see cref="SeedFileWriter"/> use.
/// </summary>
/// <remarks>
/// There used to be two, and they disagreed in both directions. The seed writer allowed thousands
/// separators, so "1,2" in a column retyped to decimal became twelve, and read slash dates
/// month-first; inference refused both. Meanwhile the seed writer refused the exponent a SQL float
/// is rendered with. Every rule here is invariant-culture and deliberately strict: a value that is
/// not read is kept as text or reported, where a value misread is simply wrong.
/// </remarks>
public static class ImportedValues
{
    private const NumberStyles IntegerStyles = NumberStyles.Integer;

    /// <summary>
    /// A sign, digits, a decimal point and an exponent. Deliberately WITHOUT
    /// <see cref="NumberStyles.AllowThousands"/>: with it, "1,2" reads as the number twelve.
    /// </summary>
    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent
        | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    /// <summary>
    /// The date formats a value may be read as a <c>DateTime</c> from. Every one is UNAMBIGUOUS —
    /// year-first, or with a spelled-out month.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTime.TryParse(string, IFormatProvider, DateTimeStyles, out DateTime)"/> is far
    /// too permissive: it accepts <c>"1,2"</c> and <c>"1/2"</c> as dates, and it reads a slash date
    /// as MONTH-first, so a UK <c>01/02/2026</c> silently becomes 2 January when the user meant
    /// 1 February. <c>dd/MM</c> and <c>MM/dd</c> cannot be told apart from the data, so slash dates
    /// are absent.
    /// </remarks>
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/MM/dd",
        "d MMM yyyy",
        "d MMMM yyyy",
        "MMM d, yyyy",
        "MMMM d, yyyy",
    ];

    private const DateTimeStyles DateStyles = DateTimeStyles.AllowWhiteSpaces;

    public static bool TryInt(string value, out int result)
        => int.TryParse(value, IntegerStyles, CultureInfo.InvariantCulture, out result);

    public static bool TryDecimal(string value, out decimal result)
        => decimal.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// The words that mean true or false, and a bare 1 or 0. Inference still never CHOOSES bool
    /// for a 0/1 column — it tries int first — but a column the user retyped to bool reads them.
    /// </summary>
    public static bool TryBoolean(string value, out bool result)
    {
        switch (value.ToLowerInvariant())
        {
            case "true" or "yes" or "y" or "1":
                result = true;
                return true;
            case "false" or "no" or "n" or "0":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    public static bool TryDateTime(string value, out DateTime result)
        => DateTime.TryParseExact(
            value, DateFormats, CultureInfo.InvariantCulture, DateStyles, out result);
}

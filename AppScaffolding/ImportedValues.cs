using System.Globalization;
using System.Text.RegularExpressions;

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

    private const DateTimeStyles DateStyles =
        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind;

    /// <summary>A trailing "Z" or a numeric UTC offset — the only two <see cref="DateFormats"/>
    /// entries a zone can appear in.</summary>
    private static readonly Regex ZoneSuffix = new(@"[Zz]$|[+-]\d{2}:\d{2}$", RegexOptions.None);

    public static bool TryInt(string value, out int result)
        => int.TryParse(value, IntegerStyles, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// A number as a value of a column already TYPED decimal — by a SQL schema, or by the user in
    /// the review loop. Accepts the exponent a SQL float is rendered with ("1E-05").
    /// </summary>
    public static bool TryDecimal(string value, out decimal result)
        => decimal.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// Whether TEXT reads as a number on its own evidence — what column type inference asks. The
    /// same as <see cref="TryDecimal"/> but WITHOUT the exponent: in a CSV, "1E3" is a code (batch
    /// 1E3), and reading it as 1000 silently changes the user's data. A value read as text is
    /// imprecise; a value read as the wrong number is corrupt.
    /// </summary>
    public static bool TryPlainDecimal(string value, out decimal result)
    {
        result = 0;
        return value.IndexOfAny(['e', 'E']) < 0
            && decimal.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// A value the source's own FORMAT declares numeric (a JSON number, an Excel numeric cell) in
    /// plain digits: "1E-05" becomes "0.00001". Exponent-form text is read as a code by inference,
    /// so a number written that way must lose the exponent before it gets there. A value that is
    /// not exponent-form, or that no decimal can hold, is returned unchanged.
    /// </summary>
    public static string PlainNumberText(string raw)
        => raw.IndexOfAny(['e', 'E']) >= 0
            && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : raw;

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

    /// <summary>
    /// Only for a value that NAMES a zone. <see cref="DateTimeOffset.TryParseExact"/> otherwise
    /// invents the generating machine's own offset for a string that named none, and a
    /// <see cref="DateTime"/> of <see cref="DateTimeKind.Local"/> cannot hold an arbitrary source
    /// offset at all — formatting it always reports the CURRENT machine's, silently reweighting the
    /// value by the difference. Written out through this instead, the source's own offset survives.
    /// </summary>
    public static bool TryDateTimeOffset(string value, out DateTimeOffset result)
    {
        if (!ZoneSuffix.IsMatch(value.Trim()))
        {
            result = default;
            return false;
        }

        return DateTimeOffset.TryParseExact(
            value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces,
            out result);
    }
}

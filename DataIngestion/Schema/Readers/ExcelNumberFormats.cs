using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// What a cell's style says about its number: whether it is really a date, and whether its
/// leading zeros are part of the value.
/// </summary>
/// <remarks>
/// A cell's <c>s</c> attribute indexes <c>cellXfs</c> in <c>xl/styles.xml</c>, whose
/// <c>numFmtId</c> is either one of Excel's builtins or a custom code declared in the same part.
/// That indirection is the ONLY place a workbook records that a number is a date.
/// </remarks>
public sealed class ExcelNumberFormats
{
    /// <summary>
    /// Builtin ids whose formats are dates or times: 14-22 the date and time formats, 27-36 and
    /// 50-58 their locale-specific equivalents, 45-47 the time-only and elapsed forms. Everything
    /// else builtin — 37-44 accounting, 0-13 general and numeric, 48-49 scientific and text — is a
    /// number.
    /// </summary>
    private static readonly HashSet<int> BuiltinDateIds =
    [
        14, 15, 16, 17, 18, 19, 20, 21, 22,
        27, 28, 29, 30, 31, 32, 33, 34, 35, 36,
        45, 46, 47,
        50, 51, 52, 53, 54, 55, 56, 57, 58,
    ];

    /// <summary>Style index to the number format id it uses.</summary>
    private readonly List<int> styles = new();

    /// <summary>Format id to its custom code, for ids the builtin table does not cover.</summary>
    private readonly Dictionary<int, string> codes = new();

    private ExcelNumberFormats() { }

    public static ExcelNumberFormats Load(ZipArchive zip)
    {
        var formats = new ExcelNumberFormats();
        var part = ExcelWorkbook.Entry(zip, "xl/styles.xml");

        // No styles part means no cell is formatted as anything, which is a real workbook and not
        // an error — everything stays the number it is.
        if (part is null)
            return formats;

        var root = ExcelWorkbook.Load(part).Root;

        foreach (var numFmt in root?.Element(ExcelWorkbook.Main + "numFmts")
                                    ?.Elements(ExcelWorkbook.Main + "numFmt")
                               ?? Enumerable.Empty<XElement>())
        {
            var id = numFmt.Attribute("numFmtId")?.Value;
            var code = numFmt.Attribute("formatCode")?.Value;

            if (code is not null
                && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key))
                formats.codes[key] = code;
        }

        foreach (var xf in root?.Element(ExcelWorkbook.Main + "cellXfs")
                                ?.Elements(ExcelWorkbook.Main + "xf")
                           ?? Enumerable.Empty<XElement>())
        {
            formats.styles.Add(
                int.TryParse(xf.Attribute("numFmtId")?.Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var id)
                    ? id
                    : 0);
        }

        return formats;
    }

    /// <summary>Whether the number in a cell of this style is a date or a time.</summary>
    public bool IsDate(int styleIndex)
    {
        var id = FormatId(styleIndex);

        if (id is null)
            return false;

        if (BuiltinDateIds.Contains(id.Value))
            return true;

        return codes.TryGetValue(id.Value, out var code) && HasDateTokens(code);
    }

    /// <summary>
    /// The width a cell of this style pads its number to, or null when it does not pad. Deliberately
    /// narrow: the code must be nothing but two or more zeros. Anything else is an ordinary number
    /// format, and padding one would invent digits the file never held.
    /// </summary>
    public int? PadWidth(int styleIndex)
    {
        var id = FormatId(styleIndex);

        if (id is null || !codes.TryGetValue(id.Value, out var code))
            return null;

        return code.Length >= 2 && code.All(c => c == '0') ? code.Length : null;
    }

    /// <summary>The format id a style index uses. Null when the index names no style, which a
    /// file written by something other than Excel can do.</summary>
    private int? FormatId(int styleIndex)
        => styleIndex >= 0 && styleIndex < styles.Count ? styles[styleIndex] : null;

    /// <summary>
    /// Whether a format code contains real date or time tokens.
    /// </summary>
    /// <remarks>
    /// A false positive here is a corruption — 9.99 read as a date becomes 9 January 1900 — so the
    /// scan strips everything that only LOOKS like a token first: a quoted literal (<c>0.00" m"</c>
    /// is a unit), a backslash escape (<c>0.0\m</c>), and a bracketed section, which is a colour
    /// (<c>[Red]</c>) or a locale (<c>[$-409]</c>) unless it is an elapsed-time token
    /// (<c>[h]</c>). Only the first section is examined: the rest describe negatives and zero,
    /// which cannot disagree about being a date.
    /// </remarks>
    private static bool HasDateTokens(string code)
    {
        var section = code.Split(';')[0];
        var scan = new StringBuilder();

        for (var i = 0; i < section.Length; i++)
        {
            var c = section[i];

            if (c == '"')
            {
                var end = section.IndexOf('"', i + 1);
                if (end < 0)
                    break;

                i = end;
                continue;
            }

            if (c == '\\' || c == '_' || c == '*')
            {
                i++;   // the next character is a literal, or a fill/skip argument
                continue;
            }

            if (c == '[')
            {
                var end = section.IndexOf(']', i + 1);
                if (end < 0)
                    break;

                var inner = section[(i + 1)..end];

                // [h], [mm], [ss] are elapsed-time tokens and count; [Red] and [$-409] do not.
                if (inner.Length > 0 && inner.All(x => x is 'h' or 'H' or 'm' or 'M' or 's' or 'S'))
                    scan.Append(inner);

                i = end;
                continue;
            }

            scan.Append(c);
        }

        return scan.ToString().Any(
            c => c is 'y' or 'Y' or 'd' or 'D' or 'h' or 'H' or 's' or 'S' or 'm' or 'M');
    }
}

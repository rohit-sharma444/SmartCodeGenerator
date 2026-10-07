using System.Globalization;
using System.Xml.Linq;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// One worksheet's cells as the records <see cref="CsvReader.ToTable"/> consumes.
/// </summary>
/// <remarks>
/// Excel's cell model is not a grid of values. A cell's type lives in its <c>t</c> attribute, its
/// text may live in a shared table, its date-ness lives in a number format two parts away, and an
/// empty cell is not written at all. Every one of those is decided here so that everything
/// downstream sees the same raw strings a CSV produces.
/// </remarks>
public static class ExcelSheetReader
{
    public static IReadOnlyList<string?[]> ReadRecords(XDocument sheet, ExcelWorkbook workbook)
    {
        var records = new List<string?[]>();

        var rows = sheet.Root?.Element(ExcelWorkbook.Main + "sheetData")
                              ?.Elements(ExcelWorkbook.Main + "row")
                   ?? Enumerable.Empty<XElement>();

        foreach (var row in rows)
        {
            var cells = new List<string?>();
            var written = false;

            // Where an unreferenced cell goes: the column after the last one placed.
            var cursor = 0;

            foreach (var cell in row.Elements(ExcelWorkbook.Main + "c"))
            {
                var index = ColumnIndex(cell.Attribute("r")?.Value) ?? cursor;

                while (cells.Count < index)
                    cells.Add(null);

                var value = CellValue(cell, workbook);
                written |= HasContent(cell);

                if (cells.Count == index)
                    cells.Add(value);
                else
                    cells[index] = value;   // a repeated reference: the last one wins

                cursor = index + 1;
            }

            // A used range that overruns its data is ordinary, and a row of empty cells is not a
            // row of nulls — the same rule CsvReader.ParseFields applies to a blank line. The test
            // is what the file wrote, not what could be read from it: a row holding one #N/A is a
            // row the user filled in, and dropping it would shorten the table. CsvReader's other
            // half of that rule — a line with a delimiter is a record however empty — has no
            // analogue here, because Excel writes styled-but-empty cells across the used range.
            if (written)
                records.Add(cells.ToArray());
        }

        return records;
    }

    /// <summary>
    /// The 0-based column a cell reference names: <c>A1</c> is 0, <c>AA1</c> is 26. Null when the
    /// reference has no letters, which is the only case a caller has to substitute for.
    /// </summary>
    public static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
            return null;

        var index = 0;
        var letters = 0;

        foreach (var c in reference)
        {
            if (!char.IsAsciiLetter(c))
                break;

            index = (index * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
            letters++;
        }

        return letters == 0 ? null : index - 1;
    }

    /// <summary>
    /// Whether the file wrote anything in this cell. An empty cell is normally not written at all,
    /// but a cell carrying only a style or a stale reference is written empty, and that is the one
    /// the used range overruns with.
    /// </summary>
    private static bool HasContent(XElement cell)
        => cell.Element(ExcelWorkbook.Main + "v") is not null
        || cell.Element(ExcelWorkbook.Main + "is") is not null;

    private static string? CellValue(XElement cell, ExcelWorkbook workbook)
    {
        var type = cell.Attribute("t")?.Value ?? "n";

        // A #REF! or #N/A is the absence of a value, which is what an empty CSV cell means too.
        // Read as its text it would drag an otherwise numeric column to string.
        if (type == "e")
            return null;

        if (type == "inlineStr")
            return cell.Element(ExcelWorkbook.Main + "is") is { } inline ? Text(inline) : null;

        var raw = cell.Element(ExcelWorkbook.Main + "v")?.Value;

        // No cached value at all. A formula in a workbook that was never calculated looks like
        // this, and there is nothing to read.
        if (raw is null)
            return null;

        switch (type)
        {
            case "s":
                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture,
                           out var index)
                       && index >= 0 && index < workbook.SharedStrings.Count
                    ? workbook.SharedStrings[index]
                    : null;

            // Words rather than 0/1: ColumnTypeInference matches true/false and deliberately
            // refuses a bare digit, so the raw form would type the column int.
            case "b":
                return raw == "1" ? "true" : "false";

            // A formula's cached text result, and a literal string written as one. Neither is a
            // number, so neither goes near the number formats.
            case "str":
                return raw;

            default:
                return Number(cell, raw, workbook);
        }
    }

    /// <summary>
    /// A numeric cell. The stored text is returned unchanged unless the cell's number format says
    /// the number is really a date, or that its leading zeros are part of it.
    /// </summary>
    private static string Number(XElement cell, string raw, ExcelWorkbook workbook)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return raw;   // typed numeric but is not a number: pass it through untouched

        var style = int.TryParse(cell.Attribute("s")?.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var s) ? s : 0;

        if (workbook.Formats.IsDate(style))
            return ExcelSerialDate.ToText(number, workbook.Date1904) ?? raw;

        if (workbook.Formats.PadWidth(style) is { } width)
            return Padded(number, width) ?? raw;

        // Everything else is emitted exactly as stored. Applying the display format would inject
        // a thousands separator that ColumnTypeInference refuses to parse, turning a clean decimal
        // column into strings; the stored value is what the file actually holds. Only an exponent
        // is dropped ("1E-05" -> "0.00001"): the cell is numeric, and inference reads
        // exponent-form TEXT as a code.
        return AppScaffolding.ImportedValues.PlainNumberText(raw);
    }

    /// <summary>
    /// A whole number padded to <paramref name="width"/>. Null when the value cannot carry padding
    /// meaningfully, in which case the caller keeps the stored text.
    /// </summary>
    private static string? Padded(double number, int width)
    {
        if (number != Math.Floor(number) || Math.Abs(number) > long.MaxValue)
            return null;

        var value = (long)number;
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

        return value < 0 ? "-" + digits : digits;
    }

    /// <summary>An inline string's text, runs and all, on the same rule as the shared table.</summary>
    private static string Text(XElement element)
        => string.Concat(
            element.Elements(ExcelWorkbook.Main + "t")
                .Concat(element.Elements(ExcelWorkbook.Main + "r").Elements(ExcelWorkbook.Main + "t"))
                .Select(t => t.Value));
}

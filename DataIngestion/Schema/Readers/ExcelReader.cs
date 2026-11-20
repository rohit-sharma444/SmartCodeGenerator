using System.IO.Compression;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// Reads an <c>.xlsx</c> workbook: one visible sheet becomes one table.
/// </summary>
/// <remarks>
/// An <c>.xlsx</c> is a zip of XML, so this needs nothing but
/// <see cref="System.IO.Compression"/> and <see cref="System.Xml.Linq"/> — the same reason
/// <see cref="CsvReader"/> gives for being hand-written: one place, no new dependency, and every
/// rule visible where it is tested. The header and typing step IS
/// <see cref="CsvReader.ToTable"/>, so column naming and type inference cannot drift from what
/// CSV, TXT and JSON do.
/// </remarks>
public sealed class ExcelReader : IDataSourceReader
{
    /// <summary>
    /// The OLE2 compound-file signature every legacy <c>.xls</c> starts with.
    /// </summary>
    private static readonly byte[] Ole2Signature =
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public IReadOnlyList<TabularTable> Read(DataSourceSpec spec)
    {
        if (!File.Exists(spec.Location))
            throw new FileNotFoundException($"No file at {spec.Location}.", spec.Location);

        var label = Path.GetFileName(spec.Location);

        GuardLegacyXls(spec.Location, label);

        using var zip = OpenZip(spec.Location, label);
        var workbook = ExcelWorkbook.Open(zip, label);

        var tables = new List<TabularTable>();
        var skipped = new List<string>();

        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.Hidden)
            {
                skipped.Add($"{sheet.Name} is hidden");
                continue;
            }

            var part = ExcelWorkbook.Entry(zip, sheet.PartPath);
            if (part is null)
            {
                skipped.Add($"{sheet.Name} is missing from the file");
                continue;
            }

            var records = ExcelSheetReader.ReadRecords(ExcelWorkbook.Load(part), workbook);

            // ToTable rejects both of these, and rightly so for a whole file — but a workbook is
            // several sheets, and one blank one must not take the rest of them down.
            if (records.Count == 0)
            {
                skipped.Add($"{sheet.Name} is empty");
                continue;
            }

            if (records.Count == 1)
            {
                skipped.Add($"{sheet.Name} has a header but no rows");
                continue;
            }

            // The sheet's own name, unnormalized: DataModelBuilder normalizes and singularizes it,
            // and RECORDS a decision when a name will not do. Normalizing here would hide that.
            var sheetLabel = $"{label} [{sheet.Name}]";
            var shaped = Shape(records, sheetLabel, out var notes);
            var table = CsvReader.ToTable(sheet.Name, sheetLabel, shaped);
            table.Notes.AddRange(notes.Select(note => note(table)));
            tables.Add(table);
        }

        if (tables.Count == 0)
        {
            throw new InvalidDataException(
                $"{label} has no sheet with data: {string.Join(", ", skipped)}. Add a header row "
                + "and at least one row of data to a visible sheet, or build the app from a "
                + "description instead.");
        }

        return tables;
    }

    /// <summary>
    /// A sheet's records with the header found and every cell kept: leading title rows are
    /// skipped, and a cell to the right of the header's last column gets a numbered column of its
    /// own rather than being cut off. Each change is named in a note, made once the table (and so
    /// the numbered column's name) exists.
    /// </summary>
    /// <remarks>
    /// A title row is one filled cell above a row with several ("Sales Report 2024" above
    /// Region, Units, Revenue). Read as the header, it made a one-column table, and every other
    /// column was cut to that width with nothing said. A one-column sheet is left alone: its
    /// next row is not wider. A row with fewer cells is not named, unlike in a text file: Excel
    /// writes nothing for the empty cells at the end of a row.
    /// </remarks>
    private static IReadOnlyList<string?[]> Shape(
        IReadOnlyList<string?[]> records, string label, out List<Func<TabularTable, string>> notes)
    {
        notes = [];

        var skipped = new List<string>();
        var start = 0;
        while (start + 2 < records.Count
               && Filled(records[start]) == 1
               && Filled(records[start + 1]) > 1)
        {
            skipped.Add(records[start].First(cell => !string.IsNullOrWhiteSpace(cell))!.Trim());
            start++;
        }

        if (skipped.Count > 0)
        {
            var header = string.Join(", ", records[start].Where(cell => !string.IsNullOrWhiteSpace(cell)));
            notes.Add(_ =>
                $"In {label}, {(skipped.Count == 1 ? "a title row" : $"{skipped.Count} title rows")} "
                + $"above the header ({header}) {(skipped.Count == 1 ? "was" : "were")} skipped: "
                + $"{string.Join("; ", skipped)}.");
        }

        var shaped = records.Skip(start).ToList();
        var width = shaped[0].Length;

        var wider = new List<string>();
        var widest = width;
        for (var r = 1; r < shaped.Count; r++)
        {
            var last = LastFilled(shaped[r]);
            if (last < width)
                continue;

            wider.Add($"row {r} ({shaped[r][0]?.Trim()})");
            widest = Math.Max(widest, last + 1);
        }

        if (wider.Count > 0)
        {
            // Padded with nulls, which ToTable names Column<n> by position.
            var header = new string?[widest];
            Array.Copy(shaped[0], header, width);
            shaped[0] = header;

            var added = widest - width;
            notes.Add(table =>
                $"In {label}, {(wider.Count == 1 ? "1 row has" : $"{wider.Count} rows have")} values "
                + "to the right of the header's last column, so "
                + $"{(added == 1 ? "a column was" : $"{added} columns were")} added to keep them: "
                + $"{string.Join(", ", table.Columns.Skip(width).Select(c => c.Name))}. "
                + $"Rows: {string.Join(", ", wider)}.");
        }

        return shaped;

        static int Filled(string?[] record) => record.Count(cell => !string.IsNullOrWhiteSpace(cell));

        static int LastFilled(string?[] record)
        {
            for (var i = record.Length - 1; i >= 0; i--)
                if (!string.IsNullOrWhiteSpace(record[i]))
                    return i;
            return -1;
        }
    }

    /// <summary>
    /// Refuses a legacy binary <c>.xls</c> with the one instruction that fixes it. Runs before the
    /// zip is opened, because otherwise the user gets "End of Central Directory record could not
    /// be found" — true, and useless.
    /// </summary>
    private static void GuardLegacyXls(string path, string label)
    {
        var header = new byte[Ole2Signature.Length];

        using (var stream = File.OpenRead(path))
        {
            // Shorter than the signature: not an .xls, and whatever it is the zip open will say so.
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false)
                < header.Length)
                return;
        }

        if (!header.AsSpan().SequenceEqual(Ole2Signature))
            return;

        throw new InvalidDataException(
            $"{label} is the old binary .xls format, not .xlsx. Open it in Excel and use "
            + "Save As > Excel Workbook (*.xlsx), then point build app at the .xlsx.");
    }

    private static ZipArchive OpenZip(string path, string label)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(
                $"{label} is not a readable .xlsx file. If it was renamed from another format, "
                + "open it in Excel and save it as .xlsx.");
        }
    }
}

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
            tables.Add(CsvReader.ToTable(sheet.Name, $"{label} [{sheet.Name}]", records));
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

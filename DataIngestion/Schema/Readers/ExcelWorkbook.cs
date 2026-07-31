using System.IO.Compression;
using System.Xml.Linq;

namespace DataIngestion.Schema.Readers;

/// <summary>One worksheet as the workbook declares it: its name, the zip entry holding its
/// cells, and whether the user hid it.</summary>
public sealed record ExcelSheet(string Name, string PartPath, bool Hidden);

/// <summary>
/// The parts of an <c>.xlsx</c> that describe the workbook rather than its cells: which sheets
/// exist and where, the shared string table, the number formats, and which date epoch the file
/// counts from.
/// </summary>
/// <remarks>
/// An <c>.xlsx</c> is a zip of XML parts wired together by relationship ids, so a sheet's cells
/// cannot be found without reading two parts first. Keeping that plumbing here leaves
/// <see cref="ExcelSheetReader"/> free to be only about cells.
/// </remarks>
public sealed class ExcelWorkbook
{
    public const string WorkbookPart = "xl/workbook.xml";
    private const string RelationshipsPart = "xl/_rels/workbook.xml.rels";
    private const string SharedStringsPart = "xl/sharedStrings.xml";

    /// <summary>SpreadsheetML itself.</summary>
    public static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Where a sheet's <c>r:id</c> attribute lives.</summary>
    public static readonly XNamespace DocRels =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>The relationships part's own namespace — a different one, confusingly.</summary>
    public static readonly XNamespace PackageRels =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public required IReadOnlyList<ExcelSheet> Sheets { get; init; }

    /// <summary>Indexed by a <c>t="s"</c> cell's value. Empty when the part is absent, which is
    /// what a workbook of nothing but numbers looks like.</summary>
    public required IReadOnlyList<string> SharedStrings { get; init; }

    public required ExcelNumberFormats Formats { get; init; }

    /// <summary>Whether serials count from 1904-01-01 rather than 1900-01-01 — the old Mac
    /// default, and still what some exports carry.</summary>
    public required bool Date1904 { get; init; }

    public static ExcelWorkbook Open(ZipArchive zip, string label)
    {
        var part = Entry(zip, WorkbookPart)
            ?? throw new InvalidDataException(
                $"{label} is a zip file but not an Excel workbook.");

        var workbook = Load(part);
        var targets = Relationships(zip);
        var sheets = new List<ExcelSheet>();

        foreach (var element in workbook.Root?.Element(Main + "sheets")?.Elements(Main + "sheet")
                                ?? Enumerable.Empty<XElement>())
        {
            var name = element.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // Without its relationship there is no way to know which part holds the cells, so the
            // sheet is unreadable rather than empty. Skipping it lets the rest of the file work.
            var id = element.Attribute(DocRels + "id")?.Value;
            if (id is null || !targets.TryGetValue(id, out var target))
                continue;

            var state = element.Attribute("state")?.Value;
            var hidden = string.Equals(state, "hidden", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(state, "veryHidden", StringComparison.OrdinalIgnoreCase);

            sheets.Add(new ExcelSheet(name, target, hidden));
        }

        if (sheets.Count == 0)
            throw new InvalidDataException(
                $"{label} declares no worksheets, so there is nothing to read. Open it in Excel "
                + "and check it still has its sheets.");

        var date1904 = workbook.Root?.Element(Main + "workbookPr")?.Attribute("date1904")?.Value
            is "1" or "true";

        return new ExcelWorkbook
        {
            Sheets = sheets,
            SharedStrings = ReadSharedStrings(zip),
            Formats = ExcelNumberFormats.Load(zip),
            Date1904 = date1904,
        };
    }

    /// <summary>
    /// A part by path. Falls back to a case-insensitive scan because the zip index is
    /// case-sensitive while writers are not reliably consistent about part casing.
    /// </summary>
    public static ZipArchiveEntry? Entry(ZipArchive zip, string path)
        => zip.GetEntry(path)
           ?? zip.Entries.FirstOrDefault(
               e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Parses a part. The default <see cref="System.Xml.XmlReader"/> settings are kept
    /// deliberately: a workbook is a file someone was emailed, and the defaults refuse a DTD, so a
    /// document declaring external entities is rejected rather than fetched.
    /// </summary>
    public static XDocument Load(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>Relationship id to the zip path it points at.</summary>
    private static Dictionary<string, string> Relationships(ZipArchive zip)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var part = Entry(zip, RelationshipsPart);

        if (part is null)
            return map;

        foreach (var relationship in Load(part).Root?.Elements(PackageRels + "Relationship")
                                     ?? Enumerable.Empty<XElement>())
        {
            var id = relationship.Attribute("Id")?.Value;
            var target = relationship.Attribute("Target")?.Value;

            if (id is not null && !string.IsNullOrWhiteSpace(target))
                map[id] = Resolve(target);
        }

        return map;
    }

    /// <summary>
    /// A relationship target as a zip path. A target is relative to the part that declared it —
    /// <c>xl/</c> here — unless it starts with a slash, in which case it is package-absolute.
    /// </summary>
    private static string Resolve(string target)
    {
        if (target.StartsWith('/'))
            return target[1..];

        var segments = new List<string> { "xl" };

        foreach (var segment in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// The string table. Each entry is the concatenation of its runs, because Excel splits a
    /// value into runs wherever part of it is formatted differently — bold one word and "Ada
    /// Lovelace" becomes two runs of one value. A phonetic run (<c>rPh</c>) is a CJK reading
    /// aid rather than part of the value, and is excluded by taking only direct children.
    /// </summary>
    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive zip)
    {
        var part = Entry(zip, SharedStringsPart);
        if (part is null)
            return [];

        var items = new List<string>();

        foreach (var item in Load(part).Root?.Elements(Main + "si")
                             ?? Enumerable.Empty<XElement>())
        {
            items.Add(string.Concat(
                item.Elements(Main + "t")
                    .Concat(item.Elements(Main + "r").Elements(Main + "t"))
                    .Select(t => t.Value)));
        }

        return items;
    }
}

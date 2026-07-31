using System.IO.Compression;
using System.Text;

namespace DataIngestion.Tests.Schema.Readers;

/// <summary>
/// Writes a minimal but genuinely openable <c>.xlsx</c> from XML supplied by the test.
/// </summary>
/// <remarks>
/// Committing binary workbooks would put unreviewable bytes in the repo, and the cases that
/// matter most — a sparse row, a shared string split across runs, a 1904-epoch workbook — cannot
/// be produced by hand in Excel reliably. Here every byte a test depends on is visible in that
/// test. The package is complete enough (content types, package rels) that a fixture can also be
/// opened in Excel when a rule needs checking against the real thing.
/// </remarks>
internal static class ExcelFixture
{
    /// <summary>One sheet. <paramref name="Xml"/> is the INNER xml of &lt;sheetData&gt;.</summary>
    internal sealed record Sheet(string Name, string Xml, bool Hidden = false);

    /// <summary>A unique temp path, so parallel tests never collide on one file.</summary>
    public static string Path(string extension)
        => System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + extension);

    /// <summary>A &lt;row&gt; wrapping already-formed &lt;c&gt; elements.</summary>
    public static string Row(int number, params string[] cells)
        => $"<row r=\"{number}\">{string.Concat(cells)}</row>";

    /// <summary>A cell. <paramref name="type"/> is the <c>t</c> attribute, null for numeric;
    /// <paramref name="style"/> is the <c>s</c> attribute, null for the default style.</summary>
    public static string Cell(string reference, string? value, string? type = null, int? style = null)
    {
        var attributes = new StringBuilder($" r=\"{reference}\"");
        if (type is not null)
            attributes.Append($" t=\"{type}\"");
        if (style is not null)
            attributes.Append($" s=\"{style}\"");

        return value is null
            ? $"<c{attributes}/>"
            : $"<c{attributes}><v>{value}</v></c>";
    }

    /// <summary>An inline-string cell, whose text lives in the cell rather than the string table.</summary>
    public static string InlineCell(string reference, string text)
        => $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{text}</t></is></c>";

    /// <summary>A shared string table from already-formed &lt;si&gt; bodies.</summary>
    public static string SharedStrings(params string[] items)
        => "<?xml version=\"1.0\"?>"
         + "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
         + string.Concat(items.Select(i => $"<si>{i}</si>"))
         + "</sst>";

    /// <summary>
    /// Writes the package. <paramref name="sharedStrings"/> and <paramref name="styles"/> are whole
    /// part documents, or null to omit the part entirely — which is what a workbook with no strings
    /// and no styles really looks like.
    /// </summary>
    public static string Write(
        string path, string? sharedStrings, string? styles, params Sheet[] sheets)
        => Write(path, sharedStrings, styles, date1904: false, sheets);

    public static string Write(
        string path, string? sharedStrings, string? styles, bool date1904, params Sheet[] sheets)
    {
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var sheetRefs = new StringBuilder();
        var rels = new StringBuilder();
        var overrides = new StringBuilder();

        for (var i = 0; i < sheets.Length; i++)
        {
            var id = i + 1;
            var state = sheets[i].Hidden ? " state=\"hidden\"" : string.Empty;

            sheetRefs.Append(
                $"<sheet name=\"{sheets[i].Name}\" sheetId=\"{id}\"{state} r:id=\"rId{id}\"/>");

            rels.Append(
                $"<Relationship Id=\"rId{id}\" Target=\"worksheets/sheet{id}.xml\" "
                + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\"/>");

            overrides.Append(
                $"<Override PartName=\"/xl/worksheets/sheet{id}.xml\" ContentType=\"application/"
                + "vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");

            Add(zip, $"xl/worksheets/sheet{id}.xml",
                "<?xml version=\"1.0\"?>"
                + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + $"<sheetData>{sheets[i].Xml}</sheetData></worksheet>");
        }

        var pr = date1904 ? "<workbookPr date1904=\"1\"/>" : string.Empty;

        Add(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\"?>"
            + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + pr + $"<sheets>{sheetRefs}</sheets></workbook>");

        if (sharedStrings is not null)
        {
            Add(zip, "xl/sharedStrings.xml", sharedStrings);
            rels.Append(
                "<Relationship Id=\"rIdSst\" Target=\"sharedStrings.xml\" "
                + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\"/>");
        }

        if (styles is not null)
        {
            Add(zip, "xl/styles.xml", styles);
            rels.Append(
                "<Relationship Id=\"rIdStyles\" Target=\"styles.xml\" "
                + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\"/>");
        }

        Add(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + rels + "</Relationships>");

        Add(zip, "_rels/.rels",
            "<?xml version=\"1.0\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Target=\"xl/workbook.xml\" "
            + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/"
            + "officeDocument\"/></Relationships>");

        Add(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\"?>"
            + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/"
            + "vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/"
            + "vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
            + overrides + "</Types>");

        return path;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var entry = zip.CreateEntry(name).Open();
        using var writer = new StreamWriter(entry, new UTF8Encoding(false));
        writer.Write(content);
    }
}

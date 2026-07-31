# Excel (.xlsx) data source — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `build app: <description> from excel <path>` reads an `.xlsx` workbook directly — one entity per visible sheet — with no new dependency and no change downstream of the reader seam.

**Architecture:** An `.xlsx` is a ZIP of XML. Four new files in `DataIngestion/Schema/Readers/` do the work: `ExcelWorkbook` (OPC plumbing — sheets, shared strings, the `date1904` flag), `ExcelNumberFormats` (which style ids mean "date" and which mean "zero-padded"), `ExcelSheetReader` (one worksheet XML → records, owning every cell rule), and `ExcelReader` (the `IDataSourceReader`, which delegates the header/typing step to the existing `CsvReader.ToTable`). Wiring is one `case` in `DataSourceReaders.TryCreate`.

**Tech Stack:** C# / net10.0, `System.IO.Compression.ZipArchive`, `System.Xml.Linq.XDocument`, xUnit (`DataIngestion.Tests`), MSTest (`CodeGenerator.Tests`).

**Spec:** [`docs/superpowers/specs/2026-07-31-excel-data-source-design.md`](../specs/2026-07-31-excel-data-source-design.md)

## Global Constraints

- **No new NuGet package.** `ZipArchive` and `XDocument` are in the BCL. `DataIngestion.csproj` gains no `PackageReference`.
- **Namespace** for all new production files: `DataIngestion.Schema.Readers`. Test namespace: `DataIngestion.Tests.Schema.Readers`.
- **Never invent a value.** Anything not confidently understood is emitted as the raw stored text, or `null`. An imprecise value is fixable in the review loop with `field`; a wrong one is invisible.
- **`InvalidDataException` messages must say what to do next**, per the `IDataSourceReader` contract. They start with the file's label (`orders.xlsx`).
- **Rows carry raw strings.** `TabularTable.Rows` is `List<string?[]>`; typing is `ColumnTypeInference`'s job, reached through `CsvReader.ToTable`.
- **No binary test fixtures.** Every workbook a test uses is written by `ExcelFixture` from XML in the test body.
- **Verify with the CI command**, not the local one: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`, run from `D:\SmartCodeGenerator-Repository`. CI runs one of the five test projects; all five run locally.
- **Red before green.** Every test is run and seen to fail for the stated reason before the implementation is written.
- Branch: `feat/excel-data-source` (already created, spec already committed).

---

### Task 1: The fixture helper and `ExcelWorkbook`

Reads the parts that describe the workbook: which sheets exist, where their XML lives, whether they are hidden, the shared string table, and the date epoch.

**Files:**
- Create: `DataIngestion.Tests/Schema/Readers/ExcelFixture.cs`
- Create: `DataIngestion/Schema/Readers/ExcelWorkbook.cs`
- Test: `DataIngestion.Tests/Schema/Readers/ExcelWorkbookTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `ExcelFixture.Write(string path, string? sharedStrings, string? styles, params ExcelFixture.Sheet[] sheets)` → `string` (the path), where `ExcelFixture.Sheet` is `record Sheet(string Name, string Xml, bool Hidden = false)` and `Xml` is the **inner XML of `<sheetData>`** (a sequence of `<row>` elements).
  - `ExcelFixture.Row(int number, params string[] cells)` → `string`, and `ExcelFixture.Path(string extension)` → `string` (a unique temp path).
  - `sealed record ExcelSheet(string Name, string PartPath, bool Hidden)`
  - `sealed class ExcelWorkbook` with `IReadOnlyList<ExcelSheet> Sheets`, `IReadOnlyList<string> SharedStrings`, `ExcelNumberFormats Formats`, `bool Date1904`; statics `Open(ZipArchive zip, string label)` → `ExcelWorkbook`, `Entry(ZipArchive zip, string path)` → `ZipArchiveEntry?`, `Load(ZipArchiveEntry entry)` → `XDocument`.
  - Constants: `ExcelWorkbook.Main`, `ExcelWorkbook.DocRels`, `ExcelWorkbook.PackageRels` (`XNamespace`), and `ExcelWorkbook.WorkbookPart = "xl/workbook.xml"`.

> `ExcelNumberFormats` does not exist until Task 3. In this task, add a **temporary** stub so the solution compiles: a file `DataIngestion/Schema/Readers/ExcelNumberFormats.cs` containing only
> ```csharp
> namespace DataIngestion.Schema.Readers;
>
> public sealed class ExcelNumberFormats
> {
>     public static ExcelNumberFormats Load(System.IO.Compression.ZipArchive zip) => new();
> }
> ```
> Task 3 replaces its body. Do not add `IsDate`/`PadWidth` yet — Task 3's tests must be able to fail.

- [ ] **Step 1: Write the fixture helper**

Create `DataIngestion.Tests/Schema/Readers/ExcelFixture.cs`:

```csharp
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
```

- [ ] **Step 2: Write the failing tests**

Create `DataIngestion.Tests/Schema/Readers/ExcelWorkbookTests.cs`:

```csharp
using System.IO.Compression;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class ExcelWorkbookTests
{
    private static ExcelWorkbook Open(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return ExcelWorkbook.Open(zip, System.IO.Path.GetFileName(path));
    }

    private static T WithWorkbook<T>(Func<ExcelWorkbook, T> use, string? sharedStrings,
        bool date1904, params ExcelFixture.Sheet[] sheets)
    {
        var path = ExcelFixture.Path(".xlsx");
        ExcelFixture.Write(path, sharedStrings, null, date1904, sheets);
        try { return use(Open(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ListsEverySheet_WithTheNameTheWorkbookGivesIt()
    {
        var sheets = WithWorkbook(w => w.Sheets, null, false,
            new ExcelFixture.Sheet("Orders", ExcelFixture.Row(1)),
            new ExcelFixture.Sheet("Customers", ExcelFixture.Row(1)));

        Assert.Equal(new[] { "Orders", "Customers" }, sheets.Select(s => s.Name));
        Assert.Equal("xl/worksheets/sheet1.xml", sheets[0].PartPath);
        Assert.Equal("xl/worksheets/sheet2.xml", sheets[1].PartPath);
    }

    /// <summary>A hidden sheet is still declared; the reader decides what to do with it.</summary>
    [Fact]
    public void RecordsWhetherASheetIsHidden()
    {
        var sheets = WithWorkbook(w => w.Sheets, null, false,
            new ExcelFixture.Sheet("Orders", ExcelFixture.Row(1)),
            new ExcelFixture.Sheet("Lookups", ExcelFixture.Row(1), Hidden: true));

        Assert.False(sheets[0].Hidden);
        Assert.True(sheets[1].Hidden);
    }

    [Fact]
    public void ReadsTheSharedStringTableInOrder()
    {
        var strings = WithWorkbook(w => w.SharedStrings,
            ExcelFixture.SharedStrings("<t>Ada</t>", "<t>Grace</t>"), false,
            new ExcelFixture.Sheet("Orders", ExcelFixture.Row(1)));

        Assert.Equal(new[] { "Ada", "Grace" }, strings);
    }

    /// <summary>Excel splits a value across runs whenever part of it is formatted differently.
    /// Read one run at a time, "Ada Lovelace" arrives as "Ada".</summary>
    [Fact]
    public void SharedString_SplitAcrossRuns_IsOneValue()
    {
        var strings = WithWorkbook(w => w.SharedStrings,
            ExcelFixture.SharedStrings("<r><t xml:space=\"preserve\">Ada </t></r><r><t>Lovelace</t></r>"),
            false, new ExcelFixture.Sheet("Orders", ExcelFixture.Row(1)));

        Assert.Equal("Ada Lovelace", Assert.Single(strings));
    }

    /// <summary>A phonetic run is a reading aid for CJK text, not part of the value.</summary>
    [Fact]
    public void SharedString_IgnoresPhoneticRuns()
    {
        var strings = WithWorkbook(w => w.SharedStrings,
            ExcelFixture.SharedStrings("<t>東京</t><rPh sb=\"0\" eb=\"2\"><t>トウキョウ</t></rPh>"),
            false, new ExcelFixture.Sheet("Orders", ExcelFixture.Row(1)));

        Assert.Equal("東京", Assert.Single(strings));
    }

    [Fact]
    public void NoSharedStringsPart_IsAnEmptyTable_NotAFailure()
    {
        var strings = WithWorkbook(w => w.SharedStrings, null, false,
            new ExcelFixture.Sheet("Orders", ExcelFixture.Row(1)));

        Assert.Empty(strings);
    }

    [Fact]
    public void ReadsTheDate1904Flag()
    {
        Assert.True(WithWorkbook(w => w.Date1904, null, true,
            new ExcelFixture.Sheet("Orders", ExcelFixture.Row(1))));

        Assert.False(WithWorkbook(w => w.Date1904, null, false,
            new ExcelFixture.Sheet("Orders", ExcelFixture.Row(1))));
    }

    /// <summary>A zip that is not a workbook must say so, not fail on a null somewhere later.</summary>
    [Fact]
    public void AZipWithNoWorkbookPart_IsRejectedWithAMessageThatSaysWhy()
    {
        var path = ExcelFixture.Path(".xlsx");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("readme.txt").Open())
            entry.Write("not a workbook"u8);

        try
        {
            var error = Assert.Throws<InvalidDataException>(() => Open(path));
            Assert.Contains("not an Excel workbook", error.Message);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ExcelWorkbookTests"
```

Expected: a **build** failure — `ExcelWorkbook` does not exist (CS0246). That is the correct red for a new type.

- [ ] **Step 4: Write `ExcelNumberFormats` as the stub described above**

Create `DataIngestion/Schema/Readers/ExcelNumberFormats.cs` with exactly the stub shown in this task's Interfaces block.

- [ ] **Step 5: Write `ExcelWorkbook`**

Create `DataIngestion/Schema/Readers/ExcelWorkbook.cs`:

```csharp
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
            SharedStrings = SharedStrings(zip),
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
    /// Parses a part. The default <see cref="XmlReader"/> settings are kept deliberately: a
    /// workbook is a file someone was emailed, and the defaults refuse a DTD, so a document
    /// declaring external entities is rejected rather than fetched.
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
    private static IReadOnlyList<string> SharedStrings(ZipArchive zip)
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
```

- [ ] **Step 6: Run the tests and confirm they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ExcelWorkbookTests"
```

Expected: all 8 pass.

- [ ] **Step 7: Commit**

```bash
git add DataIngestion/Schema/Readers/ExcelWorkbook.cs \
        DataIngestion/Schema/Readers/ExcelNumberFormats.cs \
        DataIngestion.Tests/Schema/Readers/ExcelFixture.cs \
        DataIngestion.Tests/Schema/Readers/ExcelWorkbookTests.cs
git commit -m "feat(data): read an xlsx workbook's sheets and shared strings"
```

---

### Task 2: `ExcelSheetReader` — cells to records

Turns one worksheet's XML into the `string?[]` records `CsvReader.ToTable` consumes. Owns cell addressing and every cell type except the date and padding rules, which arrive in Task 3.

**Files:**
- Create: `DataIngestion/Schema/Readers/ExcelSheetReader.cs`
- Test: `DataIngestion.Tests/Schema/Readers/ExcelSheetReaderTests.cs`

**Interfaces:**
- Consumes: `ExcelWorkbook` (`SharedStrings`, `Formats`, `Date1904`, `Main`, `Load`, `Entry`), `ExcelFixture`.
- Produces:
  - `ExcelSheetReader.ReadRecords(XDocument sheet, ExcelWorkbook workbook)` → `IReadOnlyList<string?[]>`
  - `ExcelSheetReader.ColumnIndex(string? reference)` → `int?` (0-based; null when the reference carries no column letters)

- [ ] **Step 1: Write the failing tests**

Create `DataIngestion.Tests/Schema/Readers/ExcelSheetReaderTests.cs`:

```csharp
using System.IO.Compression;
using System.Xml.Linq;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class ExcelSheetReaderTests
{
    /// <summary>Reads one sheet's rows through a real package, so the test exercises the same
    /// path the reader does rather than a hand-built XDocument that might differ.</summary>
    private static IReadOnlyList<string?[]> Records(
        string sheetXml, string? sharedStrings = null, string? styles = null, bool date1904 = false)
    {
        var path = ExcelFixture.Path(".xlsx");
        ExcelFixture.Write(path, sharedStrings, styles, date1904,
            new ExcelFixture.Sheet("Orders", sheetXml));

        try
        {
            using var zip = ZipFile.OpenRead(path);
            var workbook = ExcelWorkbook.Open(zip, "orders.xlsx");
            var part = ExcelWorkbook.Entry(zip, workbook.Sheets[0].PartPath)!;
            return ExcelSheetReader.ReadRecords(ExcelWorkbook.Load(part), workbook);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("A1", 0)]
    [InlineData("B7", 1)]
    [InlineData("Z1", 25)]
    [InlineData("AA1", 26)]
    [InlineData("AB10", 27)]
    [InlineData("BA1", 52)]
    public void ColumnIndex_DecodesTheLetters(string reference, int expected)
        => Assert.Equal(expected, ExcelSheetReader.ColumnIndex(reference));

    [Theory]
    [InlineData("7")]
    [InlineData("")]
    [InlineData(null)]
    public void ColumnIndex_IsNullWhenThereAreNoLetters(string? reference)
        => Assert.Null(ExcelSheetReader.ColumnIndex(reference));

    [Fact]
    public void ReadsNumericCells_AsTheirStoredText()
    {
        var records = Records(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", "1"), ExcelFixture.Cell("B1", "9.99")));

        Assert.Equal(new[] { "1", "9.99" }, Assert.Single(records));
    }

    /// <summary>
    /// Excel omits an empty cell entirely, so a row's cells must be placed by their reference. Read
    /// in document order instead, every value after the first gap lands one column early — the
    /// single most damaging bug this reader can have, because the file still parses.
    /// </summary>
    [Fact]
    public void SparseRow_PlacesEachCellAtItsOwnColumn()
    {
        var records = Records(
            ExcelFixture.Row(1,
                ExcelFixture.Cell("A1", "1"),
                ExcelFixture.Cell("C1", "3"),
                ExcelFixture.Cell("E1", "5")));

        Assert.Equal(new[] { "1", null, "3", null, "5" }, Assert.Single(records));
    }

    [Fact]
    public void ACellWithNoReference_FollowsTheOneBeforeIt()
    {
        var records = Records("<row r=\"1\"><c><v>1</v></c><c><v>2</v></c></row>");

        Assert.Equal(new[] { "1", "2" }, Assert.Single(records));
    }

    [Fact]
    public void SharedStringCells_ResolveThroughTheStringTable()
    {
        var records = Records(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", "1"), ExcelFixture.Cell("B1", "0", "s")),
            ExcelFixture.SharedStrings("<t>Ada</t>"));

        Assert.Equal(new[] { "1", "Ada" }, Assert.Single(records));
    }

    /// <summary>An index past the table is a broken file, and inventing a value for it would be
    /// worse than admitting the cell is unreadable.</summary>
    [Fact]
    public void ASharedStringIndexPastTheTable_IsNull()
    {
        var records = Records(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", "9", "s")),
            ExcelFixture.SharedStrings("<t>Ada</t>"));

        Assert.Null(Assert.Single(records)[0]);
    }

    [Fact]
    public void InlineStringCells_ReadTheirOwnText()
    {
        var records = Records(ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Ada")));

        Assert.Equal("Ada", Assert.Single(records)[0]);
    }

    /// <summary>A formula's cached string result is a value like any other.</summary>
    [Fact]
    public void FormulaStringCells_ReadTheirCachedValue()
    {
        var records = Records(
            "<row r=\"1\"><c r=\"A1\" t=\"str\"><f>CONCAT(B1,C1)</f><v>Ada L</v></c></row>");

        Assert.Equal("Ada L", Assert.Single(records)[0]);
    }

    /// <summary>Words, not 0/1: ColumnTypeInference matches true/false and deliberately does not
    /// match a bare digit, so the raw form would type the column int.</summary>
    [Fact]
    public void BooleanCells_ReadAsWords()
    {
        var records = Records(
            ExcelFixture.Row(1,
                ExcelFixture.Cell("A1", "1", "b"), ExcelFixture.Cell("B1", "0", "b")));

        Assert.Equal(new[] { "true", "false" }, Assert.Single(records));
    }

    /// <summary>#N/A is the absence of a value, and that is exactly what an empty CSV cell means.
    /// Read as the text "#N/A" it would drag a whole numeric column to string.</summary>
    [Fact]
    public void ErrorCells_AreNull()
    {
        var records = Records(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", "#N/A", "e")));

        Assert.Null(Assert.Single(records)[0]);
    }

    [Fact]
    public void ACellWithNoValue_IsNull()
    {
        var records = Records(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", null), ExcelFixture.Cell("B1", "2")));

        Assert.Equal(new string?[] { null, "2" }, Assert.Single(records));
    }

    /// <summary>A workbook's used range routinely overruns its data, and a row of empty cells is
    /// not a row of nulls — the same rule CsvReader applies to a blank line.</summary>
    [Fact]
    public void RowsOfNothing_AreNotRecords()
    {
        var records = Records(string.Concat(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", "1")),
            ExcelFixture.Row(2, ExcelFixture.Cell("A2", null), ExcelFixture.Cell("B2", null)),
            ExcelFixture.Row(3),
            ExcelFixture.Row(4, ExcelFixture.Cell("A4", "4"))));

        Assert.Equal(2, records.Count);
        Assert.Equal("4", records[1][0]);
    }

    [Fact]
    public void AnEmptySheet_ReadsAsNoRecords()
        => Assert.Empty(Records(string.Empty));
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ExcelSheetReaderTests"
```

Expected: build failure — `ExcelSheetReader` does not exist (CS0246).

- [ ] **Step 3: Write `ExcelSheetReader`**

Create `DataIngestion/Schema/Readers/ExcelSheetReader.cs`:

```csharp
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
        // column into strings; the stored value is what the file actually holds.
        return raw;
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
```

> This references `ExcelSerialDate` and `ExcelNumberFormats.IsDate`/`PadWidth`, which Task 3 writes. To keep this task's tests runnable, temporarily add to the `ExcelNumberFormats` stub:
> ```csharp
>     public bool IsDate(int styleIndex) => false;
>     public int? PadWidth(int styleIndex) => null;
> ```
> and create `DataIngestion/Schema/Readers/ExcelSerialDate.cs` with:
> ```csharp
> namespace DataIngestion.Schema.Readers;
>
> public static class ExcelSerialDate
> {
>     public static string? ToText(double serial, bool date1904) => null;
> }
> ```
> Task 3 replaces both bodies, and its tests are what turn them real.

- [ ] **Step 4: Run the tests and confirm they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ExcelSheetReaderTests"
```

Expected: all 20 pass (the two `ColumnIndex` theories account for 9).

- [ ] **Step 5: Commit**

```bash
git add DataIngestion/Schema/Readers/ExcelSheetReader.cs \
        DataIngestion/Schema/Readers/ExcelSerialDate.cs \
        DataIngestion/Schema/Readers/ExcelNumberFormats.cs \
        DataIngestion.Tests/Schema/Readers/ExcelSheetReaderTests.cs
git commit -m "feat(data): read an xlsx worksheet's cells into records"
```

---

### Task 3: Dates and padded numbers

The reason this reader is worth more than "save it as CSV": Excel stores a date as a number, and only its number format says so.

**Files:**
- Modify: `DataIngestion/Schema/Readers/ExcelNumberFormats.cs` (replace the stub entirely)
- Modify: `DataIngestion/Schema/Readers/ExcelSerialDate.cs` (replace the stub entirely)
- Test: `DataIngestion.Tests/Schema/Readers/ExcelSerialDateTests.cs`
- Test: `DataIngestion.Tests/Schema/Readers/ExcelNumberFormatTests.cs`

**Interfaces:**
- Consumes: `ExcelWorkbook.Entry`/`Load`/`Main`, `ExcelFixture`.
- Produces:
  - `ExcelSerialDate.ToText(double serial, bool date1904)` → `string?`
  - `ExcelNumberFormats.Load(ZipArchive zip)` → `ExcelNumberFormats`; instance `IsDate(int styleIndex)` → `bool`, `PadWidth(int styleIndex)` → `int?`

- [ ] **Step 1: Write the failing serial-date tests**

Create `DataIngestion.Tests/Schema/Readers/ExcelSerialDateTests.cs`:

```csharp
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class ExcelSerialDateTests
{
    [Theory]
    [InlineData(1, "1900-01-01")]
    [InlineData(59, "1900-02-28")]
    // Excel's 1900 calendar contains a 29 February 1900 that never existed, so every serial from
    // 60 up is one day ahead of reality. Ignore it and every date after February 1900 is wrong.
    [InlineData(61, "1900-03-01")]
    [InlineData(45658, "2025-01-01")]
    [InlineData(46096, "2026-03-15")]
    public void Converts1900Serials(double serial, string expected)
        => Assert.Equal(expected, ExcelSerialDate.ToText(serial, date1904: false));

    [Theory]
    [InlineData(0, "1904-01-01")]
    [InlineData(44196, "2025-01-01")]
    public void Converts1904Serials(double serial, string expected)
        => Assert.Equal(expected, ExcelSerialDate.ToText(serial, date1904: true));

    /// <summary>A time component is kept, in a form ColumnTypeInference also reads as a date.</summary>
    [Fact]
    public void KeepsATimeOfDay()
        => Assert.Equal("2026-03-15 13:30:00",
            ExcelSerialDate.ToText(46096.5625, date1904: false));

    /// <summary>Serial fractions are binary approximations, so 13:30 can arrive as 13:29:59.999.
    /// Rounding to the second is what stops a clean time reading as one second early.</summary>
    [Fact]
    public void RoundsToTheNearestSecond()
        => Assert.Equal("2026-03-15 13:30:00",
            ExcelSerialDate.ToText(46096.56249999, date1904: false));

    /// <summary>
    /// A serial below 1 has no date part at all — it is a time of day. Emitted as a time it stays
    /// a string, which the review loop can retype; emitted as a date it would claim a day the file
    /// never mentioned.
    /// </summary>
    [Fact]
    public void ASerialUnderOne_IsATimeOfDay()
        => Assert.Equal("09:30:00", ExcelSerialDate.ToText(0.39583333, date1904: false));

    /// <summary>Out of range is not a date, and the caller keeps the stored number instead.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3_000_000)]
    public void ASerialNoDateCanHold_IsNull(double serial)
        => Assert.Null(ExcelSerialDate.ToText(serial, date1904: false));

    /// <summary>Every emitted form must be one ColumnTypeInference types as DateTime, or the
    /// conversion has moved the problem rather than solved it.</summary>
    [Theory]
    [InlineData(46096)]
    [InlineData(46096.5625)]
    public void TheEmittedFormIsOneColumnTypeInferenceReadsAsADate(double serial)
    {
        var text = ExcelSerialDate.ToText(serial, date1904: false);

        Assert.Equal("DateTime",
            DataIngestion.Schema.ColumnTypeInference.Infer([text], "Placed"));
    }
}
```

- [ ] **Step 2: Write the failing number-format tests**

Create `DataIngestion.Tests/Schema/Readers/ExcelNumberFormatTests.cs`:

```csharp
using System.IO.Compression;
using System.Security;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class ExcelNumberFormatTests
{
    /// <summary>
    /// A styles part with one cellXfs entry per numFmtId given, so style index i uses
    /// <paramref name="numFmtIds"/>[i]. Custom codes are declared as ids from 164 up, which is
    /// where Excel's own custom formats start.
    /// </summary>
    private static string Styles(int[] numFmtIds, params (int Id, string Code)[] custom)
        => "<?xml version=\"1.0\"?>"
         + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
         + $"<numFmts count=\"{custom.Length}\">"
         + string.Concat(custom.Select(c =>
             // Escaped, because a format code may itself contain a quote -- 0.00" m" is a number
             // with a unit -- and Excel writes that as &quot; rather than breaking the attribute.
             $"<numFmt numFmtId=\"{c.Id}\" formatCode=\"{SecurityElement.Escape(c.Code)}\"/>"))
         + "</numFmts>"
         + $"<cellXfs count=\"{numFmtIds.Length}\">"
         + string.Concat(numFmtIds.Select(id => $"<xf numFmtId=\"{id}\" xfId=\"0\"/>"))
         + "</cellXfs></styleSheet>";

    private static ExcelNumberFormats Load(string? styles)
    {
        var path = ExcelFixture.Path(".xlsx");
        ExcelFixture.Write(path, null, styles, new ExcelFixture.Sheet("Orders", ""));

        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ExcelNumberFormats.Load(zip);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(14)]   // mm-dd-yy
    [InlineData(15)]   // d-mmm-yy
    [InlineData(17)]   // mmm-yy
    [InlineData(22)]   // m/d/yy h:mm
    [InlineData(46)]   // [h]:mm:ss
    public void BuiltinDateFormats_AreDates(int numFmtId)
        => Assert.True(Load(Styles([numFmtId])).IsDate(0));

    [Theory]
    [InlineData(0)]    // General
    [InlineData(1)]    // 0
    [InlineData(4)]    // #,##0.00
    [InlineData(9)]    // 0%
    [InlineData(11)]   // 0.00E+00
    [InlineData(44)]   // accounting
    [InlineData(49)]   // @  (text)
    public void BuiltinNumberFormats_AreNotDates(int numFmtId)
        => Assert.False(Load(Styles([numFmtId])).IsDate(0));

    [Theory]
    [InlineData("yyyy\\-mm\\-dd")]
    [InlineData("dd/mm/yyyy")]
    [InlineData("d mmmm yyyy")]
    [InlineData("[$-409]dddd, mmmm d, yyyy")]
    [InlineData("hh:mm:ss")]
    public void CustomDateCodes_AreDates(string code)
        => Assert.True(Load(Styles([164], (164, code))).IsDate(0));

    /// <summary>
    /// A false date is a corruption: 9.99 read as a date becomes 9 January 1900. Every code that
    /// is not recognisably a date must therefore stay a number.
    /// </summary>
    [Theory]
    [InlineData("General")]
    [InlineData("#,##0.00")]
    [InlineData("0.00E+00")]
    [InlineData("0.00_);(0.00)")]
    [InlineData("# ?/?")]
    [InlineData("0.00\" m\"")]                      // a unit, quoted -- not a month
    [InlineData("0.0\\m")]                          // an escaped literal -- not a month
    [InlineData("[Red]#,##0.00;[Blue]-#,##0.00")]   // colours, not tokens
    [InlineData("[$-409]#,##0.00")]                 // a locale, not tokens
    public void CodesThatAreNotDates_StayNumbers(string code)
        => Assert.False(Load(Styles([164], (164, code))).IsDate(0));

    [Fact]
    public void AStyleIndexPastTheTable_IsNotADate()
        => Assert.False(Load(Styles([14])).IsDate(7));

    [Fact]
    public void NoStylesPart_MeansNothingIsADate()
        => Assert.False(Load(null).IsDate(0));

    /// <summary>
    /// The one case where the display format IS the value: a postcode of numeric cells formatted
    /// 00000 shows 01234, and emitted as 1234 the user's own data has changed.
    /// </summary>
    [Theory]
    [InlineData("00000", 5)]
    [InlineData("000", 3)]
    public void AnAllZerosCode_PadsToItsWidth(string code, int width)
        => Assert.Equal(width, Load(Styles([164], (164, code))).PadWidth(0));

    /// <summary>Narrow on purpose. Anything but bare zeros is an ordinary number format, and
    /// padding one would be inventing digits.</summary>
    [Theory]
    [InlineData("0")]          // one digit pads nothing
    [InlineData("#,##0")]
    [InlineData("0.00")]
    [InlineData("00.0")]
    [InlineData("General")]
    [InlineData("yyyy")]
    public void AnythingElse_PadsNothing(string code)
        => Assert.Null(Load(Styles([164], (164, code))).PadWidth(0));

    /// <summary>The point of the padding rule: the column stays a string, so the padding survives
    /// into the generated app instead of being rounded off into an int.</summary>
    [Fact]
    public void APaddedValue_TypesAsString()
        => Assert.Equal("string", ColumnTypeInference.Infer(["01234", "00777"], "Postcode"));
}
```

- [ ] **Step 3: Run both new test classes and confirm they fail**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ExcelSerialDateTests|FullyQualifiedName~ExcelNumberFormatTests"
```

Expected: FAIL. `ExcelSerialDateTests` fail with null returned where a date string is expected; `ExcelNumberFormatTests` fail on every `Assert.True(...IsDate(...))` and every `PadWidth` expectation, because the stubs answer `false`/`null`. The negative cases pass already — that is why the positive ones matter.

- [ ] **Step 4: Write `ExcelSerialDate`**

Replace `DataIngestion/Schema/Readers/ExcelSerialDate.cs` entirely:

```csharp
using System.Globalization;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// Excel's date serials as text a date column can be inferred from.
/// </summary>
/// <remarks>
/// Excel stores 15 March 2026 as the number 46096. Imported raw, every date column in every
/// workbook types as <c>int</c> and the user's dates become five-digit numbers — the silent
/// corruption <see cref="Schema.ColumnTypeInference"/> exists to prevent. The formats emitted here
/// are all in that class's <c>DateFormats</c> list, so a converted column types as
/// <c>DateTime</c> and round-trips.
/// </remarks>
public static class ExcelSerialDate
{
    private const double SecondsPerDay = 86_400d;

    /// <summary>
    /// The serial as <c>yyyy-MM-dd</c>, or <c>yyyy-MM-dd HH:mm:ss</c> when it carries a time, or
    /// <c>HH:mm:ss</c> when it carries only a time. Null when no date can hold it, which tells the
    /// caller to keep the number exactly as the file stored it.
    /// </summary>
    public static string? ToText(double serial, bool date1904)
    {
        // Below 1 in the 1900 system there is no date part at all: the value is a time of day.
        // Emitted as a time it stays a string, which the review loop can retype with `field`;
        // emitted as a date it would claim a day the file never mentioned.
        if (!date1904 && serial > 0 && serial < 1)
            return TimeSpan.FromSeconds(Math.Round(serial * SecondsPerDay))
                .ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

        var date = ToDateTime(serial, date1904);

        if (date is null)
            return null;

        return date.Value.TimeOfDay == TimeSpan.Zero
            ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>The serial as a <see cref="DateTime"/>, or null when it names no real date.</summary>
    public static DateTime? ToDateTime(double serial, bool date1904)
    {
        if (double.IsNaN(serial) || double.IsInfinity(serial))
            return null;

        // The 1904 system counts from 1904-01-01 as serial 0. The 1900 system counts from serial 1
        // as 1900-01-01 AND contains a 29 February 1900 that never existed, so every serial from
        // 60 up is one day ahead of the real calendar and 60 itself is the phantom day.
        var (epoch, days) = date1904
            ? (new DateTime(1904, 1, 1), serial)
            : (new DateTime(1899, 12, 31), serial >= 60 ? serial - 1 : serial);

        if (days < 0)
            return null;

        var whole = Math.Floor(days);
        var seconds = (int)Math.Round((days - whole) * SecondsPerDay);

        // Rounding up through midnight rolls into the next day rather than reading 24:00:00.
        if (seconds >= SecondsPerDay)
        {
            whole++;
            seconds -= (int)SecondsPerDay;
        }

        try
        {
            return epoch.AddDays(whole).AddSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Past DateTime.MaxValue. Bounding by a magic serial would restate the same limit in a
            // second place, where it could disagree.
            return null;
        }
    }
}
```

- [ ] **Step 5: Write `ExcelNumberFormats`**

Replace `DataIngestion/Schema/Readers/ExcelNumberFormats.cs` entirely:

```csharp
using System.Globalization;
using System.IO.Compression;
using System.Text;

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
                               ?? Enumerable.Empty<System.Xml.Linq.XElement>())
        {
            var id = numFmt.Attribute("numFmtId")?.Value;
            var code = numFmt.Attribute("formatCode")?.Value;

            if (code is not null
                && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key))
                formats.codes[key] = code;
        }

        foreach (var xf in root?.Element(ExcelWorkbook.Main + "cellXfs")
                                ?.Elements(ExcelWorkbook.Main + "xf")
                           ?? Enumerable.Empty<System.Xml.Linq.XElement>())
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
```

- [ ] **Step 6: Run both test classes and confirm they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ExcelSerialDateTests|FullyQualifiedName~ExcelNumberFormatTests"
```

Expected: all pass.

- [ ] **Step 7: Add the through-the-cell tests to `ExcelSheetReaderTests`**

The rules now work; these prove they are reached from a real cell. Append to `ExcelSheetReaderTests`:

```csharp
    /// <summary>A date-styled number becomes a date, which is the whole reason to read xlsx rather
    /// than ask the user for a CSV: as a CSV export this column is already text.</summary>
    [Fact]
    public void ADateStyledNumber_ReadsAsADate()
    {
        var styles = "<?xml version=\"1.0\"?>"
            + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<cellXfs count=\"1\"><xf numFmtId=\"14\" xfId=\"0\"/></cellXfs></styleSheet>";

        var records = Records(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", "46096", style: 0)), styles: styles);

        Assert.Equal("2026-03-15", Assert.Single(records)[0]);
    }

    /// <summary>An UNstyled number in the same file stays exactly what it was.</summary>
    [Fact]
    public void AnUnstyledNumber_KeepsItsStoredText()
    {
        var styles = "<?xml version=\"1.0\"?>"
            + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<cellXfs count=\"2\"><xf numFmtId=\"0\" xfId=\"0\"/>"
            + "<xf numFmtId=\"14\" xfId=\"0\"/></cellXfs></styleSheet>";

        var records = Records(
            ExcelFixture.Row(1,
                ExcelFixture.Cell("A1", "46096", style: 0),
                ExcelFixture.Cell("B1", "46096", style: 1)),
            styles: styles);

        Assert.Equal(new[] { "46096", "2026-03-15" }, Assert.Single(records));
    }

    [Fact]
    public void A1904Workbook_CountsFromItsOwnEpoch()
    {
        var styles = "<?xml version=\"1.0\"?>"
            + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<cellXfs count=\"1\"><xf numFmtId=\"14\" xfId=\"0\"/></cellXfs></styleSheet>";

        var records = Records(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", "44196", style: 0)),
            styles: styles, date1904: true);

        Assert.Equal("2025-01-01", Assert.Single(records)[0]);
    }

    /// <summary>A postcode's padding is meaning, and losing it changes the user's own data.</summary>
    [Fact]
    public void AZeroPaddedNumber_KeepsItsPadding()
    {
        var styles = "<?xml version=\"1.0\"?>"
            + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"00000\"/></numFmts>"
            + "<cellXfs count=\"1\"><xf numFmtId=\"164\" xfId=\"0\"/></cellXfs></styleSheet>";

        var records = Records(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", "1234", style: 0)), styles: styles);

        Assert.Equal("01234", Assert.Single(records)[0]);
    }
```

- [ ] **Step 8: Run the sheet-reader tests and confirm they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ExcelSheetReaderTests"
```

Expected: all pass, including the four new ones.

- [ ] **Step 9: Commit**

```bash
git add DataIngestion/Schema/Readers/ExcelSerialDate.cs \
        DataIngestion/Schema/Readers/ExcelNumberFormats.cs \
        DataIngestion.Tests/Schema/Readers/ExcelSerialDateTests.cs \
        DataIngestion.Tests/Schema/Readers/ExcelNumberFormatTests.cs \
        DataIngestion.Tests/Schema/Readers/ExcelSheetReaderTests.cs
git commit -m "feat(data): read xlsx dates and padded codes as themselves, not as numbers"
```

---

### Task 4: `ExcelReader` and the wiring

The `IDataSourceReader` itself, its error paths, and the one `case` that makes `from excel` work.

**Files:**
- Create: `DataIngestion/Schema/Readers/ExcelReader.cs`
- Modify: `DataIngestion/Schema/Readers/DataSourceReaders.cs` (`TryCreate`, `NotSupportedMessage`)
- Modify: `DataIngestion/DataSourceKind.cs` (the doc comment)
- Test: `DataIngestion.Tests/Schema/Readers/ExcelReaderTests.cs`
- Test: `DataIngestion.Tests/Schema/Readers/DataSourceReadersTests.cs` (update)

**Interfaces:**
- Consumes: `ExcelWorkbook`, `ExcelSheetReader.ReadRecords`, `CsvReader.ToTable(string name, string label, IReadOnlyList<string?[]> records)`.
- Produces: `sealed class ExcelReader : IDataSourceReader` with `IReadOnlyList<TabularTable> Read(DataSourceSpec spec)`.

- [ ] **Step 1: Write the failing reader tests**

Create `DataIngestion.Tests/Schema/Readers/ExcelReaderTests.cs`:

```csharp
using System.IO.Compression;
using DataIngestion;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class ExcelReaderTests
{
    private static IReadOnlyList<TabularTable> Read(string path)
        => new ExcelReader().Read(new DataSourceSpec(DataSourceKind.Excel, path));

    private static void WithWorkbook(
        Action<string> use, string? sharedStrings = null, params ExcelFixture.Sheet[] sheets)
    {
        var path = ExcelFixture.Path(".xlsx");
        ExcelFixture.Write(path, sharedStrings, null, sheets);
        try { use(path); }
        finally { File.Delete(path); }
    }

    /// <summary>Header row, data rows, and the sheet's own name — the sheet is the table.</summary>
    [Fact]
    public void ASheet_BecomesATableNamedAfterIt()
    {
        WithWorkbook(path =>
        {
            var table = Assert.Single(Read(path));

            Assert.Equal("Orders", table.Name);
            Assert.Equal(new[] { "Ref", "Qty" }, table.Columns.Select(c => c.Name));
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal("ORD-2", table.Value(1, "Ref"));
        },
        ExcelFixture.SharedStrings("<t>Ref</t>", "<t>Qty</t>", "<t>ORD-1</t>", "<t>ORD-2</t>"),
        new ExcelFixture.Sheet("Orders", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.Cell("A1", "0", "s"), ExcelFixture.Cell("B1", "1", "s")),
            ExcelFixture.Row(2, ExcelFixture.Cell("A2", "2", "s"), ExcelFixture.Cell("B2", "2")),
            ExcelFixture.Row(3, ExcelFixture.Cell("A3", "3", "s"), ExcelFixture.Cell("B3", "3")))));
    }

    /// <summary>The label names the sheet as well as the file, so a normalization finding on a
    /// four-sheet workbook says which sheet it is about.</summary>
    [Fact]
    public void TheLabelNamesBothTheFileAndTheSheet()
    {
        WithWorkbook(path =>
        {
            var table = Assert.Single(Read(path));

            Assert.Contains(Path.GetFileName(path), table.Label);
            Assert.Contains("Orders", table.Label);
        },
        null,
        new ExcelFixture.Sheet("Orders", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Ref")),
            ExcelFixture.Row(2, ExcelFixture.InlineCell("A2", "ORD-1")))));
    }

    [Fact]
    public void EveryVisibleSheet_BecomesItsOwnTable()
    {
        WithWorkbook(path =>
        {
            var tables = Read(path);

            Assert.Equal(new[] { "Orders", "Customers" }, tables.Select(t => t.Name));
        },
        null,
        new ExcelFixture.Sheet("Orders", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Ref")),
            ExcelFixture.Row(2, ExcelFixture.InlineCell("A2", "ORD-1")))),
        new ExcelFixture.Sheet("Customers", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Name")),
            ExcelFixture.Row(2, ExcelFixture.InlineCell("A2", "Ada")))));
    }

    /// <summary>A hidden sheet holds scratch working or a lookup list. Generating a class per
    /// hidden helper sheet would be surprising in a way that omitting one is not.</summary>
    [Fact]
    public void AHiddenSheet_IsSkipped()
    {
        WithWorkbook(path =>
        {
            var table = Assert.Single(Read(path));
            Assert.Equal("Orders", table.Name);
        },
        null,
        new ExcelFixture.Sheet("Orders", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Ref")),
            ExcelFixture.Row(2, ExcelFixture.InlineCell("A2", "ORD-1")))),
        new ExcelFixture.Sheet("Lookups", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Code")),
            ExcelFixture.Row(2, ExcelFixture.InlineCell("A2", "X"))), Hidden: true));
    }

    /// <summary>A blank Sheet3 is normal and must not abort a workbook that is otherwise fine.</summary>
    [Fact]
    public void EmptyAndHeaderOnlySheets_AreSkippedRatherThanFatal()
    {
        WithWorkbook(path =>
        {
            var table = Assert.Single(Read(path));
            Assert.Equal("Orders", table.Name);
        },
        null,
        new ExcelFixture.Sheet("Orders", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Ref")),
            ExcelFixture.Row(2, ExcelFixture.InlineCell("A2", "ORD-1")))),
        new ExcelFixture.Sheet("Notes", ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Ref"))),
        new ExcelFixture.Sheet("Sheet3", string.Empty));
    }

    /// <summary>When nothing survives, the user needs to know which sheets were looked at and what
    /// was wrong with each — "no data" alone does not say what to fix.</summary>
    [Fact]
    public void AWorkbookWithNothingReadable_NamesEverySheetAndWhy()
    {
        WithWorkbook(path =>
        {
            var error = Assert.Throws<InvalidDataException>(() => Read(path));

            Assert.Contains("no sheet with data", error.Message);
            Assert.Contains("Notes", error.Message);
            Assert.Contains("header", error.Message);
            Assert.Contains("Sheet3", error.Message);
            Assert.Contains("empty", error.Message);
            Assert.Contains("Lookups", error.Message);
            Assert.Contains("hidden", error.Message);
        },
        null,
        new ExcelFixture.Sheet("Notes", ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Ref"))),
        new ExcelFixture.Sheet("Sheet3", string.Empty),
        new ExcelFixture.Sheet("Lookups", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Code")),
            ExcelFixture.Row(2, ExcelFixture.InlineCell("A2", "X"))), Hidden: true));
    }

    [Fact]
    public void ColumnsAreTypedFromTheirValues_TheSameWayEveryOtherFormatIs()
    {
        WithWorkbook(path =>
        {
            var table = Assert.Single(Read(path));

            Assert.Equal("string", table.Columns[0].Type);
            Assert.Equal("int", table.Columns[1].Type);
            Assert.Equal("decimal", table.Columns[2].Type);
        },
        null,
        new ExcelFixture.Sheet("Orders", string.Concat(
            ExcelFixture.Row(1,
                ExcelFixture.InlineCell("A1", "Ref"),
                ExcelFixture.InlineCell("B1", "Qty"),
                ExcelFixture.InlineCell("C1", "Total")),
            ExcelFixture.Row(2,
                ExcelFixture.InlineCell("A2", "ORD-1"),
                ExcelFixture.Cell("B2", "2"),
                ExcelFixture.Cell("C2", "9.99")),
            ExcelFixture.Row(3,
                ExcelFixture.InlineCell("A3", "ORD-2"),
                ExcelFixture.Cell("B3", "3"),
                ExcelFixture.Cell("C3", "4.50")))));
    }

    [Fact]
    public void AMissingFile_IsAFileNotFound()
    {
        var path = ExcelFixture.Path(".xlsx");

        var error = Assert.Throws<FileNotFoundException>(() => Read(path));
        Assert.Contains(path, error.Message);
    }

    /// <summary>
    /// A .xls is an OLE2 compound file, not a zip, so ZipFile.OpenRead reports "End of Central
    /// Directory record could not be found" — which tells the user nothing they can act on. The
    /// sniff runs first so they get the one instruction that fixes it.
    /// </summary>
    [Fact]
    public void ALegacyBinaryXls_SaysToResaveIt()
    {
        var path = ExcelFixture.Path(".xls");
        File.WriteAllBytes(path,
            [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00, 0x00, 0x00]);

        try
        {
            var error = Assert.Throws<InvalidDataException>(() => Read(path));

            Assert.Contains(".xls", error.Message);
            Assert.Contains("Save As", error.Message);
            Assert.Contains(".xlsx", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AFileThatIsNotAZipAtAll_SaysSo()
    {
        var path = ExcelFixture.Path(".xlsx");
        File.WriteAllText(path, "Id,Name\n1,Ada\n");

        try
        {
            var error = Assert.Throws<InvalidDataException>(() => Read(path));
            Assert.Contains("not a readable .xlsx", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AZipThatIsNotAWorkbook_SaysSo()
    {
        var path = ExcelFixture.Path(".xlsx");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("notes.txt").Open())
            entry.Write("nothing to see"u8);

        try
        {
            var error = Assert.Throws<InvalidDataException>(() => Read(path));
            Assert.Contains("not an Excel workbook", error.Message);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ExcelReaderTests"
```

Expected: build failure — `ExcelReader` does not exist (CS0246).

- [ ] **Step 3: Write `ExcelReader`**

Create `DataIngestion/Schema/Readers/ExcelReader.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests and confirm they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ExcelReaderTests"
```

Expected: all 12 pass.

- [ ] **Step 5: Update `DataSourceReadersTests` to the new truth**

In `DataIngestion.Tests/Schema/Readers/DataSourceReadersTests.cs`:

Add `[InlineData(DataSourceKind.Excel)]` to `SupportedKinds_HaveAReader`.

Replace `FormatsNotInThisBuild_HaveNoReader` and `NotSupportedMessage_NamesTheWorkingFormats` with:

```csharp
    /// <summary>SQL is the last kind with no reader.</summary>
    [Fact]
    public void SqlIsTheOnlyKindWithNoReader()
        => Assert.False(DataSourceReaders.TryCreate(DataSourceKind.Sql, out _));

    /// <summary>A refusal without an alternative leaves the user at a dead end, so the message
    /// must name the formats that do work — Excel now among them.</summary>
    [Fact]
    public void NotSupportedMessage_NamesTheWorkingFormats()
    {
        var message = DataSourceReaders.NotSupportedMessage(DataSourceKind.Sql);

        Assert.Contains("csv", message);
        Assert.Contains("txt", message);
        Assert.Contains("json", message);
        Assert.Contains("excel", message);
    }
```

- [ ] **Step 6: Run those tests and confirm they fail**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DataSourceReadersTests"
```

Expected: FAIL — `SupportedKinds_HaveAReader(Excel)` fails on `Assert.True(TryCreate(...))`, and `NotSupportedMessage_NamesTheWorkingFormats` fails on `Assert.Contains("excel", ...)`.

- [ ] **Step 7: Wire Excel into `DataSourceReaders`**

In `DataIngestion/Schema/Readers/DataSourceReaders.cs`, add to `TryCreate` above the `default`:

```csharp
            case DataSourceKind.Excel:
                reader = new ExcelReader();
                return true;
```

And replace `NotSupportedMessage` with:

```csharp
    /// <summary>What to tell a user who picked a kind this build cannot read.</summary>
    public static string NotSupportedMessage(DataSourceKind kind)
        => $"{kind} is not in this build yet — it lands in a follow-on branch. "
         + "Working formats: csv, txt, json, excel. Export the tables you want as CSV and "
         + "choose csv, or put them in a workbook and choose excel.";
```

Update the class doc comment's first line, which no longer describes two kinds:

```csharp
/// <summary>
/// Maps a <see cref="DataSourceKind"/> to its reader, and answers for the one kind that has no
/// reader yet. The interview offers every kind, so exactly one place has to know which are
/// implemented — this one.
/// </summary>
```

- [ ] **Step 8: Update the `DataSourceKind` doc comment**

In `DataIngestion/DataSourceKind.cs`:

```csharp
/// <summary>The data formats the interview offers. Sql is declared here — and named at the
/// prompt — before its reader exists, so the menu the user sees is the whole menu and choosing
/// it gets a straight answer rather than a missing option.</summary>
```

- [ ] **Step 9: Run the whole DataIngestion suite and confirm it passes**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj
```

Expected: every test passes, including `DataSourceInterviewTests` — the interview's "on the menu but no reader" branch still has SQL to exercise it. If a test there asserted Excel was refused, update it to use SQL and say why in a comment.

- [ ] **Step 10: Commit**

```bash
git add DataIngestion/Schema/Readers/ExcelReader.cs \
        DataIngestion/Schema/Readers/DataSourceReaders.cs \
        DataIngestion/DataSourceKind.cs \
        DataIngestion.Tests/Schema/Readers/ExcelReaderTests.cs \
        DataIngestion.Tests/Schema/Readers/DataSourceReadersTests.cs \
        DataIngestion.Tests/DataSourceInterviewTests.cs
git commit -m "feat(data): build an app from an Excel workbook"
```

---

### Task 5: The end-to-end proof, and the docs

A generated project that compiles is the only assertion that covers the stages between the reader and the app.

**Files:**
- Modify: `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs` (add one test)
- Modify: `CodeGenerator/App/BuildAppCommand.cs:101`
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs:476-479`
- Modify: `docs/commands.md:22`
- Modify: `README.md:210`
- Modify: `DataIngestion/README.md` (lines 33-34, 63-71, 188)
- Modify: `DataIngestion.Tests/README.md` (the test-file table)

**Interfaces:**
- Consumes: `ExcelReader`, and the existing `RunDotNet`/`TryDeleteDirectory` helpers already in `GeneratedProjectBuildTests`.
- Produces: nothing new.

- [ ] **Step 1: Write the failing end-to-end test**

Add to `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`. It needs `using System.IO.Compression;` and `using System.Text;` at the top of the file, and the local fixture writer below — `CodeGenerator.Tests` cannot see `ExcelFixture`, which is `internal` to `DataIngestion.Tests`, and a shared fixture project for one method would be more moving parts than the duplication costs.

```csharp
    /// <summary>
    /// The Excel path end to end: a workbook is read, modelled, scaffolded, and the result is
    /// COMPILED. What makes this worth its runtime over the CSV case is the two things only a
    /// workbook can get wrong — a date that is really a serial number, and a second sheet that has
    /// to become a second class — and neither is visible to a substring assertion.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectGeneratedFromAnExcelWorkbook_BuildsAndCarriesTheData()
    {
        // Short root: the scratchpad path plus a generated project's nesting exceeds MAX_PATH, and
        // MSBuild reports that as MSB3030 "file not found" on a file that is plainly there.
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var xlsx = Path.Combine(workspace, "shop.xlsx");
        WriteWorkbook(xlsx);

        try
        {
            var tables = new ExcelReader().Read(new DataSourceSpec(DataSourceKind.Excel, xlsx));

            // Two visible sheets, two entities. The hidden third sheet is not one.
            Assert.HasCount(2, tables);
            CollectionAssert.AreEquivalent(
                new[] { "Orders", "Customers" }, tables.Select(t => t.Name).ToArray());

            // The date column really is a date, not a five-digit serial.
            var orders = tables.Single(t => t.Name == "Orders");
            Assert.AreEqual("DateTime", orders.Columns.Single(c => c.Name == "Placed").Type);

            // ...and the padded reference kept its zeros, so it stayed a string.
            Assert.AreEqual("string", orders.Columns.Single(c => c.Name == "Code").Type);
            Assert.AreEqual("00042", orders.Value(0, "Code"));

            var model = DataModelBuilder.Build(tables);
            var imported = TabularImport.ToImportedData(tables);

            Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Shop", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            // The generated project compiles. This is the assertion the rest exist to enable.
            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");

            var seedFolder = Path.Combine(
                result.ProjectPath, "Data", SeedFileWriter.SeedFolderName);

            var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(seedFolder, "Order.json")))
                .RootElement.EnumerateArray().ToList();

            Assert.HasCount(2, rows);
            Assert.AreEqual(new DateTime(2026, 3, 15), rows[0].GetProperty("Placed").GetDateTime());
            Assert.AreEqual("00042", rows[0].GetProperty("Code").GetString());
            Assert.AreEqual(9.99m, rows[0].GetProperty("Total").GetDecimal());

            // The second sheet became its own seeded class.
            Assert.IsTrue(File.Exists(Path.Combine(seedFolder, "Customer.json")));
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A minimal .xlsx written by hand. A committed binary workbook would be unreviewable bytes in
    /// the repo, and this way the date serial and the padded format the test asserts on are visible
    /// right next to the assertions.
    /// </summary>
    private static void WriteWorkbook(string path)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        // Style 0: the 00000 padded code. Style 1: a date. 46096 is 15 March 2026.
        var styles = $"<styleSheet xmlns=\"{ns}\">"
            + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"00000\"/></numFmts>"
            + "<cellXfs count=\"2\"><xf numFmtId=\"164\" xfId=\"0\"/>"
            + "<xf numFmtId=\"14\" xfId=\"0\"/></cellXfs></styleSheet>";

        var orders = $"<worksheet xmlns=\"{ns}\"><sheetData>"
            + "<row r=\"1\">"
            + Inline("A1", "Code") + Inline("B1", "Placed")
            + Inline("C1", "Total") + Inline("D1", "CustomerId")
            + "</row>"
            + "<row r=\"2\"><c r=\"A2\" s=\"0\"><v>42</v></c><c r=\"B2\" s=\"1\"><v>46096</v></c>"
            + "<c r=\"C2\"><v>9.99</v></c><c r=\"D2\"><v>1</v></c></row>"
            + "<row r=\"3\"><c r=\"A3\" s=\"0\"><v>7</v></c><c r=\"B3\" s=\"1\"><v>46097</v></c>"
            + "<c r=\"C3\"><v>4.50</v></c><c r=\"D3\"><v>2</v></c></row>"
            + "</sheetData></worksheet>";

        var customers = $"<worksheet xmlns=\"{ns}\"><sheetData>"
            + "<row r=\"1\">" + Inline("A1", "Name") + Inline("B1", "City") + "</row>"
            + "<row r=\"2\">" + Inline("A2", "Ada") + Inline("B2", "London") + "</row>"
            + "<row r=\"3\">" + Inline("A3", "Grace") + Inline("B3", "York") + "</row>"
            + "</sheetData></worksheet>";

        var scratch = $"<worksheet xmlns=\"{ns}\"><sheetData>"
            + "<row r=\"1\">" + Inline("A1", "Note") + "</row>"
            + "<row r=\"2\">" + Inline("A2", "working") + "</row>"
            + "</sheetData></worksheet>";

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        Add(zip, "xl/worksheets/sheet1.xml", orders);
        Add(zip, "xl/worksheets/sheet2.xml", customers);
        Add(zip, "xl/worksheets/sheet3.xml", scratch);
        Add(zip, "xl/styles.xml", styles);

        Add(zip, "xl/workbook.xml",
            $"<workbook xmlns=\"{ns}\" xmlns:r=\"{rns}\"><sheets>"
            + "<sheet name=\"Orders\" sheetId=\"1\" r:id=\"rId1\"/>"
            + "<sheet name=\"Customers\" sheetId=\"2\" r:id=\"rId2\"/>"
            + "<sheet name=\"Scratch\" sheetId=\"3\" state=\"hidden\" r:id=\"rId3\"/>"
            + "</sheets></workbook>");

        Add(zip, "xl/_rels/workbook.xml.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/"
            + "relationships\">"
            + $"<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\" Type=\"{rns}/worksheet\"/>"
            + $"<Relationship Id=\"rId2\" Target=\"worksheets/sheet2.xml\" Type=\"{rns}/worksheet\"/>"
            + $"<Relationship Id=\"rId3\" Target=\"worksheets/sheet3.xml\" Type=\"{rns}/worksheet\"/>"
            + $"<Relationship Id=\"rId4\" Target=\"styles.xml\" Type=\"{rns}/styles\"/>"
            + "</Relationships>");

        static string Inline(string reference, string text)
            => $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{text}</t></is></c>";

        static void Add(ZipArchive zip, string name, string content)
        {
            using var entry = zip.CreateEntry(name).Open();
            using var writer = new StreamWriter(entry, new UTF8Encoding(false));
            writer.Write("<?xml version=\"1.0\"?>" + content);
        }
    }
```

- [ ] **Step 2: Run it and confirm it fails**

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ProjectGeneratedFromAnExcelWorkbook"
```

Expected: PASS, because Tasks 1-4 already built the reader. **This is the one test in the plan that cannot go red first** — it is a composition test over finished parts, and there is no implementation left for it to drive. If it FAILS, that is a real defect in Tasks 1-4 found by composition, which is what the test is for: fix the reader, not the test.

To be sure it is not vacuous, temporarily change the expected date to `new DateTime(2026, 3, 16)`, re-run, see it fail on that assertion, then change it back. A test whose failure you have never seen is not evidence.

- [ ] **Step 3: Commit the test**

```bash
git add CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs
git commit -m "test(data): compile a project generated from an Excel workbook"
```

- [ ] **Step 4: Update the CLI usage line**

`CodeGenerator/App/BuildAppCommand.cs:101`:

```csharp
            Console.WriteLine("   or: build app from csv <path>   (also txt, json, excel)");
```

- [ ] **Step 5: Update the command catalogue**

`CodeGenerationEngine/Core/CommandDictionary.cs`, the `build app:` entry:

```csharp
            Description = "Builds the model from existing data without prompting. Formats: csv, "
                        + "txt, json, excel (sql is not in this build yet). Add 'normalize' to "
```

(keep the rest of the entry, including the `Example`, unchanged)

- [ ] **Step 6: Update `docs/commands.md:22`**

Change `Formats: csv, txt, json (excel and sql are not in this build yet).` to
`Formats: csv, txt, json, excel (sql is not in this build yet).`

- [ ] **Step 7: Update `README.md:210`**

Change the sentence that says `excel` and `sql` are both answered with an alternative so it names only `sql`.

- [ ] **Step 8: Update `DataIngestion/README.md`**

- Lines 33-34: `That single shape is why Excel and SQL Server are cheap to add later` → say Excel proved it and SQL Server is what remains.
- Line 66, the Excel row of the format table:
  `| Excel | `excel` | One visible sheet per entity. Shared strings, inline strings, formula results, booleans and sparse rows; a date serial becomes a date and a `00000`-formatted code keeps its zeros. Hidden sheets skipped; legacy binary `.xls` refused with instructions. |`
- Lines 69-71: only SQL is now on the menu without a reader.
- Line 188: add `ExcelReader` to the `Schema/Readers/` row.

- [ ] **Step 9: Update `DataIngestion.Tests/README.md`**

Add rows to the test-file table:

```
| `Schema/Readers/ExcelReaderTests.cs` | A sheet becomes a table, every visible sheet its own; hidden, empty and header-only sheets skipped and named when nothing survives; missing file, legacy `.xls`, non-zip and non-workbook messages. |
| `Schema/Readers/ExcelSheetReaderTests.cs` | Cell addressing including sparse rows, shared and inline strings, formula results, booleans, error cells, empty rows, and dates and padded codes reaching the cell. |
| `Schema/Readers/ExcelWorkbookTests.cs` | Sheet list and part paths, hidden state, the shared string table including split runs and phonetic runs, and the `date1904` flag. |
| `Schema/Readers/ExcelSerialDateTests.cs` | 1900 and 1904 epochs, the phantom 1900-02-29, times of day, rounding, and out-of-range serials. |
| `Schema/Readers/ExcelNumberFormatTests.cs` | Which builtin ids and custom codes are dates, the codes that must NOT be (quoted units, escapes, colours, locales), and the narrow all-zeros padding rule. |
```

- [ ] **Step 10: Verify everything with the CI command**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj -c Release
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj -c Release
dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj -c Release
dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj -c Release
```

All from `D:\SmartCodeGenerator-Repository`. Expected: a clean build and four green runs. `-warnaserror` is where an analyzer error the local Debug build hides will surface.

- [ ] **Step 11: Drive the real CLI**

`dotnet test` leaves `CodeGenerator`'s `bin` stale, so rebuild first or a working fix looks dead:

```
dotnet build CodeGenerator/CodeGenerator.csproj -c Debug
```

Then write a small workbook to a short path (`C:\t\shop.xlsx`) and pipe the command in, since redirected stdin takes the inline form:

```
"build app: order tracking from excel C:\t\shop.xlsx" | dotnet run --project CodeGenerator --no-build
```

Expected: the normalization check names `shop.xlsx [Orders]`, and the model shows an `Order` and a `Customer`. This is the check that proves the wiring, not just the reader.

- [ ] **Step 12: Commit the docs and open the PR**

```bash
git add CodeGenerator/App/BuildAppCommand.cs \
        CodeGenerationEngine/Core/CommandDictionary.cs \
        docs/commands.md README.md \
        DataIngestion/README.md DataIngestion.Tests/README.md
git commit -m "docs: excel is a working data-source format"
git push -u origin feat/excel-data-source
gh pr create --title "feat(data): build an app from an Excel workbook" --body "..."
```

---

## Self-review

**Spec coverage:**

| Spec section | Task |
|---|---|
| `ExcelReader.cs` / `ExcelWorkbook.cs` / `ExcelSheetReader.cs` components | 1, 2, 4 (plus `ExcelNumberFormats` and `ExcelSerialDate`, split out of the sheet reader in Task 3 — the spec's table folded them into `ExcelWorkbook`/`ExcelSheetReader`, and separate files keep each testable on its own) |
| Cell rules table — every row | 2 (`ExcelSheetReaderTests`), dates and padding in 3 |
| Dates must be converted | 3 |
| Numbers emitted as stored | 2 (`AnUnstyledNumber_KeepsItsStoredText`), 3 (`CodesThatAreNotDates_StayNumbers`) |
| Sheet rules — one per sheet, hidden skipped, empty rows dropped, header-only skipped, error naming sheets | 4, except empty-row dropping which is 2 (`RowsOfNothing_AreNotRecords`) |
| Sparse cells addressed not counted | 2 (`SparseRow_PlacesEachCellAtItsOwnColumn`) |
| Error handling — all five rows | 4, plus the no-workbook-part case in 1 |
| No binary fixtures | 1 (`ExcelFixture`), 5 (`WriteWorkbook`) |
| `DataSourceReadersTests` updates | 4 |
| End-to-end compile | 5 |
| Wiring and docs — all seven locations | 4 (code) and 5 (docs) |
| Verification with the CI command | 5, steps 10-11 |

No gaps.

**Placeholders:** none. Every code step carries the code. The one `"..."` is a `gh pr create` body, written at the time from the commits.

**Type consistency:** `ExcelFixture.Sheet(Name, Xml, Hidden)` is used with the same shape in Tasks 1, 2, 3 and 4. `ExcelWorkbook.Entry`/`Load`/`Main` are declared public in Task 1 and consumed by Tasks 2, 3 and 4. `ExcelNumberFormats.IsDate(int)`/`PadWidth(int)` and `ExcelSerialDate.ToText(double, bool)` are stubbed with those exact signatures in Tasks 1-2 and implemented unchanged in Task 3. `CsvReader.ToTable(string, string, IReadOnlyList<string?[]>)` matches the existing declaration at `CsvReader.cs:137`.

**Known ordering hazard:** Tasks 1 and 2 introduce stubs that answer "not a date, no padding". Task 3's tests are what make them real, and Task 3 Step 3 explicitly requires seeing the positive cases fail first — otherwise a stub could survive into the merge with everything green.

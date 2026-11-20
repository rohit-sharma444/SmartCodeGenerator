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

    /// <summary>
    /// G4: a title row above the header. Read as the header, it made one column out of the title
    /// and dropped every other column with no word said.
    /// </summary>
    [Fact]
    public void ATitleRowAboveTheHeader_IsSkippedWithANote()
    {
        WithWorkbook(path =>
        {
            var table = Assert.Single(Read(path));

            Assert.Equal(new[] { "Region", "Units", "Revenue" }, table.Columns.Select(c => c.Name));
            Assert.Equal(3, table.Rows.Count);
            Assert.Equal("North", table.Value(0, "Region"));
            Assert.Equal("120.5", table.Value(2, "Revenue"));
            Assert.Contains(table.Notes, n => n.Contains("Sales Report 2024") && n.Contains("Region"));
        },
        null,
        new ExcelFixture.Sheet("Sales", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Sales Report 2024")),
            ExcelFixture.Row(3, ExcelFixture.InlineCell("A3", "Region"), ExcelFixture.InlineCell("B3", "Units"),
                ExcelFixture.InlineCell("C3", "Revenue")),
            ExcelFixture.Row(4, ExcelFixture.InlineCell("A4", "North"), ExcelFixture.Cell("B4", "10"), ExcelFixture.Cell("C4", "100")),
            ExcelFixture.Row(5, ExcelFixture.InlineCell("A5", "South"), ExcelFixture.Cell("B5", "20"), ExcelFixture.Cell("C5", "200")),
            ExcelFixture.Row(6, ExcelFixture.InlineCell("A6", "East"), ExcelFixture.Cell("B6", "30"), ExcelFixture.Cell("C6", "120.5")))));
    }

    /// <summary>G4: a cell to the right of the header's last column was cut off silently. It
    /// is kept, under a numbered column, and named in a note.</summary>
    [Fact]
    public void ACellRightOfTheHeader_IsKeptAndNamedInANote()
    {
        WithWorkbook(path =>
        {
            var table = Assert.Single(Read(path));

            Assert.Equal(new[] { "Ref", "Qty", "Column3" }, table.Columns.Select(c => c.Name));
            Assert.Equal("late", table.Value(1, "Column3"));
            Assert.Contains(table.Notes, n => n.Contains("Column3") && n.Contains("row 2"));
        },
        null,
        new ExcelFixture.Sheet("Orders", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Ref"), ExcelFixture.InlineCell("B1", "Qty")),
            ExcelFixture.Row(2, ExcelFixture.InlineCell("A2", "ORD-1"), ExcelFixture.Cell("B2", "1")),
            ExcelFixture.Row(3, ExcelFixture.InlineCell("A3", "ORD-2"), ExcelFixture.Cell("B3", "2"),
                ExcelFixture.InlineCell("C3", "late")))));
    }

    /// <summary>A one-column sheet is not a title row followed by data.</summary>
    [Fact]
    public void AOneColumnSheet_KeepsItsHeader()
    {
        WithWorkbook(path =>
        {
            var table = Assert.Single(Read(path));

            Assert.Equal(new[] { "Name" }, table.Columns.Select(c => c.Name));
            Assert.Equal(2, table.Rows.Count);
            Assert.Empty(table.Notes);
        },
        null,
        new ExcelFixture.Sheet("People", string.Concat(
            ExcelFixture.Row(1, ExcelFixture.InlineCell("A1", "Name")),
            ExcelFixture.Row(2, ExcelFixture.InlineCell("A2", "Ann")),
            ExcelFixture.Row(3, ExcelFixture.InlineCell("A3", "Bob")))));
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

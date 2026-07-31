using System.IO.Compression;
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

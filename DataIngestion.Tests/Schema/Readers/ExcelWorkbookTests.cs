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

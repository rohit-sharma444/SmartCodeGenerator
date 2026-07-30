using DataIngestion;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class CsvReaderTests
{
    [Fact]
    public void ReadsHeaderAndRows()
    {
        var records = CsvReader.ParseFields("Id,Name\n1,Ada\n2,Grace\n", ',');

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "Id", "Name" }, records[0]);
        Assert.Equal(new[] { "2", "Grace" }, records[2]);
    }

    [Fact]
    public void QuotedField_KeepsItsEmbeddedDelimiter()
    {
        var records = CsvReader.ParseFields("Name,City\n\"Wills, Ada\",London\n", ',');

        Assert.Equal(new[] { "Wills, Ada", "London" }, records[1]);
    }

    [Fact]
    public void QuotedField_KeepsItsEmbeddedNewline()
    {
        var records = CsvReader.ParseFields("Name,Note\nAda,\"line one\nline two\"\n", ',');

        Assert.Equal(2, records.Count);
        Assert.Equal("line one\nline two", records[1][1]);
    }

    [Fact]
    public void DoubledQuote_IsOneLiteralQuote()
    {
        var records = CsvReader.ParseFields("Name\n\"say \"\"hi\"\"\"\n", ',');

        Assert.Equal("say \"hi\"", records[1][0]);
    }

    [Fact]
    public void CrLfAndLf_AreBothRowEndings()
    {
        var records = CsvReader.ParseFields("A,B\r\n1,2\n3,4\r\n", ',');

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "3", "4" }, records[2]);
    }

    [Fact]
    public void ByteOrderMark_IsNotPartOfTheFirstColumnName()
    {
        var records = CsvReader.ParseFields("\uFEFFId,Name\n1,Ada\n", ',');

        Assert.Equal("Id", records[0][0]);
    }

    /// <summary>A short row is padded and a long row truncated, so every row matches the header.
    /// A ragged export is ordinary, and throwing would make the whole file unreadable.</summary>
    [Fact]
    public void RaggedRows_ArePaddedAndTruncatedToTheHeaderWidth()
    {
        var table = CsvReader.ToTable(
            "Orders", "orders.csv", CsvReader.ParseFields("A,B,C\n1\n1,2,3,4\n", ','));

        Assert.All(table.Rows, row => Assert.Equal(3, row.Length));
        Assert.Null(table.Value(0, "C"));
        Assert.Equal("3", table.Value(1, "C"));
    }

    [Fact]
    public void EmptyUnquotedField_IsNull_NotEmptyString()
    {
        var table = CsvReader.ToTable(
            "Orders", "orders.csv", CsvReader.ParseFields("A,B\n,2\n", ','));

        Assert.Null(table.Value(0, "A"));
    }

    [Fact]
    public void TypesEachColumnFromItsValues()
    {
        var table = CsvReader.ToTable(
            "Orders", "orders.csv",
            CsvReader.ParseFields("Ref,Qty,Price\nORD-1,2,9.99\nORD-2,3,4.50\n", ','));

        Assert.Equal("string", table.Columns[0].Type);
        Assert.Equal("int", table.Columns[1].Type);
        Assert.Equal("decimal", table.Columns[2].Type);
    }

    [Fact]
    public void UnnamedColumn_GetsAPositionalName_SoItIsAddressable()
    {
        var table = CsvReader.ToTable(
            "Orders", "orders.csv", CsvReader.ParseFields("Id,,Name\n1,x,Ada\n", ','));

        Assert.Equal("Column2", table.Columns[1].Name);
    }

    [Fact]
    public void ReadsAFileFromDisk_NamingTheTableAfterIt()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Name\n1,Ada\n");

        try
        {
            var table = Assert.Single(
                new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, path)));

            Assert.Equal(Path.GetFileNameWithoutExtension(path), table.Name);
            Assert.Equal(Path.GetFileName(path), table.Label);
            Assert.Equal("Ada", table.Value(0, "Name"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HeaderOnlyFile_IsRejectedWithAMessageThatSaysWhy()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Name\n");

        try
        {
            var error = Assert.Throws<InvalidDataException>(
                () => new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, path)));

            Assert.Contains("no data rows", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

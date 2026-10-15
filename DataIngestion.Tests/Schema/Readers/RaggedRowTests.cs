using DataIngestion;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

/// <summary>
/// A row with more fields than the header (an unquoted comma inside a value) used to be cut to
/// the header's width without a word: "P2,Smith, John,2" became Description "Smith", Qty "John",
/// and the 2 was lost — which also made the whole Qty column text.
/// </summary>
public class RaggedRowTests
{
    private const string Ragged = "Sku,Description,Qty\nP1,Plain,1\nP2,Smith, John,2\nP3,Short\nP4,Ok,4\n";

    private static DataIngestion.Schema.TabularTable Read(string text)
        => CsvReader.FromText("ragged", "ragged.csv", text, ',');

    [Fact]
    public void ARowWithMoreFields_IsLeftOut_SoNoValueLandsInTheWrongColumn()
    {
        var table = Read(Ragged);

        Assert.Equal(3, table.Rows.Count);
        Assert.DoesNotContain(table.ColumnValues("Sku"), v => v == "P2");
        Assert.DoesNotContain(table.ColumnValues("Qty"), v => v == "John");
        Assert.Equal("int", table.Columns[2].Type);
    }

    [Fact]
    public void ARowWithMoreFields_IsNamedInANote()
    {
        var table = Read(Ragged);

        var note = Assert.Single(table.Notes, n => n.Contains("more fields"));
        Assert.Contains("ragged.csv", note);
        Assert.Contains("row 2 (P2)", note);
        Assert.Contains("left out", note);
    }

    [Fact]
    public void ARowWithFewerFields_IsKept_AndNamedInANote()
    {
        var table = Read(Ragged);

        Assert.Contains(table.ColumnValues("Sku"), v => v == "P3");
        Assert.Null(table.Value(1, "Qty"));   // P3 is the second row kept

        var note = Assert.Single(table.Notes, n => n.Contains("fewer fields"));
        Assert.Contains("row 3 (P3)", note);
    }

    [Fact]
    public void TrailingEmptyFields_AreNotARaggedRow()
    {
        var table = Read("A,B\n1,2,\n3,4,,\n");

        Assert.Equal(2, table.Rows.Count);
        Assert.Empty(table.Notes);
    }

    [Fact]
    public void TheCsvReader_LeavesOutTheSameRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sff-ragged-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, Ragged);

        try
        {
            var table = new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, path))[0];

            Assert.DoesNotContain(table.ColumnValues("Sku"), v => v == "P2");
            Assert.Contains(table.Notes, n => n.Contains("row 2 (P2)"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheTxtReader_LeavesOutTheSameRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sff-ragged-{Guid.NewGuid():N}.txt");
        // Past the lines the delimiter sniff reads, which demands a steady field count.
        var lines = Enumerable.Range(1, DelimitedTextReader.SniffLines).Select(i => $"P{i};Ok;{i}");
        File.WriteAllText(path, "Sku;Description;Qty\n" + string.Join("\n", lines) + "\nPX;Smith; John;2\n");

        try
        {
            var table = new DelimitedTextReader().Read(new DataSourceSpec(DataSourceKind.Txt, path))[0];

            Assert.DoesNotContain(table.ColumnValues("Sku"), v => v == "PX");
            Assert.Contains(table.Notes, n => n.Contains($"row {DelimitedTextReader.SniffLines + 1} (PX)"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

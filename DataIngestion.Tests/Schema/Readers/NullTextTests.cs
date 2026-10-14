using DataIngestion;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

/// <summary>
/// The text NULL in a file source is how SQL Server Management Studio, Excel pastes and many
/// exports write a missing value. Kept as data, it made a column of numbers text and was seeded
/// as the word "NULL".
/// </summary>
public class NullTextTests
{
    private static DataIngestion.Schema.TabularTable Read(string csv)
        => CsvReader.ToTable("nulls", "nulls.csv", CsvReader.ParseFields(csv, ','));

    [Fact]
    public void NullText_IsAMissingValue_SoANumberColumnStaysANumber()
    {
        var table = Read("Qty,Flag\n1,Y\n,N\nNULL,\n3,null\n");

        Assert.Equal("int", table.Columns[0].Type);
        Assert.Equal("bool", table.Columns[1].Type);
        Assert.Null(table.Value(2, "Qty"));
        Assert.Null(table.Value(3, "Flag"));
        Assert.Equal("3", table.Value(3, "Qty"));
    }

    [Fact]
    public void NullText_IsNamedInANote()
    {
        var table = Read("Qty,Flag,Name\n1,Y,Ada\nNULL,NULL,Bo\n NULL ,N,Cy\n");

        var note = Assert.Single(table.Notes);
        Assert.Contains("NULL", note);
        Assert.Contains("Qty (2)", note);
        Assert.Contains("Flag (1)", note);
        Assert.DoesNotContain("Name", note);
    }

    [Fact]
    public void ATableWithoutNullText_HasNoNote()
    {
        var table = Read("Qty,Name\n1,Nullable\n2,Annul\n");

        Assert.Empty(table.Notes);
        Assert.Equal("Nullable", table.Value(0, "Name"));
    }

    [Fact]
    public void NullText_IsNotOfferedAsAnAllowedValue()
    {
        var table = Read("Active\nyes\nY\nNULL\nno\nyes\nN\nNULL\nno\n");

        var model = DataModelBuilder.Build([table]);
        var field = Assert.Single(model.Entities[0].Fields, f => f.Name == "Active");

        Assert.DoesNotContain(
            field.Proposals,
            p => p.Rule.AllowedValues.Any(v => string.Equals(v, "NULL", StringComparison.OrdinalIgnoreCase)));
    }
}

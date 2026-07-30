using AppScaffolding;
using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests;

public class TabularImportTests
{
    private static TabularTable Table(string name, string[] header, params string?[][] rows)
    {
        var table = new TabularTable { Name = name, SourceLabel = name + ".csv" };
        foreach (var column in header)
            table.Columns.Add(new TabularColumn { Name = column });

        foreach (var row in rows)
            table.Rows.Add(row);

        return table;
    }

    [Fact]
    public void RowsAreKeyedByEntityNameAndFieldName()
    {
        var data = TabularImport.ToImportedData(
            [Table("Orders", ["order ref", "Qty"], ["ORD-1", "2"], ["ORD-2", "3"])]);

        var rows = data.For("Order");

        Assert.Equal(2, rows.Count);

        // Keys are the FIELD names the model will carry, not the raw column headers -- otherwise
        // the seed file's keys would not match the generated class's properties.
        Assert.Equal("ORD-1", rows[0]["OrderRef"]);
        Assert.Equal("3", rows[1]["Qty"]);
    }

    [Fact]
    public void AnEntityWithNoImportedRowsIsAbsent()
    {
        var data = TabularImport.ToImportedData([Table("Orders", ["Ref"], ["ORD-1"])]);

        Assert.Empty(data.For("Customer"));
        Assert.False(data.IsEmpty);
    }

    [Fact]
    public void NoTablesIsEmpty()
        => Assert.True(TabularImport.ToImportedData([]).IsEmpty);

    [Fact]
    public void NullCellsSurviveAsNulls()
    {
        var data = TabularImport.ToImportedData([Table("Orders", ["Ref", "Note"], ["ORD-1", null])]);

        Assert.Null(data.For("Order")[0]["Note"]);
    }

    /// <summary>
    /// A 48,000-row export must not become a 48,000-row JSON blob inside the generated project.
    /// The cap is real, and it is SAID -- silent truncation reads as "all your data is in there".
    /// </summary>
    [Fact]
    public void RowsAreCappedAndTheCapIsStated()
    {
        var rows = Enumerable.Range(1, ImportedData.MaxSeedRows + 500)
            .Select(i => new string?[] { $"ORD-{i}" })
            .ToArray();

        var data = TabularImport.ToImportedData([Table("Orders", ["Ref"], rows)]);

        Assert.Equal(ImportedData.MaxSeedRows, data.For("Order").Count);
        Assert.Contains(data.Notes, n =>
            n.Contains(ImportedData.MaxSeedRows.ToString())
            && n.Contains((ImportedData.MaxSeedRows + 500).ToString()));
    }

    [Fact]
    public void ARowCountUnderTheCapProducesNoNote()
        => Assert.Empty(TabularImport.ToImportedData(
            [Table("Orders", ["Ref"], ["ORD-1"])]).Notes);

    /// <summary>Every table becomes its own entry, so a JSON document's child tables are imported
    /// too.</summary>
    [Fact]
    public void EveryTableIsImported()
    {
        var data = TabularImport.ToImportedData(
        [
            Table("Orders", ["Ref"], ["ORD-1"]),
            Table("OrderItem", ["OrderId", "Item"], ["1", "Nut"]),
        ]);

        Assert.Single(data.For("Order"));
        Assert.Single(data.For("OrderItem"));
    }

    /// <summary>Entity names match DataModelBuilder's -- both singularize the table name, and a
    /// mismatch would silently leave the seed file unmatched to any class.</summary>
    [Fact]
    public void EntityNamesAreSingularized_MatchingTheModelBuilder()
    {
        var tables = new[] { Table("Categories", ["Name"], ["Fiction"]) };

        var entity = DataModelBuilder.Build(tables).Entities.Single().Name;

        Assert.Equal(entity, Assert.Single(TabularImport.ToImportedData(tables).Rows).Key);
    }
}

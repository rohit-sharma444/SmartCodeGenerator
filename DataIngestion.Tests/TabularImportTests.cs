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

    /// <summary>
    /// A column renamed off its own class's name is keyed by the RENAMED field, because that is what
    /// the generated class declares. Keying it by the raw column name would leave SeedFileWriter --
    /// which walks the class's properties -- unable to find the value, so the app would start with
    /// that column silently blank on every row.
    /// </summary>
    [Fact]
    public void AColumnRenamedOffItsClassName_IsKeyedByTheRenamedField()
    {
        var data = TabularImport.ToImportedData(
            [Table("Products", ["Product", "Sku"], ["Hammer", "SKU-1"])]);

        var row = data.For("Product")[0];

        Assert.Equal("Hammer", row["ProductName"]);
        Assert.False(row.ContainsKey("Product"));
    }

    /// <summary>A dropped key column contributes no seed value either -- the generated class has no
    /// such property, so a row carrying it would name something that does not exist.</summary>
    [Fact]
    public void ADroppedKeyColumnIsNotImported()
    {
        var data = TabularImport.ToImportedData(
            [Table("Orders", ["Id", "Ref"], ["7", "ORD-1"])]);

        var row = data.For("Order")[0];

        Assert.False(row.ContainsKey("Id"));
        Assert.Equal("ORD-1", row["Ref"]);
    }

    /// <summary>Every key an imported row carries is a field the model actually declares. This is the
    /// invariant SeedFileWriter depends on, stated once rather than inferred per test.</summary>
    [Fact]
    public void EveryImportedKeyIsAFieldTheModelDeclares()
    {
        var tables = new[] { Table("Products", ["Id", "Product", "Sku", "2 bad"], ["1", "Hammer", "S", "x"]) };

        var declared = DataModelBuilder.Build(tables).Entities
            .Single().Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in TabularImport.ToImportedData(tables).For("Product")[0].Keys)
            Assert.Contains(key, declared);
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

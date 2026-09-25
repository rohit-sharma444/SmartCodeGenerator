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

    /// <summary>
    /// The cap is per entity, so truncating a parent can leave a child row pointing at a parent
    /// that was never imported. The generated app would then show an orphan whose lookup resolves
    /// to nothing, with no clue why. The foreign key is cleared instead — Ids are POSITIONAL, so
    /// dropping the row itself would renumber every row after it and break every other key.
    /// </summary>
    [Fact]
    public void AForeignKeyPastTheCappedParentIsCleared_NotLeftDangling()
    {
        var parent = new TabularTable { Name = "Orders", SourceLabel = "orders.csv" };
        parent.Columns.Add(new TabularColumn { Name = "Ref" });
        for (var i = 1; i <= ImportedData.MaxSeedRows + 5; i++)
            parent.Rows.Add([$"ORD-{i}"]);

        // Reverse order, so the child rows that survive the cap are the ones pointing at the
        // parent rows that did not.
        var child = new TabularTable { Name = "OrderItem", SourceLabel = "orders.csv" };
        child.Columns.Add(new TabularColumn { Name = "OrderId", Type = "int" });
        child.Columns.Add(new TabularColumn { Name = "Item" });
        for (var i = ImportedData.MaxSeedRows + 5; i >= 1; i--)
            child.Rows.Add([i.ToString(), "Nut"]);

        var data = TabularImport.ToImportedData([parent, child]);

        var parents = data.For("Order").Count;
        var keys = data.For("OrderItem").Select(r => r["OrderId"]).ToList();

        Assert.All(keys, k => Assert.True(
            k is null || int.Parse(k) <= parents,
            $"foreign key {k} points past the {parents} parent rows that were imported"));

        // ...and the user is told, rather than left to discover blank links in the running app.
        Assert.Contains(data.Notes, n => n.Contains("could not be linked"));
    }

    /// <summary>A foreign key naming an entity that was NOT imported is left alone — it is an
    /// ordinary int that happens to end in "Id", not a broken reference.</summary>
    [Fact]
    public void AnIdColumnNamingNoImportedEntityIsUntouched()
    {
        var data = TabularImport.ToImportedData(
            [Table("Orders", ["LegacyId", "Ref"], ["9999", "ORD-1"])]);

        Assert.Equal("9999", data.For("Order")[0]["LegacyId"]);
        Assert.Empty(data.Notes);
    }

    /// <summary>A table the model builder skips must not be imported either, or the carrier holds
    /// rows for a class that will never exist.</summary>
    [Fact]
    public void ATableTheModelBuilderSkipsIsNotImported()
    {
        var table = new TabularTable { Name = "123", SourceLabel = "123.csv" };
        table.Columns.Add(new TabularColumn { Name = "Ref" });
        table.Rows.Add(["ORD-1"]);

        Assert.Empty(DataModelBuilder.Build([table]).Entities);
        Assert.True(TabularImport.ToImportedData([table]).IsEmpty);
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

    /// <summary>
    /// Issue #131: a partly-read table says how much of it was read, and the seed note counts
    /// the table's rows rather than only the ones read.
    /// </summary>
    [Fact]
    public void A_partly_read_table_is_stated()
    {
        var table = new TabularTable { Name = "Orders", SourceLabel = "TRADING.Orders", IsPartial = true, SourceRowCount = 47_000_000 };
        table.Columns.Add(new TabularColumn { Name = "Ref" });

        for (var i = 0; i < ImportedData.MaxSeedRows + 1; i++)
            table.Rows.Add([$"ORD-{i}"]);

        var data = TabularImport.ToImportedData([table]);

        Assert.Contains(data.Notes, n => n.Contains("47,000,000") && n.Contains("TRADING.Orders")
                                         && n.Contains($"{ImportedData.MaxSeedRows + 1:N0}"));
        Assert.Contains(data.Notes, n => n.Contains($"first {ImportedData.MaxSeedRows} of 47000000 rows"));
    }
}

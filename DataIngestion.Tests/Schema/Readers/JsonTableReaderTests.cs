using DataIngestion;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class JsonTableReaderTests
{
    [Fact]
    public void ArrayOfObjects_BecomesOneTable()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"Ref":"ORD-1","Qty":2},{"Ref":"ORD-2","Qty":3}]""",
            "Orders", "orders.json"));

        Assert.Equal("Order", table.Name);
        Assert.Equal(new[] { "Ref", "Qty" }, table.Columns.Select(c => c.Name));
        Assert.Equal("int", table.Columns[1].Type);
        Assert.Equal("ORD-2", table.Value(1, "Ref"));
    }

    /// <summary>Objects need not agree on their keys: the union is the column set, and a row
    /// missing a key has a null there rather than shifting its other values along.</summary>
    [Fact]
    public void ColumnsAreTheUnionOfEveryObjectsKeys()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"A":1},{"B":2}]""", "Thing", "t.json"));

        Assert.Equal(new[] { "A", "B" }, table.Columns.Select(c => c.Name));
        Assert.Null(table.Value(0, "B"));
        Assert.Equal("2", table.Value(1, "B"));
    }

    [Fact]
    public void NestedArray_BecomesAChildTableWithAForeignKeyToItsParent()
    {
        var tables = JsonTableReader.ReadJson(
            """
            [{"Ref":"ORD-1","Lines":[{"Sku":"A","Qty":2},{"Sku":"B","Qty":1}]},
             {"Ref":"ORD-2","Lines":[{"Sku":"C","Qty":5}]}]
            """,
            "Orders", "orders.json");

        Assert.Equal(2, tables.Count);

        // The array is not a column on the parent -- it is a table.
        Assert.DoesNotContain("Lines", tables[0].Columns.Select(c => c.Name));

        var child = tables[1];
        Assert.Equal("Line", child.Name);
        Assert.Equal(3, child.Rows.Count);
        Assert.Equal(new[] { "OrderId", "Sku", "Qty" }, child.Columns.Select(c => c.Name));

        // 1-based parent row index, because seeded Ids are assigned 1..N in row order.
        Assert.Equal("1", child.Value(0, "OrderId"));
        Assert.Equal("2", child.Value(2, "OrderId"));
        Assert.Equal("int", child.Columns.Single(c => c.Name == "OrderId").Type);
    }

    /// <summary>A nested single object is one-to-one, so its keys are flattened onto the parent
    /// with a prefix rather than becoming a table of one row per parent.</summary>
    [Fact]
    public void NestedObject_IsFlattenedOntoTheParentWithAPrefix()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"Ref":"ORD-1","Address":{"City":"London","PostCode":"E1"}}]""",
            "Orders", "orders.json"));

        Assert.Equal(new[] { "Ref", "AddressCity", "AddressPostCode" },
            table.Columns.Select(c => c.Name));
        Assert.Equal("London", table.Value(0, "AddressCity"));
    }

    /// <summary>An array of scalars is non-atomic data, not a table of objects. It becomes a
    /// child table of one value column -- the same shape the normalization analyser proposes for
    /// the same data written as a comma-separated string.</summary>
    [Fact]
    public void NestedScalarArray_BecomesAChildTableOfValues()
    {
        var tables = JsonTableReader.ReadJson(
            """[{"Ref":"ORD-1","Tags":["rush","gift"]}]""", "Orders", "orders.json");

        var child = tables[1];
        Assert.Equal("Tag", child.Name);
        Assert.Equal(new[] { "OrderId", "Value" }, child.Columns.Select(c => c.Name));
        Assert.Equal("rush", child.Value(0, "Value"));
        Assert.Equal(2, child.Rows.Count);
    }

    [Fact]
    public void JsonNull_IsAnAbsentValue()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"A":null},{"A":"x"}]""", "Thing", "t.json"));

        Assert.Null(table.Value(0, "A"));
    }

    [Fact]
    public void BooleanAndNumericJson_KeepTheirTypes()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"Paid":true,"Total":9.99},{"Paid":false,"Total":1.50}]""", "Thing", "t.json"));

        Assert.Equal("bool", table.Columns.Single(c => c.Name == "Paid").Type);
        Assert.Equal("decimal", table.Columns.Single(c => c.Name == "Total").Type);
    }

    [Fact]
    public void SingleTopLevelObject_IsReadAsAOneRowTable()
        => Assert.Single(Assert.Single(JsonTableReader.ReadJson(
            """{"Ref":"ORD-1"}""", "Orders", "orders.json")).Rows);

    [Fact]
    public void EmptyArray_IsRejectedWithAMessageThatSaysWhy()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => JsonTableReader.ReadJson("[]", "Orders", "orders.json"));

        Assert.Contains("no objects", error.Message);
    }

    [Fact]
    public void MalformedJson_IsRejectedWithAMessageNamingTheFile()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => JsonTableReader.ReadJson("{not json", "Orders", "orders.json"));

        Assert.Contains("orders.json", error.Message);
    }

    [Fact]
    public void ReadsAFileFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, """[{"Ref":"ORD-1"}]""");

        try
        {
            var table = Assert.Single(
                new JsonTableReader().Read(new DataSourceSpec(DataSourceKind.Json, path)));

            Assert.Equal("ORD-1", table.Value(0, "Ref"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

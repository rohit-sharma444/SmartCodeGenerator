using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

/// <summary>
/// JSON names that meet after flattening used to overwrite each other ("customerName" and
/// customer.name both CustomerName; "total" and "Total"), arrays of one name under different
/// parents merged into one table, and a wrapper object became an entity of its own.
/// </summary>
public class JsonNameCollisionTests
{
    [Fact]
    public void AFlattenedNameThatMeetsAnother_IsNumbered_AndNeitherValueIsLost()
    {
        var table = JsonTableReader.ReadJson(
            """
            [{"customerName":"Top Level Name","customer":{"name":"Ada Lovelace"}},
             {"customer":{"name":"Grace Hopper"}}]
            """,
            "orders", "orders.json")[0];

        Assert.Equal(new[] { "CustomerName", "CustomerName2" }, table.Columns.Select(c => c.Name));
        Assert.Equal("Top Level Name", table.Value(0, "CustomerName"));
        Assert.Equal("Ada Lovelace", table.Value(0, "CustomerName2"));
        Assert.Equal("Grace Hopper", table.Value(1, "CustomerName2"));
        Assert.Null(table.Value(1, "CustomerName"));
        Assert.Equal("CustomerName", table.Columns[1].DuplicateOf);
        Assert.Contains(table.Notes, n => n.Contains("CustomerName2"));
    }

    [Fact]
    public void TwoPropertiesDifferingOnlyInCase_AreTwoColumns()
    {
        var table = JsonTableReader.ReadJson(
            """[{"total":1234.5},{"total":-5.25,"Total":999}]""", "orders", "orders.json")[0];

        Assert.Equal("-5.25", table.Value(1, "Total"));
        Assert.Equal("999", table.Value(1, "Total2"));
        Assert.Null(table.Value(0, "Total2"));
    }

    [Fact]
    public void ArraysOfOneNameUnderDifferentParents_AreTwoTables()
    {
        var tables = JsonTableReader.ReadJson(
            """
            [{"id":1,"billing":{"contacts":[{"who":"Bill A"}]},"shipping":{"contacts":[{"who":"Ship A"}]}},
             {"id":2,"billing":{"contacts":[{"who":"Bill B"}]},"shipping":{"contacts":[]}}]
            """,
            "orders", "orders.json");

        var billing = Assert.Single(tables, t => t.Name == "BillingContact");
        var shipping = Assert.Single(tables, t => t.Name == "ShippingContact");

        Assert.Equal(new[] { "Bill A", "Bill B" }, billing.ColumnValues("Who"));
        Assert.Equal(new[] { "Ship A" }, shipping.ColumnValues("Who"));
        Assert.DoesNotContain(tables, t => t.Name == "Contact");
    }

    [Fact]
    public void AnArrayWhoseNameIsItsOwn_KeepsThePlainName()
    {
        var tables = JsonTableReader.ReadJson(
            """[{"id":1,"billing":{"contacts":[{"who":"Bill A"}]}}]""", "orders", "orders.json");

        Assert.Contains(tables, t => t.Name == "Contact");
    }

    [Fact]
    public void AWrapperObjectAroundOneArray_IsUnwrapped()
    {
        var tables = JsonTableReader.ReadJson(
            """{"orders":[{"id":10,"name":"a","lines":[{"sku":"x"}]},{"id":20,"name":"b","lines":[{"sku":"y"}]}]}""",
            "wrapped", "wrapped.json");

        Assert.Equal(new[] { "Order", "Line" }, tables.Select(t => t.Name));

        var orders = tables[0];
        Assert.Equal(2, orders.Rows.Count);
        Assert.Equal(new[] { "Id", "Name" }, orders.Columns.Select(c => c.Name));
        Assert.Equal("b", orders.Value(1, "Name"));
    }

    [Fact]
    public void AnObjectWithMoreThanTheArray_IsStillOneRow()
    {
        var tables = JsonTableReader.ReadJson(
            """{"name":"shop","orders":[{"id":10}]}""", "wrapped", "wrapped.json");

        Assert.Equal("Wrapped", tables[0].Name);
        Assert.Single(tables[0].Rows);
    }
}

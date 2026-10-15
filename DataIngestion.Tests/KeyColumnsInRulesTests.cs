using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// A key holds row numbers or the source's own codes, not a quantity: "Line.OrderId, Qty —
/// OrderId is never more than Qty" is a fact about numbering, and enforcing it would refuse the
/// thousandth order's lines.
/// </summary>
public class KeyColumnsInRulesTests
{
    [Fact]
    public void AJsonChildsParentKey_TakesPartInNoComparisonRule()
    {
        var tables = JsonTableReader.ReadJson(
            """
            [{"id":1001,"lines":[{"orderId":1001,"sku":"A1","qty":2},{"orderId":1001,"sku":"B2","qty":1}]},
             {"id":1002,"lines":[{"orderId":1002,"sku":"C3","qty":3}]}]
            """,
            "orders", "orders.json");

        var model = DataModelBuilder.Build(tables);
        var line = Assert.Single(model.Entities, e => e.Name == "Line");

        Assert.Contains(line.Fields, f => f.Name == "OrderId");
        Assert.DoesNotContain(line.Proposals, p => p.Properties.Contains("OrderId"));
    }

    [Fact]
    public void AnUnlinkedKeyNamedColumn_TakesPartInNoComparisonRule()
    {
        // WarehouseId names no table of this run, so it is not a link — but it is still a key.
        var table = CsvReader.FromText(
            "stock", "stock.csv", "Sku,WarehouseId,Qty\nA,1,5\nB,2,7\nC,3,9\n", ',');

        var model = DataModelBuilder.Build([table]);
        var stock = Assert.Single(model.Entities);

        Assert.DoesNotContain(stock.Proposals, p => p.Properties.Contains("WarehouseId"));
    }
}

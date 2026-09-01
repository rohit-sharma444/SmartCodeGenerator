using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests.Schema;

public class TabularForeignKeyTests
{
    // Every other reader leaves this empty, so the default has to be a usable empty list rather
    // than null - a reader that never heard of foreign keys must not force a null check on
    // DataModelBuilder.
    [Fact]
    public void A_table_starts_with_no_foreign_keys()
    {
        var table = new TabularTable { Name = "Orders" };

        Assert.Empty(table.ForeignKeys);
    }

    [Fact]
    public void A_foreign_key_names_its_column_and_what_it_points_at()
    {
        var table = new TabularTable { Name = "Orders" };

        table.ForeignKeys.Add(new TabularForeignKey("CustomerID", "Customers", "CustomerID"));

        var key = Assert.Single(table.ForeignKeys);
        Assert.Equal("CustomerID", key.Column);
        Assert.Equal("Customers", key.TargetTable);
        Assert.Equal("CustomerID", key.TargetColumn);
    }
}

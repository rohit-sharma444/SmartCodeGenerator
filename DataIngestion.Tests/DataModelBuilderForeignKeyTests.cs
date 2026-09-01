using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// Relationships read from a source that DECLARES them, rather than inferred from column names.
/// </summary>
public class DataModelBuilderForeignKeyTests
{
    private static TabularTable Table(string name, params string[] columns)
    {
        var table = new TabularTable { Name = name };

        foreach (var column in columns)
            table.Columns.Add(new TabularColumn { Name = column, Type = "int" });

        return table;
    }

    [Fact]
    public void A_declared_foreign_key_becomes_a_relationship()
    {
        var customers = Table("Customer", "Name");
        var orders = Table("Order", "Placed");
        orders.ForeignKeys.Add(new TabularForeignKey("CustomerID", "Customer", "CustomerID"));

        var model = DataModelBuilder.Build([customers, orders]);

        var order = model.Entities.Single(e => e.Name == "Order");
        Assert.Contains(order.Relationships, r => r.Target == "Customer");
    }

    /// <summary>
    /// The name trap. Entity names go through NameNormalizer, so a foreign key naming the raw SQL
    /// table has to be normalised the same way or it matches nothing and the relationship is lost
    /// without a word.
    /// </summary>
    [Fact]
    public void A_foreign_key_to_a_table_whose_name_was_normalised_still_resolves()
    {
        var orders = Table("Order", "Placed");
        var details = Table("Order Details", "Quantity");
        details.ForeignKeys.Add(new TabularForeignKey("OrderID", "Order", "OrderID"));

        var model = DataModelBuilder.Build([orders, details]);

        var entity = model.Entities.Single(e => e.Name != "Order");
        Assert.Contains(entity.Relationships, r => r.Target == "Order");
    }

    /// <summary>
    /// The duplication trap. AddRelationships already emits a relationship for any int field
    /// ending in Id, so a foreign key on CustomerId is seen twice — and twice means
    /// DomainModelMapper.AddNavigationProperties tries to write the same navigation property
    /// twice.
    /// </summary>
    [Fact]
    public void A_foreign_key_the_convention_would_also_find_is_recorded_once()
    {
        var customer = Table("Customer", "Name");
        var order = Table("Order", "CustomerId");
        order.ForeignKeys.Add(new TabularForeignKey("CustomerId", "Customer", "CustomerId"));

        var model = DataModelBuilder.Build([customer, order]);

        var entity = model.Entities.Single(e => e.Name == "Order");
        Assert.Single(entity.Relationships, r => r.Target == "Customer");
    }

    [Fact]
    public void A_foreign_key_to_a_table_that_was_not_picked_is_dropped()
    {
        var orders = Table("Order", "Placed");
        orders.ForeignKeys.Add(new TabularForeignKey("ShipperID", "Shipper", "ShipperID"));

        var model = DataModelBuilder.Build([orders]);

        Assert.Empty(model.Entities.Single().Relationships);
    }

    [Fact]
    public void A_self_referencing_foreign_key_is_dropped()
    {
        var employees = Table("Employee", "ReportsTo");
        employees.ForeignKeys.Add(new TabularForeignKey("ReportsTo", "Employee", "EmployeeID"));

        var model = DataModelBuilder.Build([employees]);

        var entity = model.Entities.Single();
        Assert.DoesNotContain(entity.Relationships, r => r.Target == entity.Name);
    }

    // A file reader declares nothing, and must keep behaving exactly as it did.
    [Fact]
    public void A_table_with_no_declared_keys_still_gets_the_convention_relationships()
    {
        var customer = Table("Customer", "Name");
        var order = Table("Order", "CustomerId");

        var model = DataModelBuilder.Build([customer, order]);

        var entity = model.Entities.Single(e => e.Name == "Order");
        Assert.Contains(entity.Relationships, r => r.Target == "Customer");
    }

    /// <summary>
    /// Two columns pointing at the same table is ordinary - a bill-to and a ship-to customer.
    /// The model has one relationship per target, so the second must not be added again.
    /// </summary>
    [Fact]
    public void Two_foreign_keys_to_the_same_table_make_one_relationship()
    {
        var customer = Table("Customer", "Name");
        var order = Table("Order", "Placed");
        order.ForeignKeys.Add(new TabularForeignKey("BillToCustomerID", "Customer", "CustomerID"));
        order.ForeignKeys.Add(new TabularForeignKey("ShipToCustomerID", "Customer", "CustomerID"));

        var model = DataModelBuilder.Build([customer, order]);

        var entity = model.Entities.Single(e => e.Name == "Order");
        Assert.Single(entity.Relationships, r => r.Target == "Customer");
    }
}

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
        var orders = Table("Order", "Placed", "CustomerID");
        orders.ForeignKeys.Add(new TabularForeignKey("CustomerID", "Customer", "CustomerID"));

        var model = DataModelBuilder.Build([customers, orders]);

        var order = model.Entities.Single(e => e.Name == "Order");
        Assert.Contains(order.Relationships, r => r.Target == "Customer" && r.ForeignKey == "CustomerId");
    }

    /// <summary>
    /// A SQL Server habit: <c>CustomerID</c>, not <c>CustomerId</c>. The relationship was found
    /// case-blind, but every part of the generated app that treats a column as a foreign key —
    /// the seeder, the validators, the lookup pickers — matches <c>&lt;Class&gt;Id</c> exactly,
    /// so the class got a Customer navigation property and a key nothing ever read.
    /// </summary>
    [Fact]
    public void An_upper_case_ID_foreign_key_is_one_the_generated_app_treats_as_a_foreign_key()
    {
        var customers = Table("Customer", "CustomerID");
        customers.Columns.Add(new TabularColumn { Name = "Name" });
        customers.Rows.Add(["1", "Ada"]);
        var orders = Table("Order", "OrderID", "CustomerID");
        orders.Rows.Add(["1", "1"]);

        var classes = AppScaffolding.DomainModelMapper.ToClassDefinitions(
            DataModelBuilder.Build([customers, orders]));

        var order = classes.Single(c => c.Name == "Order");
        Assert.Equal(["Customer"], AppScaffolding.ForeignKeys.TargetsOf(
            order, AppScaffolding.ForeignKeys.ClassNameSet(classes)));

        // ...and the seed row is keyed by the same spelling, or the key is written nowhere.
        var row = TabularImport.ToImportedData([customers, orders]).For("Order")[0];
        Assert.Equal("1", row["CustomerId"]);
        Assert.Contains("CustomerId", row.Keys);
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
        var details = Table("Order Details", "Quantity", "OrderID");
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

    /// <summary>A manager is another employee: the link is kept, under a name that reads as a
    /// link and not as the table's own key. Dropped, its values kept the source's keys while the
    /// rows they named were renumbered.</summary>
    [Fact]
    public void A_self_referencing_foreign_key_is_kept()
    {
        var employees = Table("Employee", "ReportsTo");
        employees.ForeignKeys.Add(new TabularForeignKey("ReportsTo", "Employee", "EmployeeID"));

        var model = DataModelBuilder.Build([employees]);

        var entity = model.Entities.Single();
        Assert.Single(entity.Relationships, r => r.Target == entity.Name && r.ForeignKey == "ReportsToId");
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
    /// A declared key whose column is named for the TABLE, not the class: SQL's
    /// <c>TableDefinitionsId</c> pointing at <c>TableDefinitions</c>, whose class is
    /// <c>TableDefinition</c>. The column became <c>int TableDefinitionsId</c> beside a
    /// <c>TableDefinition</c> navigation the resolver never paired with it, and its values kept
    /// the source keys (330, 331...) while the parents were renumbered 1..N.
    /// </summary>
    [Fact]
    public void A_declared_key_named_for_the_plural_table_is_paired_and_renumbered()
    {
        var definitions = Table("TableDefinitions", "Id");
        definitions.Columns.Add(new TabularColumn { Name = "TableName" });
        definitions.Rows.Add(["330", "Orders"]);
        definitions.Rows.Add(["331", "Lines"]);

        var links = Table("SnapshotTemplateTables", "Id", "TableDefinitionsId");
        links.ForeignKeys.Add(new TabularForeignKey("TableDefinitionsId", "TableDefinitions", "Id"));
        links.Rows.Add(["1", "331"]);
        links.Rows.Add(["2", "330"]);

        var classes = AppScaffolding.DomainModelMapper.ToClassDefinitions(
            DataModelBuilder.Build([definitions, links]));

        var link = classes.Single(c => c.Name == "SnapshotTemplateTable");
        Assert.Equal(["TableDefinition"], AppScaffolding.ForeignKeys.TargetsOf(
            link, AppScaffolding.ForeignKeys.ClassNameSet(classes)));

        var rows = TabularImport.ToImportedData([definitions, links]).For("SnapshotTemplateTable");
        Assert.Equal("2", rows[0]["TableDefinitionId"]);
        Assert.Equal("1", rows[1]["TableDefinitionId"]);
    }

    /// <summary>
    /// The plural table's OWN key spelled the same way: <c>Customers.CustomersId</c>. It was read
    /// as an ordinary int field on Customer, so Customer was not renumbered and every
    /// <c>Shipment.CustomerId</c> (101, 103) named no row and was cleared - the shipments lost
    /// their customers.
    /// </summary>
    [Fact]
    public void A_plural_table_keyed_by_its_own_name_is_renumbered_and_its_references_kept()
    {
        var customers = Table("Customers", "CustomersId");
        customers.Columns.Add(new TabularColumn { Name = "Name" });
        customers.Rows.Add(["101", "Acme"]);
        customers.Rows.Add(["103", "Initech"]);

        var shipments = Table("Shipment", "ShipmentId", "CustomersId");
        shipments.ForeignKeys.Add(new TabularForeignKey("CustomersId", "Customers", "CustomersId"));
        shipments.Rows.Add(["1", "103"]);
        shipments.Rows.Add(["2", "101"]);

        var model = DataModelBuilder.Build([customers, shipments]);
        Assert.DoesNotContain(
            model.Entities.Single(e => e.Name == "Customer").Fields, f => f.Name == "CustomersId");

        var rows = TabularImport.ToImportedData([customers, shipments]).For("Shipment");
        Assert.Equal("2", rows[0]["CustomerId"]);
        Assert.Equal("1", rows[1]["CustomerId"]);
    }

    /// <summary>
    /// A declared key the class name cannot be read from (a bill-to customer) keeps its column
    /// name, but its values still follow the parent's renumbering: left as the source keys they
    /// would name whichever parent happens to sit at that position.
    /// </summary>
    [Fact]
    public void A_declared_key_with_its_own_name_is_still_renumbered()
    {
        var customer = Table("Customer", "CustomerID");
        customer.Columns.Add(new TabularColumn { Name = "Name" });
        customer.Rows.Add(["70", "Ada"]);
        customer.Rows.Add(["71", "Bob"]);

        var order = Table("Order", "OrderID", "BillToCustomerID");
        order.ForeignKeys.Add(new TabularForeignKey("BillToCustomerID", "Customer", "CustomerID"));
        order.Rows.Add(["1", "71"]);

        var row = TabularImport.ToImportedData([customer, order]).For("Order")[0];
        Assert.Equal("2", row["BillToCustomerId"]);
    }

    /// <summary>
    /// Two columns pointing at the same table is ordinary - a bill-to and a ship-to customer.
    /// Each is its own relationship, naming the field that holds it; one per target collapsed
    /// them into a single navigation neither key was paired with.
    /// </summary>
    [Fact]
    public void Two_foreign_keys_to_the_same_table_make_two_relationships()
    {
        var customer = Table("Customer", "Name");
        var order = Table("Order", "Placed", "BillToCustomerID", "ShipToCustomerID");
        order.ForeignKeys.Add(new TabularForeignKey("BillToCustomerID", "Customer", "CustomerID"));
        order.ForeignKeys.Add(new TabularForeignKey("ShipToCustomerID", "Customer", "CustomerID"));

        var model = DataModelBuilder.Build([customer, order]);

        var entity = model.Entities.Single(e => e.Name == "Order");
        Assert.Equal(
            ["BillToCustomerId", "ShipToCustomerId"],
            entity.Relationships.Where(r => r.Target == "Customer").Select(r => r.ForeignKey).Order());
    }
}

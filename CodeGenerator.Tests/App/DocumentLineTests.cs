using AppScaffolding.Screens;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A line entity gets no screen of its own — it is edited inside its parent's editor. A false
/// positive therefore HIDES a screen, which is why the name test is strict rather than
/// shape-based: an extra screen is obvious, a missing one is not.
/// </summary>
[TestClass]
public class DocumentLineTests
{
    /// <summary>Customer, Order, and the order's lines — the shape the whole design aims at.</summary>
    private static List<ClassDefinition> OrderModel(string lineName)
        =>
        [
            EntityRolesTests.Entity("Customer", ("Name", "string")),
            EntityRolesTests.Entity(
                "Order", [.. EntityRolesTests.ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
            EntityRolesTests.Entity(
                lineName,
                [
                    .. EntityRolesTests.ForeignKeyTo("Order"),
                    ("Quantity", "int"), ("UnitPrice", "decimal"),
                ]),
        ];

    [TestMethod]
    public void A_child_named_after_its_transaction_is_a_line()
    {
        var map = EntityRoles.Classify(OrderModel("OrderLine"));

        Assert.AreEqual(EntityRole.DocumentLine, map.RoleOf("OrderLine"));
        Assert.AreEqual(EntityRole.Transaction, map.RoleOf("Order"));
    }

    [TestMethod]
    public void The_transaction_reports_the_line_as_its_own()
    {
        var map = EntityRoles.Classify(OrderModel("OrderLine"));

        Assert.HasCount(1, map.LinesOf("Order"));
        Assert.AreEqual("OrderLine", map.LinesOf("Order")[0]);
    }

    [TestMethod]
    public void Every_line_suffix_is_recognised()
    {
        foreach (var suffix in new[] { "Line", "Item", "Detail", "Row" })
        {
            var map = EntityRoles.Classify(OrderModel("Order" + suffix));

            Assert.AreEqual(
                EntityRole.DocumentLine, map.RoleOf("Order" + suffix), $"suffix {suffix}");
        }
    }

    /// <summary>A bare suffix counts too: a child of Order called simply "LineItem" is still its
    /// lines.</summary>
    [TestMethod]
    public void A_name_that_merely_ends_in_a_suffix_is_a_line()
        => Assert.AreEqual(
            EntityRole.DocumentLine, EntityRoles.Classify(OrderModel("LineItem")).RoleOf("LineItem"));

    /// <summary>The strictness that matters. A child of a transaction whose name says nothing
    /// about lines keeps its own screen — hiding it would lose a screen silently.</summary>
    [TestMethod]
    public void A_child_of_a_transaction_with_an_unrelated_name_is_not_a_line()
    {
        var map = EntityRoles.Classify(OrderModel("Shipment"));

        Assert.AreNotEqual(EntityRole.DocumentLine, map.RoleOf("Shipment"));
        Assert.IsEmpty(map.LinesOf("Order"));
    }

    /// <summary>A line-sounding child of a MASTER is not a line: there is no document to put it
    /// inside, so it keeps a screen.</summary>
    [TestMethod]
    public void A_line_named_child_of_a_master_is_not_a_line()
    {
        var classes = new List<ClassDefinition>
        {
            EntityRolesTests.Entity("Customer", ("Name", "string")),
            EntityRolesTests.Entity(
                "CustomerLine", [.. EntityRolesTests.ForeignKeyTo("Customer"), ("Note", "string")]),
        };

        Assert.AreNotEqual(
            EntityRole.DocumentLine, EntityRoles.Classify(classes).RoleOf("CustomerLine"));
    }

    /// <summary>
    /// A dated document whose lines point at it is a transaction header even with no foreign key of
    /// its own. invoices.json nests each invoice's Lines array, the JSON reader makes Line a child
    /// with an InvoiceId, and the invoice names its customer as text: the model came out "2
    /// masters, 0 transactions", Line got a screen of its own and the invoice editor had no lines.
    /// </summary>
    [TestMethod]
    public void A_dated_parent_of_lines_is_a_transaction_even_with_no_foreign_key_of_its_own()
    {
        var classes = new List<ClassDefinition>
        {
            EntityRolesTests.Entity(
                "Invoice", ("InvoiceNumber", "string"), ("InvoiceDate", "DateTime"), ("CustomerName", "string")),
            EntityRolesTests.Entity(
                "Line",
                [.. EntityRolesTests.ForeignKeyTo("Invoice"), ("Product", "string"), ("Quantity", "int")]),
        };

        var map = EntityRoles.Classify(classes);

        Assert.AreEqual(EntityRole.Transaction, map.RoleOf("Invoice"));
        Assert.AreEqual(EntityRole.DocumentLine, map.RoleOf("Line"));
    }

    /// <summary>A line never appears among the navigable entities, which is the whole point of the
    /// role.</summary>
    [TestMethod]
    public void Lines_are_not_listed_as_masters_or_transactions()
    {
        var map = EntityRoles.Classify(OrderModel("OrderLine"));

        CollectionAssert.DoesNotContain(map.Names(EntityRole.Master).ToList(), "OrderLine");
        CollectionAssert.DoesNotContain(map.Names(EntityRole.Transaction).ToList(), "OrderLine");
    }

    /// <summary>Two line sets on one transaction are reported together rather than one
    /// overwriting the other.</summary>
    [TestMethod]
    public void A_transaction_can_have_more_than_one_kind_of_line()
    {
        var classes = OrderModel("OrderLine");

        classes.Add(EntityRolesTests.Entity(
            "OrderItem", [.. EntityRolesTests.ForeignKeyTo("Order"), ("Note", "string")]));

        Assert.HasCount(2, EntityRoles.Classify(classes).LinesOf("Order"));
    }
}

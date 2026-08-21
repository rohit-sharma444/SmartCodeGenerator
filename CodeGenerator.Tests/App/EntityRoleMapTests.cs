using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>The classifier's result type. Master is the answer for anything unknown, because a
/// misclassified master renders as the ordinary grid this generator already produced.</summary>
[TestClass]
public class EntityRoleMapTests
{
    private static EntityRoleMap Map()
        => new(
            new Dictionary<string, EntityRole>(StringComparer.OrdinalIgnoreCase)
            {
                ["Customer"] = EntityRole.Master,
                ["Order"] = EntityRole.Transaction,
                ["OrderLine"] = EntityRole.DocumentLine,
            },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Order"] = new[] { "OrderLine" },
            });

    [TestMethod]
    public void A_name_reports_the_role_it_was_given()
    {
        Assert.AreEqual(EntityRole.Master, Map().RoleOf("Customer"));
        Assert.AreEqual(EntityRole.Transaction, Map().RoleOf("Order"));
        Assert.AreEqual(EntityRole.DocumentLine, Map().RoleOf("OrderLine"));
    }

    /// <summary>Casing is not a classification. A caller asking about "order" means Order.</summary>
    [TestMethod]
    public void Names_are_matched_without_regard_to_case()
        => Assert.AreEqual(EntityRole.Transaction, Map().RoleOf("ORDER"));

    /// <summary>An unknown name is a Master, not an exception: the safe default is the screen this
    /// generator has always produced.</summary>
    [TestMethod]
    public void An_unknown_name_is_a_master()
        => Assert.AreEqual(EntityRole.Master, Map().RoleOf("Nonexistent"));

    [TestMethod]
    public void Names_can_be_listed_by_role()
    {
        CollectionAssert.AreEquivalent(new[] { "Customer" }, Map().Names(EntityRole.Master).ToList());
        CollectionAssert.AreEquivalent(new[] { "Order" }, Map().Names(EntityRole.Transaction).ToList());
    }

    [TestMethod]
    public void A_transaction_reports_its_lines()
    {
        Assert.HasCount(1, Map().LinesOf("Order"));
        Assert.AreEqual("OrderLine", Map().LinesOf("Order")[0]);
    }

    /// <summary>A transaction with no line entity is the common case, not an error.</summary>
    [TestMethod]
    public void A_transaction_with_no_lines_reports_an_empty_list()
        => Assert.IsEmpty(Map().LinesOf("Customer"));

    /// <summary>An empty model needs no null check at any call site.</summary>
    [TestMethod]
    public void The_empty_map_answers_master_for_everything()
    {
        Assert.AreEqual(EntityRole.Master, EntityRoleMap.Empty.RoleOf("Anything"));
        Assert.IsEmpty(EntityRoleMap.Empty.Names(EntityRole.Transaction));
    }
}

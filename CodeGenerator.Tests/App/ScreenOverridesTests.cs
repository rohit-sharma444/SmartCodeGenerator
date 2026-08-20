using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerationEngine.Models;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>The heuristic will be wrong sometimes; this is how a user says so. Two value keys,
/// comma-joined without spaces, because the parser splits on whitespace.</summary>
[TestClass]
public class ScreenOverridesTests
{
    private static List<ClassDefinition> Model() =>
    [
        EntityRolesTests.Entity("Customer", ("Name", "string")),
        EntityRolesTests.Entity("Region", ("Name", "string")),
        EntityRolesTests.Entity(
            "Order", [.. EntityRolesTests.ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
    ];

    [TestMethod]
    public void The_defaults_override_nothing()
    {
        Assert.AreEqual(string.Empty, AppCustomization.Default.Transactions);
        Assert.AreEqual(string.Empty, AppCustomization.Default.Masters);
        Assert.IsFalse(ScreenOverrides.None.Any);
    }

    [TestMethod]
    public void A_named_transaction_becomes_one_even_with_no_foreign_key()
        => Assert.AreEqual(
            EntityRole.Transaction,
            EntityRoles.Classify(Model(), ScreenOverrides.Parse("Region", string.Empty)).RoleOf("Region"));

    [TestMethod]
    public void A_named_master_stays_a_master_even_when_the_heuristic_disagrees()
        => Assert.AreEqual(
            EntityRole.Master,
            EntityRoles.Classify(Model(), ScreenOverrides.Parse(string.Empty, "Order")).RoleOf("Order"));

    /// <summary>Naming a line entity as a master promotes it to a screen of its own — the only
    /// correction anyone would want to make to line detection.</summary>
    [TestMethod]
    public void Naming_a_line_as_a_master_gives_it_its_own_screen()
    {
        var classes = new List<ClassDefinition>
        {
            EntityRolesTests.Entity("Customer", ("Name", "string")),
            EntityRolesTests.Entity(
                "Order", [.. EntityRolesTests.ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
            EntityRolesTests.Entity(
                "OrderLine", [.. EntityRolesTests.ForeignKeyTo("Order"), ("Quantity", "int")]),
        };

        var map = EntityRoles.Classify(classes, ScreenOverrides.Parse(string.Empty, "OrderLine"));

        Assert.AreEqual(EntityRole.Master, map.RoleOf("OrderLine"));
        Assert.IsEmpty(map.LinesOf("Order"), "a promoted line is no longer its parent's lines");
    }

    [TestMethod]
    public void Several_names_are_comma_separated()
    {
        var overrides = ScreenOverrides.Parse("Region,Customer", string.Empty);

        Assert.IsTrue(overrides.IsTransaction("Region"));
        Assert.IsTrue(overrides.IsTransaction("Customer"));
    }

    /// <summary>Typed by a person, so spaces after commas and odd casing both have to work.</summary>
    [TestMethod]
    public void Names_are_trimmed_and_matched_without_regard_to_case()
    {
        var overrides = ScreenOverrides.Parse("region, CUSTOMER", string.Empty);

        Assert.IsTrue(overrides.IsTransaction("Region"));
        Assert.IsTrue(overrides.IsTransaction("Customer"));
    }

    /// <summary>Naming the same entity both ways is a contradiction. Master wins, because it is
    /// the safe answer: an entity wrongly made a master is an ordinary grid.</summary>
    [TestMethod]
    public void Master_wins_when_a_name_is_given_to_both()
        => Assert.AreEqual(
            EntityRole.Master,
            EntityRoles.Classify(Model(), ScreenOverrides.Parse("Order", "Order")).RoleOf("Order"));

    /// <summary>A name that matches nothing is ignored rather than fatal: it was typed before
    /// discovery ran, and may simply be misremembered.</summary>
    [TestMethod]
    public void A_name_that_matches_no_entity_changes_nothing()
    {
        var map = EntityRoles.Classify(Model(), ScreenOverrides.Parse("Nonexistent", string.Empty));

        Assert.AreEqual(EntityRole.Master, map.RoleOf("Customer"));
        Assert.AreEqual(EntityRole.Transaction, map.RoleOf("Order"));
    }

    // ---- the inline parser ----

    [TestMethod]
    public void The_inline_options_are_read_off_the_line()
    {
        var (remaining, customization) = InlineCustomization.Split(
            "a shop transactions Order,Invoice masters Region,Category", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("Order,Invoice", customization.Transactions);
        Assert.AreEqual("Region,Category", customization.Masters);
    }

    /// <summary>Options are stripped right to left, so these have to sit happily beside the flags
    /// and the other value keys.</summary>
    [TestMethod]
    public void They_read_beside_the_other_options()
    {
        var (remaining, customization) = InlineCustomization.Split(
            "a shop company Codis transactions Order nologin", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("Order", customization.Transactions);
        Assert.AreEqual("Codis", customization.Company);
        Assert.IsFalse(customization.Login);
    }

    /// <summary>An unusable value leaves the line alone rather than silently eating two tokens —
    /// the rule ApplyValue already follows for `theme mauve`.</summary>
    [TestMethod]
    public void An_empty_value_is_not_taken()
    {
        var (remaining, customization) =
            InlineCustomization.Split("a shop transactions \"\"", AppCustomization.Default);

        Assert.AreEqual("a shop transactions \"\"", remaining);
        Assert.AreEqual(string.Empty, customization.Transactions);
    }

    /// <summary>The record is compared by value across the existing suite; a collection member
    /// would break that silently, which is why these are strings.</summary>
    [TestMethod]
    public void The_record_still_compares_by_value()
    {
        Assert.AreEqual(
            AppCustomization.Default with { Transactions = "Order" },
            AppCustomization.Default with { Transactions = "Order" });

        Assert.AreNotEqual(
            AppCustomization.Default with { Transactions = "Order" },
            AppCustomization.Default with { Transactions = "Invoice" });
    }
}

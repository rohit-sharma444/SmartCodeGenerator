using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>A silent misclassification is discoverable only by launching the application and
/// noticing a screen is missing or shaped wrongly, so the build says what it decided.</summary>
[TestClass]
public class ScreenSummaryTests
{
    private static EntityRoleMap ShopModel() => EntityRoles.Classify(
    [
        EntityRolesTests.Entity("Customer", ("Name", "string")),
        EntityRolesTests.Entity("Product", ("Name", "string")),
        EntityRolesTests.Entity(
            "Order", [.. EntityRolesTests.ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
    ]);

    [TestMethod]
    public void It_counts_both_kinds()
    {
        var summary = ScreenSummary.Describe(ShopModel());

        StringAssert.Contains(summary, "2 masters");
        StringAssert.Contains(summary, "1 transaction");
    }

    /// <summary>Singular and plural both read properly; "1 masters" is the kind of detail that
    /// makes generated output look machine-made.</summary>
    [TestMethod]
    public void One_of_something_is_singular()
    {
        var map = EntityRoles.Classify([EntityRolesTests.Entity("Customer", ("Name", "string"))]);

        StringAssert.Contains(ScreenSummary.Describe(map), "1 master");
        Assert.IsFalse(ScreenSummary.Describe(map).Contains("1 masters", StringComparison.Ordinal));
    }

    /// <summary>Lines are the detail worth naming: they are the entities that got NO screen, and
    /// nothing else tells the user that happened.</summary>
    [TestMethod]
    public void A_transaction_with_lines_says_so_by_name()
    {
        var map = EntityRoles.Classify(
        [
            EntityRolesTests.Entity("Customer", ("Name", "string")),
            EntityRolesTests.Entity(
                "Order", [.. EntityRolesTests.ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
            EntityRolesTests.Entity(
                "OrderLine", [.. EntityRolesTests.ForeignKeyTo("Order"), ("Quantity", "int")]),
        ]);

        StringAssert.Contains(ScreenSummary.Describe(map), "Order has 1 line");
    }

    [TestMethod]
    public void An_empty_model_says_nothing()
        => Assert.AreEqual(string.Empty, ScreenSummary.Describe(EntityRoleMap.Empty));

    /// <summary>It reaches the caller, not just the pure function.</summary>
    [TestMethod]
    public void The_scaffold_result_carries_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "sum-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        var result = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(TestModels.OneEntity(), "Shop", root, new WpfTarget()));

        StringAssert.Contains(result.ScreenSummary, "1 master");
    }

    /// <summary>And it reflects the user's override rather than only the heuristic — otherwise the
    /// line reports a decision the build did not make.</summary>
    [TestMethod]
    public void The_summary_follows_the_override()
    {
        var root = Path.Combine(Path.GetTempPath(), "sum-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Region",
            Fields = { new CandidateField { Name = "Name", Type = "string" } },
        });

        var result = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(model, "Shop", root, new WpfTarget())
                with { Customization = AppCustomization.Default with { Transactions = "Region" } });

        StringAssert.Contains(result.ScreenSummary, "1 transaction");
    }
}

using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TestProjectTemplatesTests
{
    [TestMethod]
    public void RoundTripTests_EmitsAFactPerEntity_UsingTheTypedRepository()
    {
        var src = TestProjectTemplates.RoundTripTests("Shop", new[] { "Customer", "Order" });

        StringAssert.Contains(src, "namespace Shop.Tests;");
        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "public void Customer_round_trips() => RoundTrip(new CustomerRepository());");
        StringAssert.Contains(src, "public void Order_round_trips() => RoundTrip(new OrderRepository());");
        StringAssert.Contains(src, "repository.Delete(id)");
    }

    [TestMethod]
    public void SeedTests_AssertsPopulatedAndIdempotent()
    {
        var src = TestProjectTemplates.SeedTests("Shop", "Customer");

        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        StringAssert.Contains(src, "new CustomerRepository().GetAll().Count");
        StringAssert.Contains(src, "Assert.Equal(afterFirst, afterSecond);");
    }

    [TestMethod]
    public void DisableParallelization_EmitsTheAssemblyAttribute()
    {
        StringAssert.Contains(
            TestProjectTemplates.DisableParallelization(),
            "[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]");
    }

    [TestMethod]
    public void InjectProjectReference_AddsAReferenceItemGroupBeforeTheClosingProjectTag()
    {
        var csproj = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n  </PropertyGroup>\n</Project>\n";

        var result = TestProjectTemplates.InjectProjectReference(csproj, @"..\Shop\Shop.csproj");

        StringAssert.Contains(result, "<ProjectReference Include=\"..\\Shop\\Shop.csproj\" />");
        Assert.IsLessThan(result.IndexOf("</Project>"), result.IndexOf("<ProjectReference"));
        Assert.EndsWith("</Project>", result.TrimEnd(), "closing tag preserved");
    }

    [TestMethod]
    public void Validator_tests_assert_a_required_field_when_the_model_has_one()
    {
        var source = TestProjectTemplates.ValidatorTests("Shop", "Customer", "Name");

        Assert.Contains("class CustomerValidatorTests", source);
        Assert.Contains("Name is required.", source);
        Assert.Contains("IEntityLookup", source);
    }

    [TestMethod]
    public void Validator_tests_for_a_model_with_no_required_field_still_assert_a_valid_record_passes()
    {
        var source = TestProjectTemplates.ValidatorTests("Shop", "Tag", null);

        Assert.Contains("class TagValidatorTests", source);
        Assert.DoesNotContain("is required.", source);
    }

    [TestMethod]
    public void Validator_tests_fill_the_record_through_the_shared_helper()
    {
        // TestSupport is emitted once, in the same Tests namespace; a second copy of the
        // fill logic here would be free to drift from the rules it is meant to satisfy.
        var source = TestProjectTemplates.ValidatorTests("Shop", "Customer", "Name");

        Assert.Contains("TestSupport.Fill(item);", source);
        Assert.DoesNotContain("Placeholder(", source);
    }

    /// <summary>
    /// An app with a login refuses a null role before any field rule runs, so the populated and
    /// the blank record are both validated as <c>TestSupport.ActingRole</c> - the administrator
    /// there, nobody in an app with no login. What each shape emits for it, compiled and run, is
    /// in <c>GeneratedTestsActAsAdministratorTests</c>.
    /// </summary>
    [TestMethod]
    public void Validator_tests_act_as_the_test_supports_role()
    {
        var source = TestProjectTemplates.ValidatorTests("Shop", "Customer", "Name");

        Assert.IsGreaterThanOrEqualTo(
            2, source.Split("TestSupport.ActingRole").Length - 1,
            "the populated and the blank record should both be validated as the acting role");
        Assert.DoesNotContain("UserRole", source, "one way of saying who acts, not two");
    }
}

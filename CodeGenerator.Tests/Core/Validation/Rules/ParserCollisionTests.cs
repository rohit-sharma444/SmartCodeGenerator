using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;

namespace CodeGenerator.Tests.Core.Validation.Rules;

/// <summary>
/// The two parsers that match more than they should. Both are load-bearing for the wider
/// catalog: the catch-all would turn every unmatched conjoined sentence into a bogus
/// allowed-values list, and the comparison rule would claim "at least 18" as a comparison
/// against a property called 18.
/// </summary>
[TestClass]
public class ParserCollisionTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Employee" };

    [TestMethod]
    public void Conjoined_bounds_are_not_an_allowed_values_list()
    {
        var parsed = new AllowedValuesRuleParser()
            .TryParse(Ctx("Age must be greater than 0 and less than 100"), out _);

        Assert.IsFalse(parsed, "'and' joins two rules, not two allowed values.");
    }

    [TestMethod]
    public void Comma_list_is_still_an_allowed_values_list()
    {
        Assert.IsTrue(new AllowedValuesRuleParser()
            .TryParse(Ctx("Status must be Draft, Sent, Paid"), out var rule));
        Assert.AreEqual("Status must be one of: Draft, Sent, Paid", rule!.Describe());
    }

    [TestMethod]
    public void Or_list_is_still_an_allowed_values_list()
    {
        Assert.IsTrue(new AllowedValuesRuleParser()
            .TryParse(Ctx("Status must be Draft or Paid"), out var rule));
        Assert.AreEqual("Status must be one of: Draft, Paid", rule!.Describe());
    }

    [TestMethod]
    public void A_numeric_operand_is_not_a_property_comparison()
    {
        var parsed = new ComparisonRuleParser().TryParse(Ctx("Age must be at least 18"), out _);

        Assert.IsFalse(parsed, "18 is a number, not a property to compare against.");
    }

    [TestMethod]
    public void A_named_operand_is_still_a_property_comparison()
    {
        Assert.IsTrue(new ComparisonRuleParser()
            .TryParse(Ctx("EndDate must be after StartDate"), out var rule));
        Assert.AreEqual("EndDate must be after StartDate", rule!.Describe());
    }
}

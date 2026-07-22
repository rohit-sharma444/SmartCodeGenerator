using System.Text;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class ComparisonRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Booking" };

    private static string Emit(IValidationRule rule, params string[] props)
    {
        var sb = new StringBuilder();
        var defs = props.Select(p => new PropertyDefinition { Name = p, Type = "System.DateTime" }).ToList();
        rule.Emit(new ValidationEmitContext(sb, defs));
        return sb.ToString();
    }

    [TestMethod]
    public void Parses_after_as_a_business_rule()
    {
        Assert.IsTrue(new ComparisonRuleParser().TryParse(Ctx("EndDate must be after StartDate"), out var r));
        Assert.AreEqual(ValidationCategory.Business, r!.Category);
        Assert.AreEqual("EndDate must be after StartDate", r.Describe());
    }

    [TestMethod]
    public void Emits_a_compareto_check_when_both_properties_exist()
    {
        new ComparisonRuleParser().TryParse(Ctx("EndDate must be after StartDate"), out var r);
        var code = Emit(r!, "EndDate", "StartDate");
        StringAssert.Contains(code, "this.EndDate");
        StringAssert.Contains(code, "this.StartDate");
        StringAssert.Contains(code, "CompareTo");
        StringAssert.Contains(code, "<= 0"); // "after" is violated when left <= right
    }

    [TestMethod]
    public void Skips_with_a_comment_when_a_property_is_unknown()
    {
        new ComparisonRuleParser().TryParse(Ctx("EndDate must be after StartDate"), out var r);
        var code = Emit(r!, "EndDate"); // StartDate missing
        StringAssert.Contains(code, "// business rule skipped");
    }

    [TestMethod]
    public void Not_exceed_maps_to_greater_than_violation()
    {
        new ComparisonRuleParser().TryParse(Ctx("Discount must not exceed Price"), out var r);
        var code = Emit(r!, "Discount", "Price");
        StringAssert.Contains(code, "> 0"); // "not exceed" is violated when left > right
    }

    [TestMethod]
    public void Describe_echoes_the_original_fragment_for_not_exceed()
    {
        new ComparisonRuleParser().TryParse(Ctx("Discount must not exceed Price"), out var r);
        // Must read naturally as written, not "Discount must be not exceed Price".
        Assert.AreEqual("Discount must not exceed Price", r!.Describe());
    }
}

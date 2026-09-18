using System.Text;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class PercentageRuleTests
{
    private static string Emit(IValidationRule rule, params string[] props)
    {
        var sb = new StringBuilder();
        var defs = props.Select(p => new PropertyDefinition { Name = p, Type = "decimal" }).ToList();
        rule.Emit(new ValidationEmitContext(sb, defs));
        return sb.ToString();
    }

    [TestMethod]
    public void Refuses_when_total_does_not_match_net_times_one_plus_rate()
    {
        var rule = new PercentageRule("Total", "Net", "Rate");

        var code = Emit(rule, "Total", "Net", "Rate");

        Assert.AreEqual(ValidationCategory.Business, rule.Category);
        CollectionAssert.AreEqual(new[] { "Total", "Net", "Rate" }, rule.Properties.ToArray());
        StringAssert.Contains(code, "this.Total");
        StringAssert.Contains(code, "this.Net");
        StringAssert.Contains(code, "this.Rate");
        // The actual percentage/tolerance check from PercentageRule.Emit: Total == Net * (1 + Rate),
        // within a 0.0001m tolerance. This fragment cannot appear unless Emit wrote the real check.
        StringAssert.Contains(code, "System.Math.Abs(__t - (__n * (1 + __r)))");
        StringAssert.Contains(code, "0.0001m");
        StringAssert.Contains(code, "errors.Add");
        Assert.DoesNotContain("business rule skipped", code,
            "Emit should write the real calculation check here, not the skipped-comment path.");
    }

    [TestMethod]
    public void Skips_with_a_comment_when_a_named_property_is_missing()
    {
        var rule = new PercentageRule("Total", "Net", "Rate");

        var code = Emit(rule, "Total"); // Net and Rate missing

        StringAssert.Contains(code, "business rule skipped");
    }
}

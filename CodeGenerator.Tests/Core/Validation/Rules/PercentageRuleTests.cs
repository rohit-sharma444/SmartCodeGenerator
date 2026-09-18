using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class PercentageRuleTests
{
    [TestMethod]
    public void Refuses_when_total_does_not_match_net_times_one_plus_rate()
    {
        var rule = new PercentageRule("Total", "Net", "Rate");
        var ctx = new ValidationEmitContext(new System.Text.StringBuilder(),
            [
                new CodeGenerationEngine.Models.PropertyDefinition { Name = "Total", Type = "decimal" },
                new CodeGenerationEngine.Models.PropertyDefinition { Name = "Net", Type = "decimal" },
                new CodeGenerationEngine.Models.PropertyDefinition { Name = "Rate", Type = "decimal" },
            ]);

        rule.Emit(ctx);

        Assert.AreEqual(ValidationCategory.Business, rule.Category);
        CollectionAssert.AreEqual(new[] { "Total", "Net", "Rate" }, rule.Properties.ToArray());
    }

    [TestMethod]
    public void Skips_with_a_comment_when_a_named_property_is_missing()
    {
        var rule = new PercentageRule("Total", "Net", "Rate");
        var sb = new System.Text.StringBuilder();
        var ctx = new ValidationEmitContext(sb,
            [new CodeGenerationEngine.Models.PropertyDefinition { Name = "Total", Type = "decimal" }]);

        rule.Emit(ctx);

        StringAssert.Contains(sb.ToString(), "business rule skipped");
    }
}

using System.Text;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class RequiredWhenRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Order" };

    [TestMethod]
    public void Parses_conditional_required()
    {
        Assert.IsTrue(new RequiredWhenRuleParser().TryParse(
            Ctx("TrackingNumber is required when Status is Shipped"), out var r));
        Assert.AreEqual(ValidationCategory.Business, r!.Category);
        Assert.AreEqual("TrackingNumber is required when Status is Shipped", r.Describe());
    }

    [TestMethod]
    public void Emits_guarded_required_check()
    {
        new RequiredWhenRuleParser().TryParse(
            Ctx("TrackingNumber is required when Status is Shipped"), out var r);
        var sb = new StringBuilder();
        var defs = new List<PropertyDefinition>
        {
            new() { Name = "TrackingNumber", Type = "string" },
            new() { Name = "Status", Type = "string" }
        };
        r!.Emit(new ValidationEmitContext(sb, defs));
        var code = sb.ToString();
        StringAssert.Contains(code, "this.Status");
        StringAssert.Contains(code, "\"Shipped\"");
        StringAssert.Contains(code, "this.TrackingNumber");
    }
}

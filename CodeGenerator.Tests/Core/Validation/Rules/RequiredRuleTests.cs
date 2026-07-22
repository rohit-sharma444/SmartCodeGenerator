using System.Text;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class RequiredRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Employee" };

    [TestMethod]
    public void Parses_is_required_phrasing()
    {
        var parser = new RequiredRuleParser();
        Assert.IsTrue(parser.TryParse(Ctx("Name is required"), out var rule));
        Assert.AreEqual(ValidationCategory.Data, rule!.Category);
        Assert.AreEqual("Name is required", rule.Describe());
    }

    [TestMethod]
    public void Does_not_parse_unrelated_text()
    {
        var parser = new RequiredRuleParser();
        Assert.IsFalse(parser.TryParse(Ctx("Name must match foo"), out _));
    }

    [TestMethod]
    public void Emits_a_required_check()
    {
        var parser = new RequiredRuleParser();
        parser.TryParse(Ctx("Name is required"), out var rule);
        var sb = new StringBuilder();
        rule!.Emit(new ValidationEmitContext(sb, new List<PropertyDefinition>()));
        var code = sb.ToString();
        StringAssert.Contains(code, "this.Name");
        StringAssert.Contains(code, "Name is required.");
    }

    [TestMethod]
    public void Is_discovered_by_the_default_catalog()
    {
        Assert.IsTrue(ValidationRuleCatalog.Default.Parsers.Any(p => p is RequiredRuleParser));
    }
}

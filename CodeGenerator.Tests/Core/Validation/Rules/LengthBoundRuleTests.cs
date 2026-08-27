using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class LengthBoundRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Employee" };

    private static string Emit(IValidationRule rule)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = "Name", Type = "string" }]));
        return sb.ToString();
    }

    private static void AssertCompiles(string body)
    {
        var source =
            "namespace Acme { public class Employee { public string Name { get; set; } = string.Empty;" +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Name must be at most 50 characters")]
    [DataRow("Name must be no longer than 50 characters")]
    [DataRow("Name must not exceed 50 characters")]
    public void Parses_the_maximum_length_phrasings(string sentence)
    {
        Assert.IsTrue(new MaxLengthRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual("Name must be at most 50 characters", rule!.Describe());
    }

    [TestMethod]
    public void Parses_the_minimum_length_phrasing()
    {
        Assert.IsTrue(new MinLengthRuleParser()
            .TryParse(Ctx("Name must be at least 2 characters"), out var rule));
        Assert.AreEqual("Name must be at least 2 characters", rule!.Describe());
    }

    [TestMethod]
    public void Parses_the_exact_length_phrasing()
    {
        Assert.IsTrue(new ExactLengthRuleParser()
            .TryParse(Ctx("Name must be exactly 8 characters"), out var rule));
        Assert.AreEqual("Name must be exactly 8 characters", rule!.Describe());
    }

    /// <summary>The catalog, not the parser: this is the ordering that keeps a character
    /// bound away from the numeric bound parsers added in Task 3.</summary>
    [TestMethod]
    public void The_catalog_routes_a_character_bound_to_the_length_rule()
    {
        var ctx = Ctx("Name must be at most 50 characters");
        var rules = ValidationRuleCatalog.Default.Parse(
            ctx.Fragment, ValidationCategory.Data, ctx);

        Assert.HasCount(1, rules);
        Assert.AreEqual("Name must be at most 50 characters", rules[0].Describe());
    }

    [TestMethod]
    public void Emitted_maximum_length_check_compiles()
    {
        new MaxLengthRuleParser().TryParse(Ctx("Name must be at most 50 characters"), out var rule);
        var code = Emit(rule!);

        StringAssert.Contains(code, "__s.Length > 50");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Emitted_exact_length_check_compiles()
    {
        new ExactLengthRuleParser().TryParse(Ctx("Name must be exactly 8 characters"), out var rule);
        var code = Emit(rule!);

        StringAssert.Contains(code, "__s.Length != 8");
        AssertCompiles(code);
    }
}

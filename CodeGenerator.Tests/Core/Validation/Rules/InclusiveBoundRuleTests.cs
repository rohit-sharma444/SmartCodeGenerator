using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class InclusiveBoundRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Employee" };

    private static string Emit(IValidationRule rule, string property, string type)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = property, Type = type }]));
        return sb.ToString();
    }

    /// <summary>Compiles the emitted body inside a real class, which is the only thing that
    /// proves the C# is C#. A substring assertion cannot.</summary>
    private static void AssertCompiles(string body, string propertyDeclaration)
    {
        var source =
            "namespace Acme { public class Employee { " + propertyDeclaration +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Age must be at least 18")]
    [DataRow("Age must be no less than 18")]
    [DataRow("Age must be greater than or equal to 18")]
    public void Parses_the_at_least_phrasings(string sentence)
    {
        Assert.IsTrue(new MinValueRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual("Age must be at least 18", rule!.Describe());
        Assert.AreEqual(ValidationCategory.Data, rule.Category);
        CollectionAssert.AreEqual(new[] { "Age" }, rule.Properties.ToArray());
    }

    [TestMethod]
    [DataRow("Salary must be at most 100000")]
    [DataRow("Salary must be no more than 100000")]
    [DataRow("Salary must not exceed 100000")]
    [DataRow("Salary must be less than or equal to 100000")]
    public void Parses_the_at_most_phrasings(string sentence)
    {
        Assert.IsTrue(new MaxValueRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual("Salary must be at most 100000", rule!.Describe());
    }

    [TestMethod]
    public void A_character_bound_is_not_a_numeric_bound()
    {
        Assert.IsFalse(new MaxValueRuleParser()
            .TryParse(Ctx("Name must be at most 50 characters"), out _));
    }

    [TestMethod]
    public void Emitted_min_bound_compiles_and_checks_the_lower_edge()
    {
        new MinValueRuleParser().TryParse(Ctx("Age must be at least 18"), out var rule);
        var code = Emit(rule!, "Age", "int");

        StringAssert.Contains(code, "this.Age");
        StringAssert.Contains(code, "__n < 18m");
        AssertCompiles(code, "public int Age { get; set; }");
    }

    [TestMethod]
    public void Emitted_max_bound_compiles_and_checks_the_upper_edge()
    {
        new MaxValueRuleParser().TryParse(Ctx("Salary must be at most 100000"), out var rule);
        var code = Emit(rule!, "Salary", "decimal");

        StringAssert.Contains(code, "__n > 100000m");
        AssertCompiles(code, "public decimal Salary { get; set; }");
    }
}

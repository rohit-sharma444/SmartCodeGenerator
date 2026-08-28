using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class NumberShapeRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Order" };

    private static string Emit(IValidationRule rule, string property, string type)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = property, Type = type }]));
        return sb.ToString();
    }

    private static void AssertCompiles(string body, string propertyDeclaration)
    {
        var source =
            "namespace Acme { public class Order { " + propertyDeclaration +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Price must be positive", "Price must be positive")]
    [DataRow("Balance must be negative", "Balance must be negative")]
    [DataRow("Quantity must not be negative", "Quantity must not be negative")]
    [DataRow("Quantity must be zero or greater", "Quantity must not be negative")]
    [DataRow("Total must not be zero", "Total must not be zero")]
    public void Parses_the_sign_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new SignRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    [DataRow("Quantity must be a whole number", "Quantity must be a whole number")]
    [DataRow("Quantity must be an integer", "Quantity must be a whole number")]
    [DataRow("Amount must be numeric", "Amount must be numeric")]
    [DataRow("Amount must be a number", "Amount must be numeric")]
    public void Parses_the_number_form_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new NumberFormRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    public void Emitted_positive_check_compiles()
    {
        new SignRuleParser().TryParse(Ctx("Price must be positive"), out var rule);
        var code = Emit(rule!, "Price", "decimal");

        StringAssert.Contains(code, "__n <= 0m");
        AssertCompiles(code, "public decimal Price { get; set; }");
    }

    [TestMethod]
    public void Emitted_whole_number_check_compiles()
    {
        new NumberFormRuleParser().TryParse(Ctx("Quantity must be a whole number"), out var rule);
        var code = Emit(rule!, "Quantity", "decimal");

        StringAssert.Contains(code, "decimal.Truncate");
        AssertCompiles(code, "public decimal Quantity { get; set; }");
    }

    [TestMethod]
    public void Emitted_numeric_check_compiles_against_a_string_property()
    {
        new NumberFormRuleParser().TryParse(Ctx("Amount must be numeric"), out var rule);
        var code = Emit(rule!, "Amount", "string");

        AssertCompiles(code, "public string Amount { get; set; } = string.Empty;");
    }
}

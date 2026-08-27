using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class TextShapeRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Product" };

    private static string Emit(IValidationRule rule)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = "Sku", Type = "string" }]));
        return sb.ToString();
    }

    private static void AssertCompiles(string body)
    {
        var source =
            "namespace Acme { public class Product { public string Sku { get; set; } = string.Empty;" +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Sku must start with \"AB\"", "Sku must start with AB")]
    [DataRow("Sku must start with AB", "Sku must start with AB")]
    [DataRow("Sku must end with \"Z\"", "Sku must end with Z")]
    public void Parses_the_affix_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new AffixRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    [DataRow("Sku must contain \"-\"", "Sku must contain -")]
    [DataRow("Sku must not contain \" \"", "Sku must not contain  ")]
    public void Parses_the_substring_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new ContainsRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    [DataRow("Sku must be uppercase")]
    [DataRow("Sku must be lowercase")]
    public void Parses_the_case_phrasings(string sentence)
    {
        Assert.IsTrue(new CaseRuleParser().TryParse(Ctx(sentence), out _));
    }

    [TestMethod]
    [DataRow("Sku must be digits only", "Sku must be digits only")]
    [DataRow("Sku must be letters only", "Sku must be letters only")]
    [DataRow("Sku must be alphanumeric", "Sku must be alphanumeric")]
    public void Parses_the_character_class_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new CharacterClassRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    /// <summary>A quote in the operand must not be able to end the emitted string literal.</summary>
    [TestMethod]
    public void An_operand_containing_a_quote_still_compiles()
    {
        new AffixRuleParser().TryParse(Ctx("Sku must start with \"A\"\"B\""), out var rule);
        AssertCompiles(Emit(rule!));
    }

    [TestMethod]
    public void Emitted_starts_with_check_compiles()
    {
        new AffixRuleParser().TryParse(Ctx("Sku must start with \"AB\""), out var rule);
        var code = Emit(rule!);

        StringAssert.Contains(code, "StartsWith");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Emitted_digits_only_check_compiles()
    {
        new CharacterClassRuleParser().TryParse(Ctx("Sku must be digits only"), out var rule);
        var code = Emit(rule!);

        StringAssert.Contains(code, "IsMatch");
        AssertCompiles(code);
    }
}

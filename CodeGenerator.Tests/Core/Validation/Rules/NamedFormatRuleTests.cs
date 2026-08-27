using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class NamedFormatRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Contact" };

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
            "namespace Acme { public class Contact { " + propertyDeclaration +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Phone must be a valid phone number")]
    [DataRow("Website must be a valid URL")]
    [DataRow("Postcode must be a valid postcode")]
    [DataRow("Reference must be a valid GUID")]
    [DataRow("Host must be a valid IP address")]
    public void Parses_the_named_formats(string sentence)
    {
        Assert.IsTrue(new NamedFormatRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(ValidationCategory.Data, rule!.Category);
        StringAssert.Contains(rule.Describe(), "must match");
    }

    [TestMethod]
    public void Every_named_format_emits_a_pattern_that_is_a_valid_regex()
    {
        foreach (var example in new NamedFormatRuleParser().Examples)
        {
            new NamedFormatRuleParser().TryParse(Ctx(example), out var rule);

            var at = rule!.Describe().IndexOf("must match ", System.StringComparison.Ordinal);
            var pattern = rule.Describe()[(at + "must match ".Length)..];
            _ = new System.Text.RegularExpressions.Regex(pattern); // throws if malformed
        }
    }

    [TestMethod]
    public void Emitted_phone_check_compiles()
    {
        new NamedFormatRuleParser().TryParse(Ctx("Phone must be a valid phone number"), out var rule);
        var code = Emit(rule!, "Phone", "string");

        AssertCompiles(code, "public string Phone { get; set; } = string.Empty;");
    }

    [TestMethod]
    public void Parses_and_emits_a_valid_date_check()
    {
        Assert.IsTrue(new ValidDateRuleParser()
            .TryParse(Ctx("Born must be a valid date"), out var rule));
        Assert.AreEqual("Born must be a valid date", rule!.Describe());

        var code = Emit(rule, "Born", "string");
        StringAssert.Contains(code, "DateTime.TryParse");
        StringAssert.Contains(code, "InvariantCulture");
        AssertCompiles(code, "public string Born { get; set; } = string.Empty;");
    }
}

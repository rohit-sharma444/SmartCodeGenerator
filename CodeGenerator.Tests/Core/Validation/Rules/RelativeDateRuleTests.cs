using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class RelativeDateRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Person" };

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
            "namespace Acme { public class Person { " + propertyDeclaration +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("DateOfBirth must be in the past", "DateOfBirth must be in the past")]
    [DataRow("ExpiryDate must be in the future", "ExpiryDate must be in the future")]
    [DataRow("StartDate must not be in the past", "StartDate must not be in the past")]
    [DataRow("StartDate must not be in the future", "StartDate must not be in the future")]
    public void Parses_the_relative_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new RelativeDateRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    [DataRow("DateOfBirth must be at least 18 years ago", "DateOfBirth must be at least 18 years ago")]
    [DataRow("Modified must be within the last 30 days", "Modified must be within the last 30 days")]
    public void Parses_the_distance_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new DateDistanceRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    public void Emitted_check_compiles_against_a_DateTime_property()
    {
        new RelativeDateRuleParser().TryParse(Ctx("DateOfBirth must be in the past"), out var rule);
        var code = Emit(rule!, "DateOfBirth", "System.DateTime");

        AssertCompiles(code, "public System.DateTime DateOfBirth { get; set; }");
    }

    [TestMethod]
    public void Emitted_check_compiles_against_a_nullable_DateTime_property()
    {
        new RelativeDateRuleParser().TryParse(Ctx("ExpiryDate must be in the future"), out var rule);
        var code = Emit(rule!, "ExpiryDate", "System.DateTime?");

        AssertCompiles(code, "public System.DateTime? ExpiryDate { get; set; }");
    }

    [TestMethod]
    public void Emitted_check_compiles_against_a_string_property_and_parses_invariantly()
    {
        new RelativeDateRuleParser().TryParse(Ctx("DateOfBirth must be in the past"), out var rule);
        var code = Emit(rule!, "DateOfBirth", "string");

        StringAssert.Contains(code, "InvariantCulture");
        StringAssert.Contains(code, "DateTimeStyles.None");
        AssertCompiles(code, "public string DateOfBirth { get; set; } = string.Empty;");
    }

    [TestMethod]
    public void Emitted_years_ago_check_compiles()
    {
        new DateDistanceRuleParser()
            .TryParse(Ctx("DateOfBirth must be at least 18 years ago"), out var rule);
        var code = Emit(rule!, "DateOfBirth", "System.DateTime");

        StringAssert.Contains(code, "AddYears(-18)");
        AssertCompiles(code, "public System.DateTime DateOfBirth { get; set; }");
    }

    [TestMethod]
    [DataRow("Age must be at least 18")]
    [DataRow("DateOfBirth must be at least 18")]
    public void Plain_numeric_at_least_phrasings_do_not_reach_the_date_parsers(string sentence)
    {
        Assert.IsFalse(new RelativeDateRuleParser().TryParse(Ctx(sentence), out _));
        Assert.IsFalse(new DateDistanceRuleParser().TryParse(Ctx(sentence), out _));
    }

    [TestMethod]
    public void Years_ago_phrasing_does_not_reach_MinValueRuleParser()
    {
        Assert.IsFalse(
            new MinValueRuleParser().TryParse(Ctx("DateOfBirth must be at least 18 years ago"), out _));
    }
}

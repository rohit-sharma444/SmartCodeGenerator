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

    /// <summary>
    /// Parses <paramref name="sentence"/> through the catalog, emits the check against a
    /// single <c>System.DateTime DateOfBirth</c> property, compiles it, loads the resulting
    /// assembly, sets the property to <paramref name="value"/>, and runs <c>Validate()</c> for
    /// real. Executes the emitted decision rather than reading the emitted source — the
    /// violation expressions read plausible but two of them silently overlapped, and no test
    /// that only inspected or compiled the source could have caught that.
    /// </summary>
    private static List<string> RunValidate(string sentence, System.DateTime value)
    {
        var ctx = new ValidationParseContext { Fragment = sentence, ClassName = "Person" };
        var rules = ValidationRuleCatalog.Default.Parse(sentence, ValidationCategory.Data, ctx);
        Assert.HasCount(1, rules, $"expected exactly one rule for: {sentence}");

        var code = Emit(rules[0], "DateOfBirth", "System.DateTime");
        var source =
            "namespace Acme { public class Person { " +
            "public System.DateTime DateOfBirth { get; set; } " +
            "public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            code + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));

        var assembly = System.Reflection.Assembly.Load(result.AssemblyBytes!);
        var type = assembly.GetType("Acme.Person")!;
        var instance = System.Activator.CreateInstance(type)!;
        type.GetProperty("DateOfBirth")!.SetValue(instance, value);

        return (List<string>)type.GetMethod("Validate")!.Invoke(instance, null)!;
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

    /// <summary>
    /// "In the past" and "not in the past" must partition time: a value earlier today has to satisfy
    /// exactly one of them. Asserted by running the emitted code, not by reading it — this family had
    /// no behavioural coverage at all, which is how the two rules came to overlap.
    /// </summary>
    [TestMethod]
    public void Past_and_not_past_partition_a_value_earlier_today()
    {
        var earlierToday = System.DateTime.Today.AddHours(1);

        var inPast = RunValidate("DateOfBirth must be in the past", earlierToday);
        var notInPast = RunValidate("DateOfBirth must not be in the past", earlierToday);

        Assert.AreNotEqual(inPast.Count == 0, notInPast.Count == 0,
            "a value earlier today satisfied both 'in the past' and 'not in the past'");
    }

    /// <summary>Future/NotFuture must likewise partition time at the tomorrow boundary.</summary>
    [TestMethod]
    public void Future_and_not_future_partition_tomorrow()
    {
        var tomorrow = System.DateTime.Today.AddDays(1).AddHours(1);

        var inFuture = RunValidate("DateOfBirth must be in the future", tomorrow);
        var notInFuture = RunValidate("DateOfBirth must not be in the future", tomorrow);

        Assert.AreNotEqual(inFuture.Count == 0, notInFuture.Count == 0,
            "tomorrow satisfied both 'in the future' and 'not in the future'");
    }

    [TestMethod]
    public void Today_is_a_future_violation_but_not_a_not_future_violation()
    {
        var today = System.DateTime.Today;

        var inFuture = RunValidate("DateOfBirth must be in the future", today);
        var notInFuture = RunValidate("DateOfBirth must not be in the future", today);

        Assert.HasCount(1, inFuture, "today must violate 'must be in the future'");
        Assert.HasCount(0, notInFuture, "today must not violate 'must not be in the future'");
    }

    [TestMethod]
    public void Yesterday_is_not_a_past_violation()
    {
        var yesterday = System.DateTime.Today.AddDays(-1);

        var inPast = RunValidate("DateOfBirth must be in the past", yesterday);

        Assert.HasCount(0, inPast, "yesterday must not violate 'must be in the past'");
    }
}

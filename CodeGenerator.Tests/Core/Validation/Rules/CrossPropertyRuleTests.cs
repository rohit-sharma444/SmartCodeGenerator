using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class CrossPropertyRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Order" };

    private static string Emit(IValidationRule rule, params (string Name, string Type)[] props)
    {
        var sb = new StringBuilder();
        var defs = props.Select(p => new PropertyDefinition { Name = p.Name, Type = p.Type }).ToList();
        rule.Emit(new ValidationEmitContext(sb, defs));
        return sb.ToString();
    }

    private static void AssertCompiles(string body, string declarations)
    {
        var source =
            "namespace Acme { public class Order { " + declarations +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void Parses_either_or()
    {
        Assert.IsTrue(new EitherOrRuleParser()
            .TryParse(Ctx("Either Email or Phone must be provided"), out var rule));
        Assert.AreEqual("Either Email or Phone must be provided", rule!.Describe());
        Assert.AreEqual(ValidationCategory.Business, rule.Category);
        CollectionAssert.AreEquivalent(new[] { "Email", "Phone" }, rule.Properties.ToArray());
    }

    [TestMethod]
    [DataRow("Total must equal Quantity times Price")]
    [DataRow("Total must equal Quantity plus Price")]
    [DataRow("Total must equal Quantity minus Price")]
    [DataRow("Total must equal Quantity divided by Price")]
    public void Parses_the_arithmetic_phrasings(string sentence)
    {
        Assert.IsTrue(new ArithmeticRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(sentence, rule!.Describe());
        CollectionAssert.AreEquivalent(
            new[] { "Total", "Quantity", "Price" }, rule.Properties.ToArray());
    }

    [TestMethod]
    [DataRow("BillingPostcode must be the same as ShippingPostcode", true)]
    [DataRow("BillingPostcode must be different from ShippingPostcode", false)]
    [DataRow("BillingPostcode must not be the same as ShippingPostcode", false)]
    public void Parses_the_sameness_phrasings(string sentence, bool same)
    {
        Assert.IsTrue(new SameAsRuleParser().TryParse(Ctx(sentence), out var rule));
        StringAssert.Contains(rule!.Describe(), same ? "the same as" : "different from");
    }

    [TestMethod]
    public void Emitted_either_or_check_compiles()
    {
        new EitherOrRuleParser().TryParse(Ctx("Either Email or Phone must be provided"), out var rule);
        var code = Emit(rule!, ("Email", "string"), ("Phone", "string"));

        AssertCompiles(code,
            "public string Email { get; set; } = string.Empty;" +
            "public string Phone { get; set; } = string.Empty;");
    }

    [TestMethod]
    public void Emitted_arithmetic_check_compiles()
    {
        new ArithmeticRuleParser().TryParse(Ctx("Total must equal Quantity times Price"), out var rule);
        var code = Emit(rule!, ("Total", "decimal"), ("Quantity", "int"), ("Price", "decimal"));

        AssertCompiles(code,
            "public decimal Total { get; set; } public int Quantity { get; set; }" +
            "public decimal Price { get; set; }");
    }

    [TestMethod]
    public void An_unknown_operand_is_skipped_with_a_comment_rather_than_emitted()
    {
        new ArithmeticRuleParser().TryParse(Ctx("Total must equal Quantity times Price"), out var rule);
        var code = Emit(rule!, ("Total", "decimal"), ("Quantity", "int")); // Price missing

        StringAssert.Contains(code, "// business rule skipped");
    }

    /// <summary>
    /// Parses <paramref name="sentence"/> through the catalog, emits the check against the given
    /// properties, compiles it, loads the resulting assembly, sets the property values, and runs
    /// <c>Validate()</c> for real. Executes the emitted decision rather than reading the emitted
    /// source — a family of date rules once passed every parse/Describe/compile test while two of
    /// them accepted the same value, because nothing ever ran them.
    /// </summary>
    private static List<string> RunValidate(
        string sentence,
        (string Name, string Type)[] props,
        (string Name, object Value)[] values)
    {
        var ctx = Ctx(sentence);
        var rules = ValidationRuleCatalog.Default.Parse(sentence, ValidationCategory.Business, ctx);
        Assert.HasCount(1, rules, $"expected exactly one rule for: {sentence}");

        var code = Emit(rules[0], props);
        var declarations = string.Join(" ", props.Select(p =>
            $"public {p.Type} {p.Name} {{ get; set; }}" + (p.Type == "string" ? " = string.Empty;" : "")));

        var source =
            "namespace Acme { public class Order { " + declarations +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            code + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));

        var assembly = System.Reflection.Assembly.Load(result.AssemblyBytes!);
        var type = assembly.GetType("Acme.Order")!;
        var instance = System.Activator.CreateInstance(type)!;
        foreach (var (name, value) in values)
            type.GetProperty(name)!.SetValue(instance, value);

        return (List<string>)type.GetMethod("Validate")!.Invoke(instance, null)!;
    }

    [TestMethod]
    public void EitherOr_both_blank_produces_an_error()
    {
        var errors = RunValidate(
            "Either Email or Phone must be provided",
            [("Email", "string"), ("Phone", "string")],
            [("Email", ""), ("Phone", "")]);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void EitherOr_one_filled_produces_no_error()
    {
        var errors = RunValidate(
            "Either Email or Phone must be provided",
            [("Email", "string"), ("Phone", "string")],
            [("Email", "a@b.com"), ("Phone", "")]);

        Assert.HasCount(0, errors);
    }

    [TestMethod]
    public void Arithmetic_satisfied_produces_no_error()
    {
        var errors = RunValidate(
            "Total must equal Quantity times Price",
            [("Total", "decimal"), ("Quantity", "int"), ("Price", "decimal")],
            [("Total", 6m), ("Quantity", 2), ("Price", 3m)]);

        Assert.HasCount(0, errors);
    }

    [TestMethod]
    public void Arithmetic_violated_produces_an_error()
    {
        var errors = RunValidate(
            "Total must equal Quantity times Price",
            [("Total", "decimal"), ("Quantity", "int"), ("Price", "decimal")],
            [("Total", 7m), ("Quantity", 2), ("Price", 3m)]);

        Assert.HasCount(1, errors);
    }

    /// <summary>
    /// 1 / 3 has no exact decimal representation. A target rounded to 5 places would fail an
    /// exact-equality check against the true quotient, but is well within the rule's tolerance —
    /// which is the whole point of comparing with a tolerance rather than for exact equality.
    /// </summary>
    [TestMethod]
    public void Arithmetic_tolerates_a_repeating_decimal_quotient()
    {
        var errors = RunValidate(
            "Total must equal Quantity divided by Price",
            [("Total", "decimal"), ("Quantity", "decimal"), ("Price", "decimal")],
            [("Total", 0.33333m), ("Quantity", 1m), ("Price", 3m)]);

        Assert.HasCount(0, errors);
    }

    /// <summary>
    /// The emitted guard (<c>__r != 0m</c>) means a zero divisor never reaches the comparison at
    /// all, so the check is silently skipped rather than raising an error — even though Total (5)
    /// plainly does not equal Quantity/0.
    /// </summary>
    [TestMethod]
    public void Arithmetic_division_by_zero_is_silently_skipped()
    {
        var errors = RunValidate(
            "Total must equal Quantity divided by Price",
            [("Total", "decimal"), ("Quantity", "decimal"), ("Price", "decimal")],
            [("Total", 5m), ("Quantity", 10m), ("Price", 0m)]);

        Assert.HasCount(0, errors);
    }

    [TestMethod]
    public void SameAs_equal_values_same_as_direction_produces_no_error()
    {
        var errors = RunValidate(
            "BillingPostcode must be the same as ShippingPostcode",
            [("BillingPostcode", "string"), ("ShippingPostcode", "string")],
            [("BillingPostcode", "AB1 2CD"), ("ShippingPostcode", "AB1 2CD")]);

        Assert.HasCount(0, errors);
    }

    [TestMethod]
    public void SameAs_unequal_values_same_as_direction_produces_an_error()
    {
        var errors = RunValidate(
            "BillingPostcode must be the same as ShippingPostcode",
            [("BillingPostcode", "string"), ("ShippingPostcode", "string")],
            [("BillingPostcode", "AB1 2CD"), ("ShippingPostcode", "XY9 8ZZ")]);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void SameAs_equal_values_different_from_direction_produces_an_error()
    {
        var errors = RunValidate(
            "BillingPostcode must be different from ShippingPostcode",
            [("BillingPostcode", "string"), ("ShippingPostcode", "string")],
            [("BillingPostcode", "AB1 2CD"), ("ShippingPostcode", "AB1 2CD")]);

        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void SameAs_unequal_values_different_from_direction_produces_no_error()
    {
        var errors = RunValidate(
            "BillingPostcode must be different from ShippingPostcode",
            [("BillingPostcode", "string"), ("ShippingPostcode", "string")],
            [("BillingPostcode", "AB1 2CD"), ("ShippingPostcode", "XY9 8ZZ")]);

        Assert.HasCount(0, errors);
    }
}

using System.Text;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class DataRulesTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "C" };

    private static string Emit(IValidationRule rule)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(sb, new List<PropertyDefinition>()));
        return sb.ToString();
    }

    [TestMethod]
    public void Length_range()
    {
        Assert.IsTrue(new LengthRangeRuleParser().TryParse(Ctx("Name length must be between 3 and 50"), out var r));
        StringAssert.Contains(Emit(r!), "Length < 3");
        StringAssert.Contains(Emit(r!), "Length > 50");
    }

    [TestMethod]
    public void Numeric_range()
    {
        Assert.IsTrue(new NumericRangeRuleParser().TryParse(Ctx("Age must be between 18 and 60"), out var r));
        var code = Emit(r!);
        StringAssert.Contains(code, "< 18m");
        StringAssert.Contains(code, "> 60m");
    }

    [TestMethod]
    public void Greater_than()
    {
        Assert.IsTrue(new GreaterThanRuleParser().TryParse(Ctx("Salary must be greater than 0"), out var r));
        StringAssert.Contains(Emit(r!), "<= 0m");
    }

    [TestMethod]
    public void Less_than()
    {
        Assert.IsTrue(new LessThanRuleParser().TryParse(Ctx("Salary must be less than 100000"), out var r));
        StringAssert.Contains(Emit(r!), ">= 100000m");
    }

    [TestMethod]
    public void Valid_email()
    {
        Assert.IsTrue(new EmailRuleParser().TryParse(Ctx("Email must be a valid email"), out var r));
        StringAssert.Contains(Emit(r!), "Regex.IsMatch");
    }

    [TestMethod]
    public void Match_pattern()
    {
        Assert.IsTrue(new MatchRuleParser().TryParse(Ctx("Phone must match ^\\d{10}$"), out var r));
        StringAssert.Contains(Emit(r!), "Regex.IsMatch");
    }

    [TestMethod]
    public void Allowed_values_needs_a_list()
    {
        Assert.IsTrue(new AllowedValuesRuleParser().TryParse(Ctx("Country must be India, USA, UK"), out var r));
        StringAssert.Contains(Emit(r!), "__allowed");
        // single value is not an allowed-values list
        Assert.IsFalse(new AllowedValuesRuleParser().TryParse(Ctx("Country must be India"), out _));
    }
}

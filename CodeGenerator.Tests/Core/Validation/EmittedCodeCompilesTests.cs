using System.Linq;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.Core.Validation;

/// <summary>End-to-end guard: rule-emitted C# must actually compile against the
/// declaring property's real CLR type, not just substring-match. Catches cases
/// like `this.Prop?.ToString()` on a non-nullable value-type property (CS0023),
/// which <c>DataRulesTests</c> cannot see because it emits against an empty
/// property list.</summary>
[TestClass]
public class EmittedCodeCompilesTests
{
    private static string ComposeSource(string propertyDeclaration, string emittedPartial) =>
        $"namespace Acme {{ public partial class Employee {{ {propertyDeclaration} }} }}" + emittedPartial;

    private static void AssertCompiles(string source)
    {
        var result = Compiler.Compile(source);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var message = string.Join("\n", errors.Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
        Assert.IsEmpty(errors, $"Compilation errors:\n{message}");
    }

    [TestMethod]
    public void Numeric_range_rule_compiles_against_a_non_nullable_int_property()
    {
        var ctx = new ValidationParseContext { Fragment = "Age must be between 18 and 60", ClassName = "Employee" };
        var rules = ValidationRuleCatalog.Default.Parse("Age must be between 18 and 60", ValidationCategory.Data, ctx);
        Assert.HasCount(1, rules);

        var props = new List<PropertyDefinition> { new() { Name = "Age", Type = "int" } };
        var emitted = ValidationEngine.EmitValidationPartial("Acme", "Employee", props, rules);

        AssertCompiles(ComposeSource("public int Age { get; set; }", emitted));
    }

    [TestMethod]
    public void Length_range_rule_compiles_against_a_string_property()
    {
        var ctx = new ValidationParseContext { Fragment = "Name length must be between 3 and 50", ClassName = "Employee" };
        var rules = ValidationRuleCatalog.Default.Parse("Name length must be between 3 and 50", ValidationCategory.Data, ctx);
        Assert.HasCount(1, rules);

        var props = new List<PropertyDefinition> { new() { Name = "Name", Type = "string" } };
        var emitted = ValidationEngine.EmitValidationPartial("Acme", "Employee", props, rules);

        AssertCompiles(ComposeSource("public string? Name { get; set; }", emitted));
    }

    [TestMethod]
    public void Comparison_business_rule_compiles_against_two_datetime_properties()
    {
        var ctx = new ValidationParseContext { Fragment = "EndDate must be after StartDate", ClassName = "Booking" };
        var rules = ValidationRuleCatalog.Default.Parse("EndDate must be after StartDate", ValidationCategory.Business, ctx);
        Assert.HasCount(1, rules);

        var props = new List<PropertyDefinition>
        {
            new() { Name = "StartDate", Type = "System.DateTime" },
            new() { Name = "EndDate", Type = "System.DateTime" },
        };
        var emitted = ValidationEngine.EmitValidationPartial("Acme", "Booking", props, rules);

        var declaringPartial =
            "namespace Acme { public partial class Booking { " +
            "public System.DateTime StartDate { get; set; } " +
            "public System.DateTime EndDate { get; set; } } }";

        AssertCompiles(declaringPartial + emitted);
    }

    [TestMethod]
    public void RequiredWhen_business_rule_compiles_against_two_string_properties()
    {
        var ctx = new ValidationParseContext { Fragment = "TrackingNumber is required when Status is Shipped", ClassName = "Order" };
        var rules = ValidationRuleCatalog.Default.Parse("TrackingNumber is required when Status is Shipped", ValidationCategory.Business, ctx);
        Assert.HasCount(1, rules);

        var props = new List<PropertyDefinition>
        {
            new() { Name = "Status", Type = "string" },
            new() { Name = "TrackingNumber", Type = "string" },
        };
        var emitted = ValidationEngine.EmitValidationPartial("Acme", "Order", props, rules);

        var declaringPartial =
            "namespace Acme { public partial class Order { " +
            "public string? Status { get; set; } " +
            "public string? TrackingNumber { get; set; } } }";

        AssertCompiles(declaringPartial + emitted);
    }

    [TestMethod]
    public void RequiredWhen_business_rule_compiles_when_the_condition_value_contains_a_backslash()
    {
        var fragment = @"TrackingNumber is required when Path is C:\logs";
        var ctx = new ValidationParseContext { Fragment = fragment, ClassName = "Order" };
        var rules = ValidationRuleCatalog.Default.Parse(fragment, ValidationCategory.Business, ctx);
        Assert.HasCount(1, rules);

        var props = new List<PropertyDefinition>
        {
            new() { Name = "Path", Type = "string" },
            new() { Name = "TrackingNumber", Type = "string" },
        };
        var emitted = ValidationEngine.EmitValidationPartial("Acme", "Order", props, rules);

        var declaringPartial =
            "namespace Acme { public partial class Order { " +
            "public string? Path { get; set; } " +
            "public string? TrackingNumber { get; set; } } }";

        AssertCompiles(declaringPartial + emitted);
    }

    [TestMethod]
    public void RequiredWhen_business_rule_compiles_when_the_condition_value_contains_an_embedded_quote()
    {
        var fragment = "TrackingNumber is required when Status is he said \"go\"";
        var ctx = new ValidationParseContext { Fragment = fragment, ClassName = "Order" };
        var rules = ValidationRuleCatalog.Default.Parse(fragment, ValidationCategory.Business, ctx);
        Assert.HasCount(1, rules);

        var props = new List<PropertyDefinition>
        {
            new() { Name = "Status", Type = "string" },
            new() { Name = "TrackingNumber", Type = "string" },
        };
        var emitted = ValidationEngine.EmitValidationPartial("Acme", "Order", props, rules);

        var declaringPartial =
            "namespace Acme { public partial class Order { " +
            "public string? Status { get; set; } " +
            "public string? TrackingNumber { get; set; } } }";

        AssertCompiles(declaringPartial + emitted);
    }
}

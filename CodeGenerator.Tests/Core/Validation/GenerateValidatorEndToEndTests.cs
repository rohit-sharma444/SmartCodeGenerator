using System.Linq;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.Core.Validation;

/// <summary>End-to-end guard for the `generate validator` command path: a mixed
/// data + business rule set must emit a single <c>Validate()</c> containing both
/// a "// Data validation" section and a "// Business rules" section, and the
/// combined output must actually compile (not just parse) against the declaring
/// class's real properties. This is the convergence point of the two independent
/// command paths (data rules vs. business rules) proven end to end.</summary>
[TestClass]
public class GenerateValidatorEndToEndTests
{
    [TestMethod]
    public void Mixed_rules_emit_a_single_Validate_with_both_sections_that_compiles()
    {
        var parseCtx = new ValidationParseContext { Fragment = "", ClassName = "Booking" };
        var catalog = ValidationRuleCatalog.Default;

        var rules = new List<IValidationRule>();
        rules.AddRange(catalog.Parse("StartDate is required", ValidationCategory.Data, parseCtx));
        rules.AddRange(catalog.Parse("EndDate must be after StartDate", ValidationCategory.Business, parseCtx));

        var props = new List<PropertyDefinition>
        {
            new() { Name = "StartDate", Type = "System.DateTime" },
            new() { Name = "EndDate", Type = "System.DateTime" }
        };

        var code = ValidationEngine.EmitValidationPartial("Acme", "Booking", props, rules);

        StringAssert.Contains(code, "// Data validation");
        StringAssert.Contains(code, "// Business rules");
        StringAssert.Contains(code, "StartDate is required.");
        StringAssert.Contains(code, "EndDate must be after StartDate.");

        // Genuine semantic end-to-end check: compile the combined data+business
        // Validate() against the declaring class's real properties, not just parse it.
        var declaringPartial =
            "namespace Acme { public partial class Booking { " +
            "public System.DateTime StartDate { get; set; } " +
            "public System.DateTime EndDate { get; set; } } }";

        var result = Compiler.Compile(declaringPartial + code);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var message = string.Join("\n", errors.Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
        Assert.IsEmpty(errors, $"Compilation errors:\n{message}");
    }
}

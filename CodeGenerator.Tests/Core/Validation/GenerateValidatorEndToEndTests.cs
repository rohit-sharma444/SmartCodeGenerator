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
// ValidationRegistry is a process-wide static and MSTestSettings parallelizes at method
// level, so a class that writes to it cannot run beside another that does.
[TestClass]
[DoNotParallelize]
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

    /// <summary>
    /// The pair that has never been compiled together. Every other test in this file supplies the
    /// declaring half itself, already marked partial - so the fact that `create class` does not
    /// mark it has been invisible.
    /// </summary>
    [TestMethod]
    public void The_class_and_its_generated_validator_compile_together()
    {
        var folder = Path.Combine(Path.GetTempPath(), "gv-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            var classFile = Path.Combine(folder, "Booking.cs");
            File.WriteAllText(classFile,
                "namespace Acme\r\n" +
                "{\r\n" +
                "    public class Booking\r\n" +          // NOT partial, as create class writes it
                "    {\r\n" +
                "        public int Nights { get; set; }\r\n" +
                "    }\r\n" +
                "}\r\n");

            ValidationRegistry.Clear();
            ProjectManager.CurrentProject = null;

            var ctx = new ValidationParseContext
            {
                Fragment = "Nights must be positive",
                ClassName = "Booking"
            };

            foreach (var rule in ValidationRuleCatalog.Default
                         .Parse(ctx.Fragment, ValidationCategory.Data, ctx))
                ValidationRegistry.Add("Booking", rule);

            var command = new ParsedCommand
            {
                Intent = IntentType.GenerateValidator,
                ClassName = "Booking"
            };

            Assert.IsTrue(EntityExtractor.GenerateValidator(command, folder));

            var validatorFile = Path.Combine(folder, "Booking.Validation.cs");
            Assert.IsTrue(File.Exists(validatorFile));

            var result = Compiler.CompileSources(
            [
                (classFile, File.ReadAllText(classFile)),
                (validatorFile, File.ReadAllText(validatorFile))
            ]);

            Assert.IsTrue(result.Success,
                string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
        }
        finally
        {
            ValidationRegistry.Clear();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}

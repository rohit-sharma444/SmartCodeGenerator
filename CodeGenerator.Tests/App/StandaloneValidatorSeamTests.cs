using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Task 11: proves — rather than assumes — that Task 1's widened <c>ValidationContext&lt;T&gt;</c>
/// seam and the standalone <c>generate validator</c> command are genuinely independent paths, and
/// that both still compile their real partial-class pair under this branch.
/// </summary>
/// <remarks>
/// This task's own brief claimed "the seam change in Task 1 breaks [the standalone] path." That is
/// false: <c>EntityExtractor.GenerateValidator</c> (CodeGenerationEngine/Core/EntityExtractor.cs:66)
/// calls <c>ValidationEngine.EmitValidationPartial</c> (CodeGenerationEngine/Core/ValidationEngine.cs:20-33),
/// which emits a plain <c>public partial class {className} { public List&lt;string&gt; Validate() }</c>
/// that names none of <c>IValidator&lt;T&gt;</c>, <c>ValidationContext</c>, <c>IEntityLookup</c>,
/// <c>ValidationPipeline</c> or <c>Permissions</c> — the seam Tasks 1-4 introduced into
/// <see cref="AppScaffolding.ValidatorTemplate"/> for the CODE GENERATOR's own emitted app-side
/// validators. The two tests below compile each path's real partial pair separately, so that claim
/// is demonstrated rather than taken on faith.
/// <para>
/// Also verified on disk: the generated APP-side validator (<see cref="ValidatorTemplate.Generated"/>)
/// is stateless since Task 4 — no constructor, no field. <c>ValidatorTemplate.cs:105</c> resolves it as
/// <c>new {name}Validator()</c> and the <c>Field</c> stage (<c>ValidatorTemplate.cs:221-227</c>) reads
/// <c>context.Lookup.Exists&lt;T&gt;(...)</c> rather than an injected <c>IEntityLookup</c> field. A test
/// asserting a <c>public CustomerValidator(IEntityLookup lookup)</c> constructor (as an earlier draft of
/// this task's brief proposed) would demand state Task 4 deliberately removed, so no such test exists
/// here — "can be given a lookup" is satisfied by the context carrying one, not by a constructor.
/// </para>
/// </remarks>
// ValidationRegistry is a process-wide static and MSTestSettings parallelizes at method level, so a
// class that writes to it (see GenerateValidatorEndToEndTests) cannot run beside another that does.
[TestClass]
[DoNotParallelize]
public class StandaloneValidatorSeamTests
{
    /// <summary>
    /// The APP path: <see cref="ValidatorTemplate.Generated"/> and <see cref="ValidatorTemplate.CustomStub"/>
    /// are the two partial halves the code generator emits into a generated app's own Validation/
    /// folder, under the seam Tasks 1-4 actually changed. Compiled as separate syntax trees via
    /// <see cref="Compiler.CompileSources"/> — never a concatenation, which is how this project hid a
    /// CS0260 uncompilable pair for months on the sibling standalone path (see the
    /// generate-validator-partial-defect history).
    /// </summary>
    [TestMethod]
    public void AppPathPartialHalvesCompileTogether_UnderTheNewSeam()
    {
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var surname = new PropertyDefinition { Name = "Surname", Type = "string" };
        surname.Rules.Add(new PropertyRule { Name = "required" });
        definition.Properties.Add(surname);

        var models = """
namespace Acme.Models;

public partial class Customer
{
    public int Id { get; set; }
    public string Surname { get; set; } = string.Empty;
}
""";

        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", ["Customer"])),
            ("Models/Customer.cs", models),
            ("Validation/CustomerValidator.Generated.cs",
                ValidatorTemplate.Generated("Acme", definition, [])),
            ("Validation/CustomerValidator.Custom.cs",
                ValidatorTemplate.CustomStub("Acme", definition.Name)),
        });

        var errors = string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");
    }

    /// <summary>
    /// The STANDALONE <c>generate validator</c> command's real two-file output, driven through its
    /// own entry point (<see cref="EntityExtractor.GenerateValidator"/> — exactly what
    /// <c>CommandHandlingService.HandleGenerateValidator</c> calls on the no-selected-project path
    /// this test exercises) rather than a hand-written guess of what it emits. Compiles the class
    /// file exactly as <see cref="Validation.ClassValidationWriter.EnsurePartial"/> leaves it,
    /// together with the <c>{Class}.Validation.cs</c> that
    /// <see cref="ValidationEngine.EmitValidationPartial"/> produces, as two separate syntax trees.
    /// </summary>
    /// <remarks>
    /// A near-identical guard already exists — <c>GenerateValidatorEndToEndTests
    /// .The_class_and_its_generated_validator_compile_together</c> (committed in fde5e52, well
    /// before this branch, as the original fix for the CS0260 defect this task's title names). This
    /// test is this task's own, current evidence for the same guarantee: it uses the brief's own
    /// Customer/Surname fixture, and its passing here confirms Task 1's seam left this path
    /// untouched. It went GREEN on the first run — nothing in CodeGenerationEngine/ needed changing.
    /// </remarks>
    [TestMethod]
    public void StandaloneValidatorPartialHalvesCompileTogether_UntouchedByTheSeam()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sv-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            var classFile = Path.Combine(folder, "Customer.cs");
            File.WriteAllText(classFile,
                "namespace Acme\r\n" +
                "{\r\n" +
                "    public class Customer\r\n" +          // NOT partial, as create class writes it
                "    {\r\n" +
                "        public int Id { get; set; }\r\n" +
                "        public string Surname { get; set; }\r\n" +
                "    }\r\n" +
                "}\r\n");

            ValidationRegistry.Clear();
            ProjectManager.CurrentProject = null;

            var parseCtx = new ValidationParseContext
            {
                Fragment = "Surname is required",
                ClassName = "Customer"
            };

            foreach (var rule in ValidationRuleCatalog.Default
                         .Parse(parseCtx.Fragment, ValidationCategory.Data, parseCtx))
                ValidationRegistry.Add("Customer", rule);

            var command = new ParsedCommand
            {
                Intent = IntentType.GenerateValidator,
                ClassName = "Customer"
            };

            Assert.IsTrue(EntityExtractor.GenerateValidator(command, folder));

            var validatorFile = Path.Combine(folder, "Customer.Validation.cs");
            Assert.IsTrue(File.Exists(validatorFile));

            var result = Compiler.CompileSources(new (string Path, string Code)[]
            {
                (classFile, File.ReadAllText(classFile)),
                (validatorFile, File.ReadAllText(validatorFile)),
            });

            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));

            Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");
        }
        finally
        {
            ValidationRegistry.Clear();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}

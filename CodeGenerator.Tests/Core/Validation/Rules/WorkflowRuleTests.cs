using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class WorkflowRuleTests
{
    private static ValidationEmitContext ContextFor(string property) =>
        new(new System.Text.StringBuilder(),
            [new PropertyDefinition { Name = property, Type = "string" }]);

    [TestMethod]
    public void Category_is_Workflow()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());

        Assert.AreEqual(ValidationCategory.Workflow, rule.Category);
    }

    [TestMethod]
    public void Properties_names_the_one_status_property()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());

        CollectionAssert.AreEqual(new[] { "Status" }, rule.Properties.ToArray());
    }

    [TestMethod]
    public void Emit_writes_a_backward_move_check_naming_both_values()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());
        var ctx = ContextFor("Status");

        rule.Emit(ctx);
        var code = ctx.Source.ToString();

        StringAssert.Contains(code, "stored.Status");
        StringAssert.Contains(code, "item.Status");
        StringAssert.Contains(code, "\"Draft\"");
        StringAssert.Contains(code, "\"Approved\"");
        StringAssert.Contains(code, "\"Shipped\"");
    }

    [TestMethod]
    public void Emit_writes_a_final_status_check_naming_the_final_value()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Cancelled"], new HashSet<string> { "Cancelled" });
        var ctx = ContextFor("Status");

        rule.Emit(ctx);
        var code = ctx.Source.ToString();

        StringAssert.Contains(code, "\"Cancelled\"");
    }

    [TestMethod]
    public void Emit_skips_with_a_comment_when_the_property_is_missing()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved"], new HashSet<string>());
        var ctx = ContextFor("SomethingElse");

        rule.Emit(ctx);

        StringAssert.Contains(ctx.Source.ToString(), "workflow rule skipped");
    }

    /// <summary>
    /// A status value containing both '"' and '\' (plausible from real scanned data -- a
    /// stray CSV-parse artifact) must not break the generated app's compile. A substring
    /// assertion on the escaped text would not have caught the CS1009 "Unrecognized escape
    /// sequence" the naive $"\"{v}\"" interpolation produced -- only actually compiling does.
    /// </summary>
    [TestMethod]
    public void Emit_escapes_quotes_and_backslashes_so_the_generated_code_compiles()
    {
        const string awkward = "Appro\"ved\\"; // literal: Appro"ved\

        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Status", Type = "string" });

        var rule = new WorkflowRule("Status", ["Draft", awkward, "Shipped"], new HashSet<string> { awkward });

        CompileWithWorkflowRule(definition, [rule]);
    }

    private static void CompileWithWorkflowRule(ClassDefinition definition, IReadOnlyList<IValidationRule> workflowRules)
    {
        const string LookupStub = """
namespace Acme.Validation;

public sealed class StoreEntityLookup : IEntityLookup
{
    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;
    public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new() => 0;
    public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId)
        where TEntity : class, new() => false;
}
""";

        var modelProperties = string.Join("\n", definition.Properties
            .Select(p => $"    public {p.Type} {p.Name} {{ get; set; }}"));

        var models = $$"""
using System;

namespace Acme.Models;

public class {{definition.Name}}
{
{{modelProperties}}
}
""";

        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: false)),
            ("Validation/StoreEntityLookup.cs", LookupStub),
            ("Models/" + definition.Name + ".cs", models),
            ("Validation/" + definition.Name + "Validator.Generated.cs",
                ValidatorTemplate.Generated(
                    "Acme", definition, [], referencesRequired: false, workflowRules: workflowRules)),
            ("Validation/" + definition.Name + "Validator.Custom.cs",
                ValidatorTemplate.CustomStub("Acme", definition.Name)),
        });

        var message = string.Join("\n",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
    }
}

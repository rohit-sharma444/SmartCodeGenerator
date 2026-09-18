using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The Workflow stage, proven with a hand-built rule list -- no proposal, no order command,
/// just the emission mechanics. Task 3 wires the real accepted-proposal path in.
/// </summary>
[TestClass]
public class WorkflowStageEmissionTests
{
    private const string LookupStub = """
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

    [TestMethod]
    public void A_backward_move_is_refused()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Status", Type = "string" });

        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());
        var validate = CompiledValidator(definition, [rule]);

        var errors = validate.UpdateErrors("Approved", "Draft");

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "cannot move backward");
    }

    [TestMethod]
    public void A_forward_skip_is_allowed()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Status", Type = "string" });

        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());
        var validate = CompiledValidator(definition, [rule]);

        var errors = validate.UpdateErrors("Draft", "Shipped");

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void A_move_from_a_final_status_is_refused()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Status", Type = "string" });

        var rule = new WorkflowRule("Status", ["Draft", "Cancelled"], new HashSet<string> { "Cancelled" });
        var validate = CompiledValidator(definition, [rule]);

        var errors = validate.UpdateErrors("Cancelled", "Reopened");

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "cannot change once it is");
    }

    [TestMethod]
    public void No_workflow_rules_means_no_workflow_stage_at_all()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var source = ValidatorTemplate.Generated("Acme", definition, [], referencesRequired: false);

        Assert.DoesNotContain("private static void Workflow(", source);
        Assert.DoesNotContain("        Workflow,", source);
    }

    private static (System.Type EntityType, System.Func<string, string, IReadOnlyList<string>> UpdateErrors)
        CompiledValidator(ClassDefinition definition, IReadOnlyList<IValidationRule> workflowRules)
    {
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

        var assembly = System.Reflection.Assembly.Load(result.AssemblyBytes!);
        var entityType = assembly.GetType("Acme.Models." + definition.Name)!;
        var validatorType = assembly.GetType("Acme.Validation." + definition.Name + "Validator")!;
        var validator = System.Activator.CreateInstance(validatorType)!;
        var validateMethod = validatorType.GetMethod("Validate")!;

        var lookupType = assembly.GetType("Acme.Validation.StoreEntityLookup")!;
        var contextType = assembly.GetType("Acme.Validation.ValidationContext`1")!.MakeGenericType(entityType);
        var updateOp = assembly.GetType("Acme.Validation.ValidationOperation")!.GetField("Update")!.GetValue(null)!;

        return (entityType, (storedStatus, itemStatus) =>
        {
            var stored = System.Activator.CreateInstance(entityType)!;
            entityType.GetProperty("Status")!.SetValue(stored, storedStatus);

            var item = System.Activator.CreateInstance(entityType)!;
            entityType.GetProperty("Status")!.SetValue(item, itemStatus);

            var ctor = contextType.GetConstructors()[0];
            var context = ctor.Invoke([item, updateOp, stored, System.Activator.CreateInstance(lookupType), null, null]);
            var outcome = validateMethod.Invoke(validator, [context])!;
            return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
        });
    }
}

using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The Business stage, proven with a hand-built rule list — no registry, no scan, just the
/// emission mechanics. Task 3 wires ValidationRegistry in; this task only proves the stage
/// itself compiles and runs given any IValidationRule list.
/// </summary>
[TestClass]
public class BusinessStageEmissionTests
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
    public void A_comparison_rule_refuses_a_record_that_breaks_it()
    {
        var definition = new ClassDefinition { Name = "Booking" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "StartDate", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "EndDate", Type = "DateTime" });

        var rule = new ComparisonRule(
            "StartDate", "EndDate", "> 0", "StartDate must not be after EndDate");

        var validate = CompiledValidator(definition, [rule]);

        var badItem = System.Activator.CreateInstance(validate.EntityType)!;
        validate.EntityType.GetProperty("StartDate")!.SetValue(badItem, new System.DateTime(2026, 9, 10));
        validate.EntityType.GetProperty("EndDate")!.SetValue(badItem, new System.DateTime(2026, 9, 1));

        var errors = validate.Errors(badItem);

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "StartDate must not be after EndDate");
    }

    [TestMethod]
    public void The_same_rule_passes_a_record_that_does_not_break_it()
    {
        var definition = new ClassDefinition { Name = "Booking" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "StartDate", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "EndDate", Type = "DateTime" });

        var rule = new ComparisonRule(
            "StartDate", "EndDate", "> 0", "StartDate must not be after EndDate");

        var validate = CompiledValidator(definition, [rule]);

        var goodItem = System.Activator.CreateInstance(validate.EntityType)!;
        validate.EntityType.GetProperty("StartDate")!.SetValue(goodItem, new System.DateTime(2026, 9, 1));
        validate.EntityType.GetProperty("EndDate")!.SetValue(goodItem, new System.DateTime(2026, 9, 10));

        Assert.IsEmpty(validate.Errors(goodItem));
    }

    [TestMethod]
    public void No_business_rules_means_no_business_stage_at_all()
    {
        var definition = new ClassDefinition { Name = "Booking" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var source = ValidatorTemplate.Generated("Acme", definition, [], referencesRequired: false);

        Assert.DoesNotContain("private static void Business(", source);
        Assert.DoesNotContain("        Business,", source);
    }

    private static (System.Type EntityType, System.Func<object, IReadOnlyList<string>> Errors)
        CompiledValidator(ClassDefinition definition, IReadOnlyList<IValidationRule> businessRules)
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
                    "Acme", definition, [], referencesRequired: false, businessRules: businessRules)),
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
        var forMethod = contextType.GetMethod("For")!;
        var insert = assembly.GetType("Acme.Validation.ValidationOperation")!.GetField("Insert")!.GetValue(null)!;

        return (entityType, item =>
        {
            var context = forMethod.Invoke(null, [item, insert, System.Activator.CreateInstance(lookupType)]);
            var outcome = validateMethod.Invoke(validator, [context])!;
            return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
        });
    }
}

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
    public void Two_business_rules_on_one_entity_each_judge_independently()
    {
        // Both rules are ComparisonRule, which declares __l/__r at a flat scope with no braces of
        // its own: the regression this proves is CS0128 (duplicate local) when two such rules used
        // to share one StringBuilder, so this MUST be two rules of the same kind, not two different
        // kinds whose helper-local names happen not to collide.
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "StartDate", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "EndDate", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "Discount", Type = "decimal" });
        definition.Properties.Add(new PropertyDefinition { Name = "Total", Type = "decimal" });

        var dateRule = new ComparisonRule(
            "StartDate", "EndDate", "> 0", "StartDate must not be after EndDate");
        var amountRule = new ComparisonRule(
            "Discount", "Total", "> 0", "Discount must not exceed Total");

        // (a) This call alone is the real regression test: CompiledValidator asserts
        // result.Success, so two rules of the same kind naming the SAME locals (__l, __r) failing
        // to compile (CS0128) would fail right here, before any assertion below runs.
        var validate = CompiledValidator(definition, [dateRule, amountRule]);

        // (b) One rule violated, the other satisfied -> exactly the ONE broken rule's message,
        // proving each rule's own try/catch scope judges independently of its neighbour.
        var item = System.Activator.CreateInstance(validate.EntityType)!;
        validate.EntityType.GetProperty("StartDate")!.SetValue(item, new System.DateTime(2026, 9, 10));
        validate.EntityType.GetProperty("EndDate")!.SetValue(item, new System.DateTime(2026, 9, 1));
        validate.EntityType.GetProperty("Discount")!.SetValue(item, 5m);
        validate.EntityType.GetProperty("Total")!.SetValue(item, 100m);

        var oneViolation = validate.Errors(item);

        Assert.HasCount(1, oneViolation);
        StringAssert.Contains(oneViolation[0], "StartDate must not be after EndDate");

        // Now break the SECOND rule too: both messages must appear, proving neither rule's block
        // silently swallowed -- or was swallowed by -- the other's.
        validate.EntityType.GetProperty("Discount")!.SetValue(item, 500m);

        var bothViolations = validate.Errors(item);

        Assert.HasCount(2, bothViolations);
        Assert.IsTrue(bothViolations.Any(e => e.Contains("StartDate must not be after EndDate")));
        Assert.IsTrue(bothViolations.Any(e => e.Contains("Discount must not exceed Total")));
    }

    [TestMethod]
    public void A_rule_naming_an_unknown_property_still_compiles_as_a_comment_only_block()
    {
        // ComparisonRule.Emit writes only a "// business rule skipped: ..." comment when a named
        // property is not on the entity. Wrapped alone in its own try { } that comment must still
        // compile (a comment is a no-op statement list, which C# allows), and must add no error.
        var definition = new ClassDefinition { Name = "Widget" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var rule = new ComparisonRule(
            "Nonexistent", "AlsoNonexistent", "> 0", "Nonexistent must not exceed AlsoNonexistent");

        var validate = CompiledValidator(definition, [rule]);

        var item = System.Activator.CreateInstance(validate.EntityType)!;
        Assert.IsEmpty(validate.Errors(item));
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

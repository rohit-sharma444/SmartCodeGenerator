using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ValidatorEmitsEveryRuleKindTests
{
    [TestMethod]
    public void AMinValueRuleOnAnInt_IsEmitted()
    {
        // Today Checks returns early for any non-string property, so this rule
        // disappears and the generated validator reports success over nothing.
        var source = GeneratedFor("Rating", "int",
            new PropertyRule { Name = "minvalue", MinValue = 0 });

        StringAssert.Contains(source, "item.Rating < 0");
    }

    [TestMethod]
    public void AMaxValueRuleOnAnInt_IsEmitted()
    {
        var source = GeneratedFor("Rating", "int",
            new PropertyRule { Name = "maxvalue", MaxValue = 255 });

        StringAssert.Contains(source, "item.Rating > 255");
    }

    [TestMethod]
    public void AScaleRuleOnADecimal_IsEmitted()
    {
        var source = GeneratedFor("Price", "decimal",
            new PropertyRule { Name = "scale", Scale = 2 });

        StringAssert.Contains(source, "decimal.Round(item.Price, 2)");
    }

    [TestMethod]
    public void AZeroScaleRule_SaysWholeNumber_NotZeroPlacesOrFewer()
    {
        // Found by running a generated app over a real decimal(18,0) column: the sentence the
        // user read was "PAIDUPVALUE must have 0 decimal places or fewer", and there is no fewer
        // than none. The check itself was right; only the sentence was unreadable. decimal(18,0)
        // is a common declaration, so this is the message such a column shows every time.
        var source = GeneratedFor("PaidUpValue", "decimal",
            new PropertyRule { Name = "scale", Scale = 0 });

        StringAssert.Contains(source, "decimal.Round(item.PaidUpValue, 0)");
        StringAssert.Contains(source, "PaidUpValue must be a whole number.");
        Assert.DoesNotContain("0 decimal places or fewer", source);
    }

    [TestMethod]
    public void AMinDateRuleOnADateTime_IsEmitted()
    {
        var source = GeneratedFor("OrderedOn", "DateTime",
            new PropertyRule { Name = "mindate", MinDate = "1753-01-01" });

        // A bare "1753" would pass even sitting in a comment or on an unrelated property —
        // assert the actual comparison shape against the right property instead.
        StringAssert.Contains(source, "item.OrderedOn < DateTime.Parse(\"1753-01-01\"");

        // And the guard that keeps an untouched optional date fillable. Pinned here as well as
        // in the run-it test, so the shape of the emitted line is not quietly lost.
        StringAssert.Contains(source, "item.OrderedOn != default(DateTime)");
    }

    [TestMethod]
    public void ADateOnlyRuleOnADateTime_IsEmitted()
    {
        var source = GeneratedFor("DueOn", "DateTime", new PropertyRule { Name = "dateonly" });

        StringAssert.Contains(source, "item.DueOn.TimeOfDay");
    }

    /// <summary>The rule names inference can record, each with a value its emitter would read.</summary>
    private static readonly (string Name, PropertyRule Rule)[] EveryRuleKind =
    [
        ("required", new PropertyRule { Name = "required" }),
        ("maxlength", new PropertyRule { Name = "maxlength", MaxLength = 50 }),
        ("minvalue", new PropertyRule { Name = "minvalue", MinValue = 0 }),
        ("maxvalue", new PropertyRule { Name = "maxvalue", MaxValue = 255 }),
        ("scale", new PropertyRule { Name = "scale", Scale = 2 }),
        ("mindate", new PropertyRule { Name = "mindate", MinDate = "1753-01-01" }),
        ("dateonly", new PropertyRule { Name = "dateonly" }),
    ];

    /// <summary>The five types DomainModelMapper maps a column to.</summary>
    private static readonly string[] EveryEmittedType = ["string", "int", "decimal", "DateTime", "bool"];

    /// <summary>What Generated emits in place of a body when no check was produced at all.</summary>
    private const string NoChecks = "// This model declares no inferred rules.";

    [TestMethod]
    public void EveryPairEmittableRulesDeclares_ReallyEmitsACheck()
    {
        // Half one of the anti-silent-drop guard, and what makes EmittableRules honest rather
        // than merely asserted: every pair it claims is driven through Checks and must produce a
        // check that names the property. A pair added to the list with no emitter fails here.
        foreach (var (type, ruleName) in ValidatorTemplate.EmittableRules)
        {
            var rule = EveryRuleKind.Single(r => r.Name == ruleName).Rule;
            var source = GeneratedFor("Field", type, rule);

            StringAssert.Contains(source, "item.Field",
                $"EmittableRules claims ('{type}', '{ruleName}') but Checks emitted no check.");
            Assert.DoesNotContain(NoChecks, source,
                $"EmittableRules claims ('{type}', '{ruleName}') but the validator body is empty.");
        }
    }

    [TestMethod]
    public void EveryPairEmittableRulesOmits_ReallyEmitsNothing()
    {
        // Half two, and the half a bare list of names cannot express. Checks guards every check
        // on the property's TYPE, so a rule on the wrong type emits nothing whatever -- a scale
        // rule on an int, or a required rule on a DateTime. Asserting the omissions is what
        // stops the list quietly claiming less than Checks does, which would let inference
        // record a pair that emits nothing and still pass the guard above.
        foreach (var type in EveryEmittedType)
            foreach (var (ruleName, rule) in EveryRuleKind)
            {
                if (ValidatorTemplate.CanEmit(type, ruleName))
                    continue;

                StringAssert.Contains(GeneratedFor("Field", type, rule), NoChecks,
                    $"Checks emits something for ('{type}', '{ruleName}'), "
                    + "which EmittableRules does not declare.");
            }
    }

    [TestMethod]
    public void EveryRuleNameInferenceCanRecord_IsPairedWithAtLeastOneType()
    {
        // A name dropped from EmittableRules altogether would make both halves above pass
        // vacuously -- nothing claims it, and nothing emits it. This is the third leg.
        foreach (var (name, _) in EveryRuleKind)
            Assert.IsTrue(
                ValidatorTemplate.EmittableRules.Any(pair => pair.Rule == name),
                $"No type is paired with '{name}', so nothing emits it.");
    }

    [TestMethod]
    public void RequiredIsPairedWithStringAlone()
    {
        // The pair that was wrong, pinned by name. string.IsNullOrWhiteSpace is the only
        // meaningful emptiness test among the five types; a non-nullable int cannot be null and
        // 0 is a legitimate value, so a required check on one would assert nothing true.
        var types = ValidatorTemplate.EmittableRules
            .Where(pair => pair.Rule == "required")
            .Select(pair => pair.Type)
            .ToList();

        Assert.HasCount(1, types);
        Assert.AreEqual("string", types[0]);
    }

    [TestMethod]
    public void AnUnsetDate_PassesTheFloorCheck_ButADateBelowItStillFails()
    {
        // Runs the generated validator rather than reading its source, because the defect this
        // pins is a runtime one: the generated model types every date column as a non-nullable
        // DateTime, so an untouched field arrives as 0001-01-01 -- below SQL Server's own floor.
        // Without the default(DateTime) guard the validator refuses every record saved with an
        // optional date left blank, which makes the form unusable, and no substring assertion
        // over the emitted source would have noticed.
        var validate = CompiledValidator();

        var unset = System.Activator.CreateInstance(validate.EntityType)!;

        Assert.IsEmpty(validate.Errors(unset), "an untouched date must not be refused");

        var typed = System.Activator.CreateInstance(validate.EntityType)!;
        validate.EntityType.GetProperty("OrderedOn")!
            .SetValue(typed, new System.DateTime(1600, 5, 4));

        var errors = validate.Errors(typed);

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "must be on or after 1753-01-01");

        var allowed = System.Activator.CreateInstance(validate.EntityType)!;
        validate.EntityType.GetProperty("OrderedOn")!
            .SetValue(allowed, new System.DateTime(2026, 9, 6));

        Assert.IsEmpty(validate.Errors(allowed), "a date above the floor must pass");
    }

    /// <summary>
    /// Compiles a one-property Order model plus its generated validator, loads the assembly, and
    /// hands back the entity type with a delegate that runs Validate over an instance.
    /// </summary>
    private static (System.Type EntityType, System.Func<object, IReadOnlyList<string>> Errors)
        CompiledValidator()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var ordered = new PropertyDefinition { Name = "OrderedOn", Type = "DateTime" };
        ordered.Rules.Add(new PropertyRule { Name = "mindate", MinDate = "1753-01-01" });
        definition.Properties.Add(ordered);

        var models = """
using System;

namespace Acme.Models;

public class Order
{
    public int Id { get; set; }
    public DateTime OrderedOn { get; set; }
}
""";

        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [])),
            ("Validation/StoreEntityLookup.cs", LookupStub),
            ("Models/Order.cs", models),
            ("Validation/OrderValidator.Generated.cs", ValidatorTemplate.Generated("Acme", definition, [])),
            ("Validation/OrderValidator.Custom.cs", ValidatorTemplate.CustomStub("Acme", definition.Name)),
        });

        var message = string.Join("\n",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");

        var assembly = System.Reflection.Assembly.Load(result.AssemblyBytes!);
        var entityType = assembly.GetType("Acme.Models.Order")!;
        var validatorType = assembly.GetType("Acme.Validation.OrderValidator")!;
        var validator = System.Activator.CreateInstance(validatorType)!;
        var validateMethod = validatorType.GetMethod("Validate")!;

        var lookupType = assembly.GetType("Acme.Validation.StoreEntityLookup")!;
        var contextType = assembly.GetType("Acme.Validation.ValidationContext`1")!
            .MakeGenericType(entityType);
        var forMethod = contextType.GetMethod("For")!;
        var insert = assembly.GetType("Acme.Validation.ValidationOperation")!
            .GetField("Insert")!.GetValue(null)!;

        return (entityType, item =>
        {
            var context = forMethod.Invoke(null, [item, insert, System.Activator.CreateInstance(lookupType)]);
            var outcome = validateMethod.Invoke(validator, [context])!;
            return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
        });
    }

    /// <summary>Stands in for the real StoreEntityLookup, which would drag the whole store seam
    /// in. The generated validator's parameterless constructor news this up, so it must exist —
    /// declared under the same Acme.Validation namespace as IEntityLookup, kept real rather than
    /// stripped, so this test would also catch a wrong namespace on the generated files.</summary>
    private const string LookupStub = """
namespace Acme.Validation;

public sealed class StoreEntityLookup : IEntityLookup
{
    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;

    public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new() => 0;
}
""";

    [TestMethod]
    public void EveryNewRuleKind_EmitsCodeThatCompiles()
    {
        // Substring assertions above prove the check is PRESENT. Only compiling proves
        // it is valid C# — a decimal literal without its m suffix passes the first and
        // fails the second.
        var definition = new ClassDefinition { Name = "Product" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var rating = new PropertyDefinition { Name = "Rating", Type = "int" };
        rating.Rules.Add(new PropertyRule { Name = "minvalue", MinValue = 0 });
        rating.Rules.Add(new PropertyRule { Name = "maxvalue", MaxValue = 255 });
        definition.Properties.Add(rating);

        var price = new PropertyDefinition { Name = "Price", Type = "decimal" };
        price.Rules.Add(new PropertyRule { Name = "scale", Scale = 2 });
        // A non-integral bound on a decimal: this is the one that fails to compile
        // without the m suffix, and passes a substring assertion either way.
        price.Rules.Add(new PropertyRule { Name = "maxvalue", MaxValue = 2.5m });
        definition.Properties.Add(price);

        var ordered = new PropertyDefinition { Name = "OrderedOn", Type = "DateTime" };
        ordered.Rules.Add(new PropertyRule { Name = "mindate", MinDate = "1753-01-01" });
        ordered.Rules.Add(new PropertyRule { Name = "dateonly" });
        definition.Properties.Add(ordered);

        var models = """
using System;

namespace Acme.Models;

public class Product
{
    public int Id { get; set; }
    public int Rating { get; set; }
    public decimal Price { get; set; }
    public DateTime OrderedOn { get; set; }
}
""";

        // Compiler.CompileSources, not EmittedSource.Flatten: each file keeps its own real
        // namespace and using directives instead of having them stripped and hoisted, so a wrong
        // namespace or a missing using in the emitted output would show up here as a genuine
        // compile error rather than being silently normalised away. The two partial halves of
        // ProductValidator (Generated + CustomStub) are still compiled together, as separate
        // syntax trees in one compilation — Roslyn resolves partial declarations across the
        // whole compilation, not per syntax tree, so this does not weaken CS0260 detection.
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [])),
            ("Validation/StoreEntityLookup.cs", LookupStub),
            ("Models/Product.cs", models),
            ("Validation/ProductValidator.Generated.cs", ValidatorTemplate.Generated("Acme", definition, [])),
            ("Validation/ProductValidator.Custom.cs", ValidatorTemplate.CustomStub("Acme", definition.Name)),
        });

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var message = string.Join("\n", errors.Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
        Assert.IsEmpty(errors, $"Compilation errors:\n{message}");
    }

    private static string GeneratedFor(string propertyName, string type, PropertyRule rule)
    {
        var definition = new ClassDefinition { Name = "Product" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var property = new PropertyDefinition { Name = propertyName, Type = type };
        property.Rules.Add(rule);
        definition.Properties.Add(property);

        return ValidatorTemplate.Generated("Acme", definition, []);
    }
}

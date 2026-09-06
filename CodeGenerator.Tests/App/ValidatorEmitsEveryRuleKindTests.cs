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
    public void AMinDateRuleOnADateTime_IsEmitted()
    {
        var source = GeneratedFor("OrderedOn", "DateTime",
            new PropertyRule { Name = "mindate", MinDate = "1753-01-01" });

        // A bare "1753" would pass even sitting in a comment or on an unrelated property —
        // assert the actual comparison shape against the right property instead.
        StringAssert.Contains(source, "item.OrderedOn < DateTime.Parse(\"1753-01-01\"");
    }

    [TestMethod]
    public void ADateOnlyRuleOnADateTime_IsEmitted()
    {
        var source = GeneratedFor("DueOn", "DateTime", new PropertyRule { Name = "dateonly" });

        StringAssert.Contains(source, "item.DueOn.TimeOfDay");
    }

    [TestMethod]
    public void EveryRuleNameInferenceCanRecord_HasAnEmitter()
    {
        // The anti-silent-drop guard. If a later task teaches inference a new rule name
        // and forgets the emitter, this goes red instead of shipping a missing check.
        foreach (var name in ValidatorTemplate.EmittableRuleNames)
            Assert.IsFalse(string.IsNullOrWhiteSpace(name));

        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("required"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("maxlength"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("minvalue"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("maxvalue"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("scale"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("mindate"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("dateonly"));
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
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
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

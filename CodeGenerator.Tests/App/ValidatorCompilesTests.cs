using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>Emitted validators must COMPILE, not merely contain the right substrings. Roslyn
/// error-recovery lets malformed emitted code pass syntax-tree assertions, and the rules here
/// interpolate both entity names and message literals — exactly where injection breaks compilation.
/// The model class and the two seams are composed in so the validator has real types to bind to.</summary>
[TestClass]
public class ValidatorCompilesTests
{
    /// <summary>Stands in for the real StoreEntityLookup, which would drag the whole store seam in.
    /// The generated validator's parameterless constructor news this up, so it must exist.</summary>
    private const string LookupStub = """
public sealed class StoreEntityLookup : IEntityLookup
{
    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;

    public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new() => 0;
}
""";

    /// <summary>The emitted validator plus everything it binds against — the real seams, a stub
    /// lookup and the model declarations — flattened so one Compile call sees the lot.</summary>
    private static string Compose(
        string modelDeclarations, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets) =>
        EmittedSource.Flatten(
            ValidationContextTemplate.Source("Acme"),
            ValidatorTemplate.ValidatorInterface("Acme"),
            ValidatorTemplate.EntityLookupInterface("Acme"),
            ValidationPipelineTemplate.Source("Acme"),
            PermissionsTemplate.Source("Acme", []),
            LookupStub,
            modelDeclarations,
            ValidatorTemplate.Generated("Acme", definition, foreignKeyTargets),
            ValidatorTemplate.CustomStub("Acme", definition.Name));

    private static PropertyDefinition Prop(string name, string type) => new() { Name = name, Type = type };

    [TestMethod]
    public void A_validator_with_every_rule_family_compiles()
    {
        var name = Prop("Name", "string");
        name.Rules.Add(new PropertyRule { Name = "required" });
        name.Rules.Add(new PropertyRule { Name = "maxlength", MaxLength = 200 });

        var email = Prop("Email", "string");
        email.Rules.Add(new PropertyRule { Name = "maxlength", MaxLength = 200 });
        email.Attributes.Add("EmailAddress");

        var phone = Prop("Phone", "string");
        phone.Attributes.Add("Phone");

        var id = Prop("Id", "int");
        id.Attributes.Add("Key");

        // Two foreign keys, not one: each target gets its own emitted block, and nothing else in
        // the suite compiles a model that carries more than a single lookup check.
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.AddRange(new[]
        {
            id, name, email, phone,
            Prop("RegionId", "int"), Prop("Region", "Region"),
            Prop("TierId", "int"), Prop("Tier", "Tier"),
        });

        var models = """
public class Region { public int Id { get; set; } }
public class Tier { public int Id { get; set; } }
public class Customer
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int RegionId { get; set; }
    public Region? Region { get; set; }
    public int TierId { get; set; }
    public Tier? Tier { get; set; }
}
""";

        EmittedSource.AssertCompiles(Compose(models, definition, new[] { "Region", "Tier" }));
    }

    [TestMethod]
    public void A_validator_for_a_model_with_no_rules_compiles()
    {
        var definition = new ClassDefinition { Name = "Tag" };
        definition.Properties.Add(Prop("Id", "int"));

        EmittedSource.AssertCompiles(Compose(
            "public class Tag { public int Id { get; set; } }",
            definition,
            System.Array.Empty<string>()));
    }

    [TestMethod]
    public void Two_email_properties_do_not_collide_on_a_local_variable()
    {
        // Each property's checks get their own try block precisely so locals stay scoped.
        var primary = Prop("Email", "string");
        primary.Attributes.Add("EmailAddress");
        var backup = Prop("BackupEmail", "string");
        backup.Attributes.Add("EmailAddress");

        var definition = new ClassDefinition { Name = "Contact" };
        definition.Properties.AddRange(new[] { primary, backup });

        EmittedSource.AssertCompiles(Compose(
            "public class Contact { public string? Email { get; set; } public string? BackupEmail { get; set; } }",
            definition,
            System.Array.Empty<string>()));
    }
}

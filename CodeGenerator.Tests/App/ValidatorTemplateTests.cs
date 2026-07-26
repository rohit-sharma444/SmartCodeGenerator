using System.Reflection;
using AppScaffolding;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>The fixed part of the generated Validation/ folder: the seams and the factory.</summary>
[TestClass]
public class ValidatorTemplateTests
{
    /// <summary>Flattens emitted files into one compilable unit. See
    /// <see cref="EmittedSource.Flatten"/> for why the hoisting lives in the harness.</summary>
    private static string Flatten(params string[] sources) => EmittedSource.Flatten(sources);

    /// <summary>The store seam StoreEntityLookup binds against, and one entity for the factory.</summary>
    private const string Stubs = """
using System;
using System.Collections.Generic;

public interface IStore<T> where T : class, new()
{
    List<T> GetAll();
    bool SaveAll(List<T> items);
    int NextId(List<T> items);
}

public static class StoreFactory
{
    public static IStore<T> Create<T>() where T : class, new() => null!;
}

public class Customer { public int Id { get; set; } }

public sealed class CustomerValidator : IValidator<Customer>
{
    public IReadOnlyList<string> Validate(Customer item) => Array.Empty<string>();
}
""";

    [TestMethod]
    public void Validator_interface_compiles_and_declares_the_validation_namespace()
    {
        var source = ValidatorTemplate.ValidatorInterface("Acme");
        Assert.Contains("namespace Acme.Validation", source);
        EmittedSource.AssertCompiles(source);
    }

    [TestMethod]
    public void Entity_lookup_interface_compiles()
    {
        EmittedSource.AssertCompiles(ValidatorTemplate.EntityLookupInterface("Acme"));
    }

    [TestMethod]
    public void Store_entity_lookup_compiles_against_the_store_seam()
    {
        // Stubs' CustomerValidator implements IValidator<Customer>, so this seam test also needs
        // that interface declared -- ValidatorInterface, flattened alongside it.
        EmittedSource.AssertCompiles(Flatten(
            Stubs,
            ValidatorTemplate.EntityLookupInterface("Acme"),
            ValidatorTemplate.StoreEntityLookup("Acme"),
            ValidatorTemplate.ValidatorInterface("Acme")));
    }

    [TestMethod]
    public void Factory_compiles_and_maps_every_entity_to_its_validator()
    {
        var source = ValidatorTemplate.Factory("Acme", new[] { "Customer" });

        Assert.Contains("typeof(Customer)", source);
        Assert.Contains("new CustomerValidator()", source);

        EmittedSource.AssertCompiles(Flatten(Stubs, ValidatorTemplate.ValidatorInterface("Acme"), source));
    }

    [TestMethod]
    public void Factory_falls_back_to_a_no_op_validator_for_an_unmapped_type()
    {
        // Repository<T> is generic: a T with no generated validator must not throw. Merely
        // asserting the string "NullValidator" appears in the output is true even if the fallback
        // path were broken (e.g. thrown instead of returned), so this compiles the factory for one
        // mapped entity (Customer) plus an UNMAPPED one (Order), loads the resulting assembly, and
        // actually calls ValidatorFactory.Create<Order>() through reflection: a broken fallback --
        // throwing, or returning anything other than NullValidator<T> -- fails this test.
        const string unmappedEntity = "public class Order { public int Id { get; set; } }";

        var source = Flatten(
            Stubs,
            unmappedEntity,
            ValidatorTemplate.ValidatorInterface("Acme"),
            ValidatorTemplate.Factory("Acme", new[] { "Customer" }));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var factoryType = assembly.GetType("ValidatorFactory")!;
        var orderType = assembly.GetType("Order")!;
        var create = factoryType.GetMethod("Create")!.MakeGenericMethod(orderType);

        var validator = create.Invoke(null, null);

        Assert.IsNotNull(validator);
        Assert.AreEqual("NullValidator`1", validator!.GetType().Name);
    }

    [TestMethod]
    public void Factory_with_no_entities_still_compiles()
    {
        var source = ValidatorTemplate.Factory("Acme", System.Array.Empty<string>());

        Assert.Contains("NullValidator", source);
        EmittedSource.AssertCompiles(Flatten(Stubs, ValidatorTemplate.ValidatorInterface("Acme"), source));
    }

    private static CodeGenerationEngine.Models.ClassDefinition ClassWith(
        string name, params CodeGenerationEngine.Models.PropertyDefinition[] properties)
    {
        var definition = new CodeGenerationEngine.Models.ClassDefinition { Name = name };
        definition.Properties.AddRange(properties);
        return definition;
    }

    [TestMethod]
    public void A_required_rule_emits_a_blank_check()
    {
        var name = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Name", Type = "string" };
        name.Rules.Add(new CodeGenerationEngine.Models.PropertyRule { Name = "required" });

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Customer", name), System.Array.Empty<string>());

        Assert.Contains("string.IsNullOrWhiteSpace(item.Name)", source);
        Assert.Contains("\"Name is required.\"", source);
    }

    [TestMethod]
    public void A_maxlength_rule_emits_a_length_check_using_the_rules_value()
    {
        var title = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Title", Type = "string" };
        title.Rules.Add(new CodeGenerationEngine.Models.PropertyRule { Name = "maxlength", MaxLength = 120 });

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Book", title), System.Array.Empty<string>());

        Assert.Contains("Length: > 120", source);
        Assert.Contains("\"Title must be 120 characters or fewer.\"", source);
    }

    [TestMethod]
    public void An_email_attribute_emits_a_shape_check()
    {
        var email = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Email", Type = "string" };
        email.Attributes.Add("EmailAddress");

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Contact", email), System.Array.Empty<string>());

        Assert.Contains("\"Email is not a valid email address.\"", source);
    }

    [TestMethod]
    public void A_phone_attribute_emits_a_shape_check()
    {
        var phone = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Phone", Type = "string" };
        phone.Attributes.Add("Phone");

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Contact", phone), System.Array.Empty<string>());

        Assert.Contains("\"Phone is not a valid phone number.\"", source);
    }

    [TestMethod]
    public void A_key_attribute_emits_no_check()
    {
        // The store assigns Id in Create, so validating it would reject every new record.
        var id = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Id", Type = "int" };
        id.Attributes.Add("Key");

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Customer", id), System.Array.Empty<string>());

        Assert.DoesNotContain("item.Id", source);
    }

    [TestMethod]
    public void A_foreign_key_target_emits_a_lookup_check()
    {
        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Order"), new[] { "Customer" });

        Assert.Contains("item.CustomerId != 0", source);
        Assert.Contains("_lookup.Exists<Customer>(item.CustomerId)", source);
        Assert.Contains("\"CustomerId does not refer to a known Customer.\"", source);
    }

    [TestMethod]
    public void Every_validator_calls_the_custom_hook()
    {
        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Customer"), System.Array.Empty<string>());

        Assert.Contains("ValidateCustom(item, errors);", source);
        Assert.Contains("partial void ValidateCustom(Customer item, List<string> errors);", source);
    }

    [TestMethod]
    public void The_custom_stub_implements_the_hook_as_a_partial_of_the_same_class()
    {
        var source = ValidatorTemplate.CustomStub("Acme", "Customer");

        Assert.Contains("public sealed partial class CustomerValidator", source);
        Assert.Contains("partial void ValidateCustom(Customer item, List<string> errors)", source);
    }
}

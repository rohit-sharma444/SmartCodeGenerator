using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>The fixed part of the generated Validation/ folder: the seams and the factory.</summary>
[TestClass]
public class ValidatorTemplateTests
{
    /// <summary>Flattens an emitted Validation/ file so several can be compiled together, and
    /// supplies stubs for the generated types they reference from other namespaces.</summary>
    private static string Flatten(string source) => source
        .Replace("using Acme.Models;", string.Empty)
        .Replace("using Acme.Data;", string.Empty)
        .Replace("namespace Acme.Validation;", string.Empty);

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
        EmittedSource.AssertCompiles(
            Stubs
            + Flatten(ValidatorTemplate.EntityLookupInterface("Acme"))
            + Flatten(ValidatorTemplate.StoreEntityLookup("Acme"))
            + Flatten(ValidatorTemplate.ValidatorInterface("Acme")));
    }

    [TestMethod]
    public void Factory_compiles_and_maps_every_entity_to_its_validator()
    {
        var source = ValidatorTemplate.Factory("Acme", new[] { "Customer" });

        Assert.Contains("typeof(Customer)", source);
        Assert.Contains("new CustomerValidator()", source);

        EmittedSource.AssertCompiles(
            Stubs
            + Flatten(ValidatorTemplate.ValidatorInterface("Acme"))
            + Flatten(source));
    }

    [TestMethod]
    public void Factory_falls_back_to_a_no_op_validator_for_an_unmapped_type()
    {
        // Repository<T> is generic: a T with no generated validator must not throw.
        Assert.Contains("NullValidator", ValidatorTemplate.Factory("Acme", new[] { "Customer" }));
    }

    [TestMethod]
    public void Factory_with_no_entities_still_compiles()
    {
        var source = ValidatorTemplate.Factory("Acme", System.Array.Empty<string>());

        Assert.Contains("NullValidator", source);
        EmittedSource.AssertCompiles(
            Stubs
            + Flatten(ValidatorTemplate.ValidatorInterface("Acme"))
            + Flatten(source));
    }
}

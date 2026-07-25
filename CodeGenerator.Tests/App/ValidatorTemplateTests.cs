using System.Reflection;
using AppScaffolding;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>The fixed part of the generated Validation/ folder: the seams and the factory.</summary>
[TestClass]
public class ValidatorTemplateTests
{
    /// <summary>
    /// Flattens several emitted Validation/ files (and any other C# snippets) into one compilable
    /// unit: strips each file's own namespace declaration and its &lt;App&gt;.Models/&lt;App&gt;.Data
    /// using (the stubs below stand in for those types), then hoists every remaining
    /// <c>using</c> directive to the front of the result.
    /// </summary>
    /// <remarks>
    /// A <c>using</c> directive may not follow a type declaration anywhere in a single
    /// compilation unit (CS1529), and <c>Compiler.Compile</c> parses the whole concatenated
    /// string as one syntax tree. Simply concatenating several files that each open with their
    /// own <c>using</c> would therefore not compile once more than one type-bearing fragment is
    /// joined -- this hoisting belongs here, in the test harness, precisely so the production
    /// templates can keep plain <c>using</c> directives and bare type names, matching every
    /// sibling template, when they are emitted stand-alone into a real generated app.
    /// </remarks>
    private static string Flatten(params string[] sources)
    {
        var usings = new List<string>();
        var bodies = new List<string>();

        foreach (var source in sources)
        {
            var stripped = source
                .Replace("using Acme.Models;", string.Empty)
                .Replace("using Acme.Data;", string.Empty)
                .Replace("namespace Acme.Validation;", string.Empty);

            foreach (var line in stripped.Split('\n'))
            {
                if (line.TrimStart().StartsWith("using ", StringComparison.Ordinal))
                    usings.Add(line.Trim());
                else
                    bodies.Add(line);
            }
        }

        return string.Join("\n", usings.Distinct()) + "\n" + string.Join("\n", bodies);
    }

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
}

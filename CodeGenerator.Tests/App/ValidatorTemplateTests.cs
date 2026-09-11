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
    public ValidationOutcome Validate(ValidationContext<Customer> context) => ValidationOutcome.Valid;
}
""";

    [TestMethod]
    public void Validator_interface_compiles_and_declares_the_validation_namespace()
    {
        var source = ValidatorTemplate.ValidatorInterface("Acme");
        Assert.Contains("namespace Acme.Validation", source);
        EmittedSource.AssertCompiles(Flatten(
            ValidationContextTemplate.Source("Acme"), ValidatorTemplate.EntityLookupInterface("Acme"), source));
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
        // that interface declared -- ValidatorInterface, flattened alongside it -- and both it and
        // ValidatorInterface name ValidationContext/ValidationOutcome, so the context seam comes too.
        EmittedSource.AssertCompiles(Flatten(
            ValidationContextTemplate.Source("Acme"),
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

        EmittedSource.AssertCompiles(Flatten(
            ValidationContextTemplate.Source("Acme"), ValidatorTemplate.EntityLookupInterface("Acme"),
            Stubs, ValidatorTemplate.ValidatorInterface("Acme"), source));
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
            ValidationContextTemplate.Source("Acme"),
            ValidatorTemplate.EntityLookupInterface("Acme"),
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
        EmittedSource.AssertCompiles(Flatten(
            ValidationContextTemplate.Source("Acme"), ValidatorTemplate.EntityLookupInterface("Acme"),
            Stubs, ValidatorTemplate.ValidatorInterface("Acme"), source));
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
        Assert.Contains("context.Lookup.Exists<Customer>(item.CustomerId)", source);
        Assert.Contains("\"CustomerId does not refer to a known Customer.\"", source);
    }

    [TestMethod]
    public void Every_validator_calls_the_custom_hook()
    {
        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Customer"), System.Array.Empty<string>());

        Assert.Contains("ValidateCustom(item, errors);", source);
        Assert.Contains("private static partial void ValidateCustom(Customer item, List<ValidationError> errors);", source);
    }

    [TestMethod]
    public void The_custom_stub_implements_the_hook_as_a_partial_of_the_same_class()
    {
        var source = ValidatorTemplate.CustomStub("Acme", "Customer");

        Assert.Contains("public sealed partial class CustomerValidator", source);
        Assert.Contains("private static partial void ValidateCustom(Customer item, List<ValidationError> errors)", source);
    }

    /// <summary>
    /// The fourth reconciliation point: a re-scaffold under a changed prefix has to repoint the
    /// write-once custom stub's header at the new root, or it references a namespace no generated
    /// file declares any more. Only the header may move — the hand-written rule below it, and any
    /// using the file's own author added, are the user's and must survive byte for byte.
    /// </summary>
    [TestMethod]
    public void ReconcileCustomStubNamespace_RepointsTheHeaderAndLeavesTheRestAlone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "validator_fixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "CustomerValidator.Custom.cs");
            const string original = """
                using System.Collections.Generic;
                using CodisLtd.Shop.Models;
                using System.Text.RegularExpressions;

                namespace CodisLtd.Shop.Validation;

                public sealed partial class CustomerValidator
                {
                    partial void ValidateCustom(Customer item, List<string> errors)
                    {
                        if (Regex.IsMatch(item.Name, "^[a-z]"))
                            errors.Add("Name must not start with a lower-case letter.");
                    }
                }
                """;
            File.WriteAllText(path, original);

            ValidatorTemplate.ReconcileCustomStubNamespace(path, "Acme.Shop");

            var updated = File.ReadAllText(path);

            // The header moved.
            Assert.Contains("namespace Acme.Shop.Validation;", updated);
            Assert.Contains("using Acme.Shop.Models;", updated);
            Assert.IsFalse(updated.Contains("CodisLtd", StringComparison.Ordinal));

            // An unrelated using is byte-for-byte intact -- it never named the old root.
            Assert.Contains("using System.Text.RegularExpressions;", updated);

            // The body -- the user's own rule -- is untouched.
            Assert.Contains("if (Regex.IsMatch(item.Name, \"^[a-z]\"))", updated);
            Assert.Contains(
                "errors.Add(\"Name must not start with a lower-case letter.\");", updated);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A user who reformatted their stub to the block form must still get repointed: matching only
    /// the file-scoped shape would silently no-op here, leaving the file declaring a namespace no
    /// generated file declares any more -- the very failure this reconciler exists to prevent.
    /// </summary>
    [TestMethod]
    public void ReconcileCustomStubNamespace_RepointsABlockScopedDeclaration()
    {
        var folder = Path.Combine(Path.GetTempPath(), "validator_fixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "CustomerValidator.Custom.cs");
            const string original = """
                using System.Collections.Generic;
                using CodisLtd.Shop.Models;
                using System.Text.RegularExpressions;

                namespace CodisLtd.Shop.Validation
                {
                    public sealed partial class CustomerValidator
                    {
                        partial void ValidateCustom(Customer item, List<string> errors)
                        {
                            if (Regex.IsMatch(item.Name, "^[a-z]"))
                                errors.Add("Name must not start with a lower-case letter.");
                        }
                    }
                }
                """;
            File.WriteAllText(path, original);

            ValidatorTemplate.ReconcileCustomStubNamespace(path, "Acme.Shop");

            var updated = File.ReadAllText(path);

            // The header moved.
            Assert.Contains("namespace Acme.Shop.Validation", updated);
            Assert.Contains("using Acme.Shop.Models;", updated);
            Assert.IsFalse(updated.Contains("CodisLtd", StringComparison.Ordinal));

            // An unrelated using is byte-for-byte intact -- it never named the old root.
            Assert.Contains("using System.Text.RegularExpressions;", updated);

            // Everything from the opening brace onward -- the braces, the body, the user's own
            // rule -- survives byte for byte: only the header above it may move.
            const string fromOpeningBrace = "{";
            var originalTail = original[original.IndexOf(fromOpeningBrace, StringComparison.Ordinal)..];
            var updatedTail = updated[updated.IndexOf(fromOpeningBrace, StringComparison.Ordinal)..];
            Assert.AreEqual(originalTail, updatedTail);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void ReconcileCustomStubNamespace_IsIdempotent()
    {
        var folder = Path.Combine(Path.GetTempPath(), "validator_fixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "CustomerValidator.Custom.cs");
            File.WriteAllText(path, ValidatorTemplate.CustomStub("CodisLtd.Shop", "Customer"));

            ValidatorTemplate.ReconcileCustomStubNamespace(path, "Acme.Shop");
            var once = File.ReadAllText(path);

            ValidatorTemplate.ReconcileCustomStubNamespace(path, "Acme.Shop");

            Assert.AreEqual(once, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>With no prefix the current root equals the project name, so reconciling a stub
    /// that already names it must be a byte-for-byte no-op.</summary>
    [TestMethod]
    public void ReconcileCustomStubNamespace_WithoutAPrefixChangesNothing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "validator_fixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "CustomerValidator.Custom.cs");
            var original = ValidatorTemplate.CustomStub("Shop", "Customer");
            File.WriteAllText(path, original);

            ValidatorTemplate.ReconcileCustomStubNamespace(path, "Shop");

            Assert.AreEqual(original, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void ReconcileCustomStubNamespace_OnAMissingFile_DoesNotThrow()
        => ValidatorTemplate.ReconcileCustomStubNamespace(
            Path.Combine(Path.GetTempPath(), "no-such-file-" + Path.GetRandomFileName() + ".cs"),
            "Acme.Shop");
}

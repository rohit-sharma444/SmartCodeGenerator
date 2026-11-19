using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AppScaffolding;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SeedDataTemplateTests
{
    private static ClassDefinition Class(string name, params PropertyDefinition[] props)
    {
        var def = new ClassDefinition { Name = name };
        foreach (var p in props) def.Properties.Add(p);
        return def;
    }

    private static PropertyDefinition Prop(string name, string type) =>
        new() { Name = name, Type = type };

    [TestMethod]
    public void OrderByDependency_PutsReferencedEntityBeforeItsReferencer()
    {
        // Order references Customer (CustomerId int + Customer nav), so Customer must seed first.
        var order = Class("Order",
            Prop("Id", "int"), Prop("CustomerId", "int"), Prop("Customer", "Customer"));
        var customer = Class("Customer", Prop("Id", "int"), Prop("Name", "string"));

        var ordered = SeedDataTemplate.OrderByDependency(new List<ClassDefinition> { order, customer });

        CollectionAssert.AreEqual(new[] { "Customer", "Order" }, (System.Collections.ICollection)ordered);
    }

    [TestMethod]
    public void OrderByDependency_ToleratesNoRelationships()
    {
        var a = Class("Alpha", Prop("Id", "int"));
        var b = Class("Beta", Prop("Id", "int"));

        var ordered = SeedDataTemplate.OrderByDependency(new List<ClassDefinition> { a, b });

        Assert.HasCount(2, ordered);
        CollectionAssert.Contains((System.Collections.ICollection)ordered, "Alpha");
        CollectionAssert.Contains((System.Collections.ICollection)ordered, "Beta");
    }

    [TestMethod]
    public void Source_EmitsSeedCallsInOrder_AndSeedRowCount()
    {
        var src = SeedDataTemplate.Source("Shop", new[] { "Customer", "Order" }, new[] { "CustomerId" });

        StringAssert.Contains(src, "namespace Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "public const int SeedRowCount = 3;");
        StringAssert.Contains(src, "Seed<Customer>(seededIds);");
        StringAssert.Contains(src, "Seed<Order>(seededIds);");
        StringAssert.Contains(src, "ForeignKeyFields");
        // Customer's Seed call precedes Order's.
        Assert.IsLessThan(src.IndexOf("Seed<Order>"), src.IndexOf("Seed<Customer>"));
    }

    [TestMethod]
    public void ForeignKeyFieldNames_ReturnsOnlyRealForeignKeys_AndSourceGatesOnThem()
    {
        var order = Class("Order",
            Prop("Id", "int"), Prop("CustomerId", "int"), Prop("Customer", "Customer"),
            Prop("ExternalId", "int"));   // int ending in Id but NOT a FK (no nav sibling)
        var customer = Class("Customer", Prop("Id", "int"), Prop("Name", "string"));
        var classes = new System.Collections.Generic.List<ClassDefinition> { order, customer };

        var fks = SeedDataTemplate.ForeignKeyFieldNames(classes);

        CollectionAssert.Contains((System.Collections.ICollection)fks, "CustomerId");
        CollectionAssert.DoesNotContain((System.Collections.ICollection)fks, "ExternalId");

        var src = SeedDataTemplate.Source("Shop",
            SeedDataTemplate.OrderByDependency(classes), fks);
        StringAssert.Contains(src, "ForeignKeyFields.Contains(name)");
        StringAssert.Contains(src, "\"CustomerId\"");
        Assert.IsFalse(src.Contains("\"ExternalId\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// Found running a real generated Blazor login app for validation-phase-3's task 15: a
    /// freshly generated app started with EMPTY business data even though a real Data/Seed/
    /// file existed, because EnsureSeeded runs before anyone could possibly be signed in and
    /// its imported-rows branch wrote through the SAME permission-gated SaveAll that task 2
    /// correctly taught to refuse a null-role write. Every existing test above asserts on
    /// emitted TEXT, so none of them could see a write actually get refused. This test compiles
    /// the REAL JsonStore, StoreFactory, Permissions (login: true) and ActingUser together and
    /// proves EnsureSeeded's imported-rows branch still populates the store under the matrix
    /// that correctly refuses every OTHER null-role write.
    /// </summary>
    [TestMethod]
    public void EnsureSeeded_imports_seed_file_rows_before_anyone_has_signed_in()
    {
        var entityName = $"Widget{Guid.NewGuid():N}"[..14];

        var model = $$"""
namespace Acme.Models;

public class {{entityName}}
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
""";

        var sources = new List<(string Path, string Code)>
        {
            ("Session.cs", SessionTemplate.Source("Acme")),
            ("IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("JsonStore.cs", JsonStoreTemplate.Source("Acme.Data")),
            ("StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: true)),
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth: true)),
            ("Models/Entity.cs", model),
            ("Data/SeedData.cs", SeedDataTemplate.Source(
                "Acme", [entityName], [], [entityName])),
        };
        sources.AddRange(AuthCoreFiles.For("Acme").Select(f => (f.Name, f.Source)));

        var result = Compiler.CompileSources(sources);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));

        var assembly = Assembly.Load(result.AssemblyBytes!);

        var storeJson = Path.Combine(AppContext.BaseDirectory, entityName + ".json");
        var seedDir = Path.Combine(AppContext.BaseDirectory, "Data", "Seed");
        var seedJson = Path.Combine(seedDir, entityName + ".json");

        try
        {
            if (File.Exists(storeJson))
                File.Delete(storeJson);

            Directory.CreateDirectory(seedDir);
            File.WriteAllText(seedJson, $$"""[{"Id":1,"Name":"Widget One"},{"Id":2,"Name":"Widget Two"}]""");

            // Nobody has signed in: Session.Current is null, so ActingUser.Role() is null, and
            // under login: true, Permissions.Allows correctly refuses every OTHER write from a
            // null role. Importing the seed file must be the one write that goes through
            // anyway, or a freshly generated login app starts with no data at all.
            assembly.GetType("Acme.Data.SeedData")!.GetMethod("EnsureSeeded")!.Invoke(null, null);

            var entityType = assembly.GetType("Acme.Models." + entityName)!;
            var storeType = assembly.GetType("Acme.Data.StoreFactory")!;
            var store = storeType.GetMethod("Create")!.MakeGenericMethod(entityType)
                .Invoke(null, null)!;
            var all = (System.Collections.IList)store.GetType().GetMethod("GetAll")!
                .Invoke(store, null)!;

            Assert.HasCount(2, all);
        }
        finally
        {
            if (File.Exists(storeJson))
                File.Delete(storeJson);
            if (File.Exists(seedJson))
                File.Delete(seedJson);
        }
    }

    /// <summary>
    /// As above, for the OTHER call site with the same bug: an entity with no Data/Seed file
    /// falls back to generated sample rows, written through the same SaveAll.
    /// </summary>
    [TestMethod]
    public void EnsureSeeded_writes_sample_rows_before_anyone_has_signed_in()
    {
        var entityName = $"Gadget{Guid.NewGuid():N}"[..14];

        var model = $$"""
namespace Acme.Models;

public class {{entityName}}
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
""";

        var sources = new List<(string Path, string Code)>
        {
            ("Session.cs", SessionTemplate.Source("Acme")),
            ("IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("JsonStore.cs", JsonStoreTemplate.Source("Acme.Data")),
            ("StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: true)),
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth: true)),
            ("Models/Entity.cs", model),
            ("Data/SeedData.cs", SeedDataTemplate.Source("Acme", [entityName], [])),
        };
        sources.AddRange(AuthCoreFiles.For("Acme").Select(f => (f.Name, f.Source)));

        var result = Compiler.CompileSources(sources);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));

        var assembly = Assembly.Load(result.AssemblyBytes!);

        var storeJson = Path.Combine(AppContext.BaseDirectory, entityName + ".json");

        try
        {
            if (File.Exists(storeJson))
                File.Delete(storeJson);

            // No Data/Seed file for this entity, so EnsureSeeded falls back to generated
            // sample rows -- the OTHER SaveAll call site, same bug, before anyone has signed in.
            var seedDataType = assembly.GetType("Acme.Data.SeedData")!;
            seedDataType.GetMethod("EnsureSeeded")!.Invoke(null, null);

            var entityType = assembly.GetType("Acme.Models." + entityName)!;
            var storeType = assembly.GetType("Acme.Data.StoreFactory")!;
            var store = storeType.GetMethod("Create")!.MakeGenericMethod(entityType)
                .Invoke(null, null)!;
            var all = (System.Collections.IList)store.GetType().GetMethod("GetAll")!
                .Invoke(store, null)!;

            var expectedCount = (int)seedDataType.GetField("SeedRowCount")!.GetValue(null)!;
            Assert.HasCount(expectedCount, all);
        }
        finally
        {
            if (File.Exists(storeJson))
                File.Delete(storeJson);
        }
    }

    /// <summary>
    /// Every seeded row passes its own validator when a property has a value list. The seeder
    /// wrote "Status 1", "Status 2", "Status 3", so every seeded row broke the list, and saving one
    /// unchanged (a PUT of the row a GET returned) was refused with "Status must be one of: ...".
    /// With "no two rows may share" too, the rows take different values, and a list too short
    /// for every row seeds fewer rows, leaving one value for a record the user adds.
    /// </summary>
    [TestMethod]
    [DataRow(false, 5, 3, DisplayName = "a list")]
    [DataRow(true, 5, 3, DisplayName = "a list no two rows share")]
    [DataRow(false, 2, 3, DisplayName = "a short list")]
    [DataRow(true, 2, 1, DisplayName = "a short list no two rows share")]
    public void Every_seeded_row_keeps_to_its_value_list(bool unique, int listed, int expectedRows)
    {
        var entityName = $"Ticket{Guid.NewGuid():N}"[..14];
        string[] values = ["open", "shut", "hold", "done", "new"];
        values = values[..listed];

        var status = new PropertyDefinition { Name = "Status", Type = "string" };
        status.Rules.Add(new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = [.. values] });
        if (unique)
            status.Rules.Add(new PropertyRule { PropertyName = "Status", Name = "unique", IsUnique = true });

        var definition = Class(entityName, Prop("Id", "int"), Prop("Title", "string"), status);

        var sources = new List<(string Path, string Code)>
        {
            ("GlobalUsings.cs", "global using System;\nglobal using System.Collections.Generic;"),
            ("Session.cs", SessionTemplate.Source("Acme")),
            ("IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("JsonStore.cs", JsonStoreTemplate.Source("Acme.Data")),
            ("StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: false)),
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth: true)),
            ("Validation/Lookup.cs", """
namespace Acme.Validation;

public sealed class FindsEverything : IEntityLookup
{
    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;
    public int CountReferencing<TEntity>(string foreignKeyProperty, int id) where TEntity : class, new() => 0;
    public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId) where TEntity : class, new() => false;
}
"""),
            ("Models/Entity.cs", RoslynClassGenerator.Generate(definition, "Acme.Models")),
            ("Validation/EntityValidator.Generated.cs",
                ValidatorTemplate.Generated("Acme", definition, [], referencesRequired: false)),
            ("Validation/EntityValidator.Custom.cs", ValidatorTemplate.CustomStub("Acme", entityName)),
            ("Data/SeedData.cs", SeedDataTemplate.Source("Acme", [entityName], [], [], [definition])),
        };
        sources.AddRange(AuthCoreFiles.For("Acme").Select(f => (f.Name, f.Source)));

        var result = Compiler.CompileSources(sources);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));

        var assembly = Assembly.Load(result.AssemblyBytes!);
        var storeJson = Path.Combine(AppContext.BaseDirectory, entityName + ".json");

        try
        {
            if (File.Exists(storeJson))
                File.Delete(storeJson);

            assembly.GetType("Acme.Data.SeedData")!.GetMethod("EnsureSeeded")!.Invoke(null, null);

            var entityType = assembly.GetType("Acme.Models." + entityName)!;
            var store = assembly.GetType("Acme.Data.StoreFactory")!.GetMethod("Create")!
                .MakeGenericMethod(entityType).Invoke(null, null)!;
            var rows = ((System.Collections.IEnumerable)store.GetType().GetMethod("GetAll")!
                .Invoke(store, null)!).Cast<object>().ToList();

            Assert.HasCount(expectedRows, rows);

            var validator = Activator.CreateInstance(
                assembly.GetType("Acme.Validation." + entityName + "Validator")!)!;
            var contextFor = assembly.GetType("Acme.Validation.ValidationContext`1")!
                .MakeGenericType(entityType).GetMethod("For")!;
            var update = assembly.GetType("Acme.Validation.ValidationOperation")!
                .GetField("Update")!.GetValue(null)!;
            var lookup = Activator.CreateInstance(assembly.GetType("Acme.Validation.FindsEverything")!);

            var statuses = new List<string>();

            foreach (var row in rows)
            {
                var context = contextFor.Invoke(null, [row, update, lookup]);
                var outcome = validator.GetType().GetMethod("Validate")!.Invoke(validator, [context])!;
                var messages = (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;

                Assert.IsEmpty(messages, string.Join("\n", messages));
                statuses.Add((string)entityType.GetProperty("Status")!.GetValue(row)!);
            }

            if (unique)
                Assert.HasCount(statuses.Count, statuses.Distinct(), string.Join(", ", statuses));

            var source = SeedDataTemplate.Source("Acme", [entityName], [], [], [definition]);
            if (expectedRows < 3)
                StringAssert.Contains(source, $"// {entityName}: ", "no line says why fewer rows are seeded");
        }
        finally
        {
            if (File.Exists(storeJson))
                File.Delete(storeJson);
        }
    }

    /// <summary>An app with no value list gets exactly the seeder it always had.</summary>
    [TestMethod]
    public void Classes_with_no_value_list_change_nothing()
    {
        var plain = Class("Order", Prop("Id", "int"), Prop("Title", "string"));

        Assert.AreEqual(
            SeedDataTemplate.Source("Shop", ["Order"], [], []),
            SeedDataTemplate.Source("Shop", ["Order"], [], [], [plain]));
    }
}

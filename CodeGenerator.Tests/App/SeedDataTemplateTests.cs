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
}

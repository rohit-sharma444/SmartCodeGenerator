using System.Collections;
using System.Reflection;
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class EntityLookupCountingTests
{
    [TestMethod]
    public void CountingReadsThroughTheStore_LikeExistsDoes()
    {
        var source = ValidatorTemplate.StoreEntityLookup("Acme");

        StringAssert.Contains(source, "public int CountReferencing<TEntity>(string foreignKeyProperty, int id)");
        StringAssert.Contains(source, "StoreFactory.Create<TEntity>()");
    }

    [TestMethod]
    public void AnUnknownPropertyCountsZero_RatherThanThrowing()
    {
        // This runs on a delete, in a generated app, against a property name that came from
        // ForeignKeys at generation time. If a rename ever leaves the two disagreeing, refusing
        // every delete with a reflection exception is far worse than counting nothing: the
        // check goes quiet and the delete proceeds, which is what it did before this phase.
        var source = ValidatorTemplate.StoreEntityLookup("Acme");

        StringAssert.Contains(source, "if (property is null)");
        StringAssert.Contains(source, "return 0;");
    }

    [TestMethod]
    public void ItCompilesWithTheRestOfTheFolder()
    {
        // JsonStore now asks Permissions before every write (Task 7's coarse store guard), so
        // Permissions.cs joins the compile list -- ValidationContext.cs was already here, since
        // Permissions itself names ValidationOperation.
        var result = CodeGenerationEngine.Core.Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", Array.Empty<string>(), login: false)),
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth: false)),
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data")),
        });

        var errors = string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");
    }

    /// <summary>
    /// Proves the emitted <c>StoreEntityLookup.AnyOtherWith</c> RUNS: seeds two records with the
    /// same Code through the real, compiled <c>JsonStore&lt;T&gt;</c>/<c>StoreFactory</c> path,
    /// then asks the real, compiled <c>StoreEntityLookup</c> whether another record holds that
    /// value.
    /// </summary>
    [TestMethod]
    public void AnyOtherWith_FindsADuplicate_ButNotWhenTheOnlyMatchIsTheRecordBeingExcepted()
    {
        var (assembly, entityType, lookup) =
            CompileAndConstructStoreEntityLookup(("Code", "string"));

        var idProperty = entityType.GetProperty("Id")!;
        var codeProperty = entityType.GetProperty("Code")!;
        var listType = typeof(List<>).MakeGenericType(entityType);

        var storeFactoryType = assembly.GetType("Acme.Data.StoreFactory")!;
        var store = storeFactoryType.GetMethod("Create")!.MakeGenericMethod(entityType)
            .Invoke(null, null)!;
        var storeType = store.GetType();
        var newItem = storeType.GetMethod("NewItem")!;
        var saveAll = storeType.GetMethod("SaveAll")!;

        try
        {
            // item1 and item2 both hold "DUP" -- either is a duplicate of the other. item3 holds
            // "SOLO" -- it is not a duplicate of anything but itself, which is exactly the case
            // exceptId exists for: an update must not collide with the record it is updating.
            var empty = (IList)Activator.CreateInstance(listType)!;
            var item1 = newItem.Invoke(store, [empty])!;
            codeProperty.SetValue(item1, "DUP");

            var withItem1 = (IList)Activator.CreateInstance(listType)!;
            withItem1.Add(item1);
            var item2 = newItem.Invoke(store, [withItem1])!;
            codeProperty.SetValue(item2, "DUP");

            var withBoth = (IList)Activator.CreateInstance(listType)!;
            withBoth.Add(item1);
            withBoth.Add(item2);
            var item3 = newItem.Invoke(store, [withBoth])!;
            codeProperty.SetValue(item3, "SOLO");

            var all = (IList)Activator.CreateInstance(listType)!;
            all.Add(item1);
            all.Add(item2);
            all.Add(item3);
            saveAll.Invoke(store, [all]);

            var anyOtherWith = lookup.GetType().GetMethod("AnyOtherWith")!
                .MakeGenericMethod(entityType);

            var item1Id = (int)idProperty.GetValue(item1)!;
            var item3Id = (int)idProperty.GetValue(item3)!;

            var collidesWithAnotherRecord =
                (bool)anyOtherWith.Invoke(lookup, ["Code", "DUP", item1Id])!;
            Assert.IsTrue(collidesWithAnotherRecord, "item2 already holds Code=DUP");

            var doesNotCollideWithItself =
                (bool)anyOtherWith.Invoke(lookup, ["Code", "SOLO", item3Id])!;
            Assert.IsFalse(
                doesNotCollideWithItself,
                "the only record holding Code=SOLO is item3 itself, which exceptId excludes");
        }
        finally
        {
            var path = Path.Combine(AppContext.BaseDirectory, entityType.Name + ".json");
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// Compiles a uniquely-named entity type (an <c>Id</c> plus each of
    /// <paramref name="properties"/>) together with the real Validation/Data seam a generated
    /// app's own <c>StoreEntityLookup</c> runs against -- <c>ValidationContext</c>,
    /// <c>IEntityLookup</c>, <c>StoreEntityLookup</c>, <c>Permissions</c>, <c>ActingUser</c>,
    /// <c>StoreFactory</c>, <c>IStore</c> and <c>JsonStore</c> -- loads the result, and
    /// constructs the real <c>StoreEntityLookup</c> over it.
    /// </summary>
    /// <remarks>
    /// No test anywhere in this repository previously constructed a real <c>JsonStore&lt;T&gt;</c>
    /// and wrote through it, and <c>JsonStore&lt;T&gt;</c>'s file path is keyed ONLY by
    /// <c>typeof(T).Name</c> at <c>AppContext.BaseDirectory</c> -- shared by the whole test
    /// assembly, which <c>MSTestSettings.cs</c> parallelizes at the METHOD level. The entity name
    /// is GUID-suffixed so its JSON file can never collide with another test's file, in this run
    /// or a future one, with no locking needed. The caller is responsible for deleting that file
    /// in a <c>finally</c> once it is done with the store -- this helper only compiles and
    /// constructs, since only the caller knows when that is.
    /// <para>
    /// Task 8 reuses this helper (naming and shape settled here, first) rather than inventing a
    /// second, differently-shaped construction for the same seam.
    /// </para>
    /// </remarks>
    internal static (Assembly Assembly, Type EntityType, object Lookup) CompileAndConstructStoreEntityLookup(
        params (string Name, string Type)[] properties)
    {
        var entityName = $"Entity{Guid.NewGuid():N}"[..16];

        var propertyLines = string.Join("\n", properties.Select(p =>
            $"    public {p.Type} {p.Name} {{ get; set; }}" + (p.Type == "string" ? " = \"\";" : "")));

        var model = $$"""
namespace Acme.Models;

public class {{entityName}}
{
    public int Id { get; set; }
{{propertyLines}}
}
""";

        var result = CodeGenerationEngine.Core.Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Models/Entity.cs", model),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", Array.Empty<string>(), login: false)),
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth: false)),
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data")),
        });

        var errors = string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");

        var assembly = Assembly.Load(result.AssemblyBytes!);
        var entityType = assembly.GetType($"Acme.Models.{entityName}")!;
        var lookupType = assembly.GetType("Acme.Validation.StoreEntityLookup")!;
        var lookup = Activator.CreateInstance(lookupType)!;

        return (assembly, entityType, lookup);
    }
}

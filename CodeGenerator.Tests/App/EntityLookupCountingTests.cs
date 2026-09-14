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
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data")),
        });

        var errors = string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");
    }
}

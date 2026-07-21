using System.IO;
using System.Linq;
using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppScaffolderPersistenceTests
{
    private static CandidateDomainModel OneEntity()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Book",
            Fields = { new CandidateField { Name = "Title", Type = "string" } }
        });
        return model;
    }

    private static (string Project, string[] DataFiles, string Csproj) Scaffold(PersistenceKind kind)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var result = new AppScaffolder().Scaffold(OneEntity(), "Sample", workspace, target, kind);
        Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

        var dataFolder = Path.Combine(result.ProjectPath, "Data");
        var dataFiles = Directory.GetFiles(dataFolder).Select(f => Path.GetFileName(f)!).ToArray();
        var csproj = File.ReadAllText(
            Path.Combine(result.ProjectPath, Path.GetFileName(result.ProjectPath) + ".csproj"));
        return (result.ProjectPath, dataFiles, csproj);
        // NOTE: the caller does not delete the workspace; MSTest temp dirs are transient.
    }

    [TestMethod]
    public void Sqlite_EmitsEfCoreFilesAndOmitsJsonStore()
    {
        var (_, dataFiles, csproj) = Scaffold(PersistenceKind.Sqlite);

        CollectionAssert.Contains(dataFiles, "EfStore.cs");
        CollectionAssert.Contains(dataFiles, "AppDbContext.cs");
        CollectionAssert.Contains(dataFiles, "IStore.cs");
        CollectionAssert.Contains(dataFiles, "StoreFactory.cs");
        CollectionAssert.DoesNotContain(dataFiles, "JsonStore.cs");

        StringAssert.Contains(csproj,
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"10.0.10\" />");
    }

    [TestMethod]
    public void Sqlite_StoreFactoryCreatesEfStore()
    {
        var (project, _, _) = Scaffold(PersistenceKind.Sqlite);
        var factory = File.ReadAllText(Path.Combine(project, "Data", "StoreFactory.cs"));

        StringAssert.Contains(factory, "new EfStore<T>()");
    }

    [TestMethod]
    public void Json_EmitsJsonStoreAndNoEfCoreFilesOrPackage()
    {
        var (_, dataFiles, csproj) = Scaffold(PersistenceKind.Json);

        CollectionAssert.Contains(dataFiles, "JsonStore.cs");
        CollectionAssert.DoesNotContain(dataFiles, "EfStore.cs");
        CollectionAssert.DoesNotContain(dataFiles, "AppDbContext.cs");
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Sqlite", csproj,
            "JSON mode must not reference EF Core");
    }
}

using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SeedDataFromFileTests
{
    [TestMethod]
    public void WithNoSeededEntities_TheGeneratedSeederKeepsItsSampleRows()
    {
        var source = SeedDataTemplate.Source("Shop", ["Order"], [], []);

        StringAssert.Contains(source, "SampleValue");
        Assert.DoesNotContain("SeedFiles", source,
            "a project with no imported data must not carry the seed-file branch at all");
    }

    [TestMethod]
    public void WithSeededEntities_TheGeneratedSeederLoadsTheirFiles()
    {
        var source = SeedDataTemplate.Source("Shop", ["Order", "Customer"], [], ["Order"]);

        StringAssert.Contains(source, "SeedFiles");
        StringAssert.Contains(source, "\"Order\"");

        // The folder the scaffolder actually writes to, not a second spelling of it.
        StringAssert.Contains(source, SeedFileWriter.SeedFolderName);

        // Sample rows survive for entities with no file -- Customer above.
        StringAssert.Contains(source, "SampleValue");
    }

    [TestMethod]
    public void InjectSeedContent_AddsTheContentItem()
    {
        var csproj = "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>";

        var updated = CsprojEditing.InjectSeedContent(csproj);

        StringAssert.Contains(updated, "Data\\Seed\\*.json");
        StringAssert.Contains(updated, "PreserveNewest");
    }

    [TestMethod]
    public void InjectSeedContent_IsIdempotent()
    {
        var once = CsprojEditing.InjectSeedContent("<Project>\n</Project>");

        Assert.AreEqual(once, CsprojEditing.InjectSeedContent(once));
    }
}

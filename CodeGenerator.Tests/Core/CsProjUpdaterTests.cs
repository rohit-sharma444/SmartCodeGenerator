using System.Xml.Linq;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// The second place the NETSDK1022 bug lived.
/// </summary>
/// <remarks>
/// <c>ClassRestoreService</c> calls <see cref="CsProjUpdater.AddCompileEntry"/> when a deleted class
/// is restored, which added the same duplicate <c>&lt;Compile&gt;</c> item that broke the build from
/// <c>ProjectFileUpdater</c>. One root cause, two call sites — fixing only the reproduced one would
/// have left restore able to break a project on its own.
/// <para>
/// <see cref="CsProjUpdater.RemoveCompileEntry"/> is deliberately kept: entries written by earlier
/// versions of this tool are still out there in users' projects, and removing them is a repair.
/// </para>
/// </remarks>
[TestClass]
public class CsProjUpdaterTests
{
    private const string SdkProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private static string NewProjectDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scg-cpu-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Shop.csproj"), SdkProject);

        return dir;
    }

    private static IReadOnlyList<string?> CompileIncludes(string directory)
        => XDocument.Load(Path.Combine(directory, "Shop.csproj"))
            .Descendants()
            .Where(x => x.Name.LocalName == "Compile")
            .Select(x => x.Attribute("Include")?.Value)
            .ToList();

    [TestMethod]
    public void RestoringAClassAddsNoCompileItem()
    {
        var directory = NewProjectDirectory();

        try
        {
            var classFile = Path.Combine(directory, "Order.cs");
            File.WriteAllText(classFile, "public class Order { }");

            CsProjUpdater.AddCompileEntry(directory, classFile);

            Assert.IsEmpty(CompileIncludes(directory),
                "the SDK already globs .cs; an explicit item is NETSDK1022");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void AnEntryLeftByAnEarlierVersionIsStillRemovable()
    {
        // The repair path. Projects created before the fix carry these, and deleting a class must
        // still clean one up rather than leaving a Compile item pointing at a deleted file — which
        // is a build error of its own.
        var directory = NewProjectDirectory();

        try
        {
            var csproj = Path.Combine(directory, "Shop.csproj");

            File.WriteAllText(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Order.cs" />
                  </ItemGroup>
                </Project>
                """);

            CsProjUpdater.RemoveCompileEntry(directory, "Order");

            Assert.IsEmpty(CompileIncludes(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void RemovingLeavesUnrelatedEntriesAlone()
    {
        var directory = NewProjectDirectory();

        try
        {
            var csproj = Path.Combine(directory, "Shop.csproj");

            File.WriteAllText(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="Order.cs" />
                    <Compile Include="Customer.cs" />
                  </ItemGroup>
                </Project>
                """);

            CsProjUpdater.RemoveCompileEntry(directory, "Order");

            CollectionAssert.AreEqual(new[] { "Customer.cs" }, CompileIncludes(directory).ToList());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void NeitherOperationThrowsWithoutAProjectFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scg-cpu-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            CsProjUpdater.AddCompileEntry(directory, Path.Combine(directory, "Order.cs"));
            CsProjUpdater.RemoveCompileEntry(directory, "Order");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

using System.Xml.Linq;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// What <see cref="ProjectFileUpdater"/> may put in a .csproj.
/// </summary>
/// <remarks>
/// It used to add an explicit <c>&lt;Compile Include="Order.cs"/&gt;</c> for every class generated.
/// Every project this tool creates is SDK-style, and the SDK globs <c>.cs</c> from the project
/// directory by default — so that entry is a duplicate, and the project stops building with
/// <c>NETSDK1022</c>. The tool reported "Compiled." because it compiles the class in isolation,
/// never the project.
/// <para>
/// Nothing covered this file before, which is how it shipped.
/// </para>
/// </remarks>
[TestClass]
public class ProjectFileUpdaterTests
{
    /// <summary>An SDK-style project, as every `dotnet new` template produces.</summary>
    private const string SdkProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private static string NewProjectDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scg-pfu-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(dir);

        return dir;
    }

    private static (string Csproj, string ClassFile) Scaffold(string directory, string code)
    {
        var csproj = Path.Combine(directory, "Shop.csproj");
        var classFile = Path.Combine(directory, "Order.cs");

        File.WriteAllText(csproj, SdkProject);
        File.WriteAllText(classFile, code);

        return (csproj, classFile);
    }

    private static IReadOnlyList<XElement> CompileItems(string csproj)
        => XDocument.Load(csproj)
            .Descendants()
            .Where(x => x.Name.LocalName == "Compile")
            .ToList();

    [TestMethod]
    public void NoCompileItemIsAddedForAGeneratedClass()
    {
        var directory = NewProjectDirectory();

        try
        {
            var (csproj, classFile) = Scaffold(directory, "public class Order { }");

            ProjectFileUpdater.Update(csproj, classFile, "public class Order { }");

            Assert.IsEmpty(CompileItems(csproj),
                "an explicit Compile item duplicates the SDK's default glob (NETSDK1022)");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ARepeatedUpdateStillAddsNothing()
    {
        // Generating over an existing class is normal; two entries would be twice as broken.
        var directory = NewProjectDirectory();

        try
        {
            var (csproj, classFile) = Scaffold(directory, "public class Order { }");

            ProjectFileUpdater.Update(csproj, classFile, "public class Order { }");
            ProjectFileUpdater.Update(csproj, classFile, "public class Order { }");

            Assert.IsEmpty(CompileItems(csproj));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void TheAnnotationsPackageIsStillAddedWhenTheCodeNeedsIt()
    {
        // The other half of Update's job, and the half that is correct — a [Required] attribute
        // genuinely needs that package reference. Removing the Compile item must not take it out.
        var directory = NewProjectDirectory();

        try
        {
            var (csproj, classFile) = Scaffold(directory, "x");

            ProjectFileUpdater.Update(csproj, classFile,
                "public class Order { [Required] public string Reference { get; set; } }");

            var packages = XDocument.Load(csproj)
                .Descendants()
                .Where(x => x.Name.LocalName == "PackageReference")
                .Select(x => x.Attribute("Include")?.Value)
                .ToList();

            CollectionAssert.Contains(packages, "System.ComponentModel.Annotations");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ThatPackageIsNotAddedTwice()
    {
        var directory = NewProjectDirectory();

        try
        {
            var (csproj, classFile) = Scaffold(directory, "x");

            const string code = "public class Order { [Required] public string R { get; set; } }";

            ProjectFileUpdater.Update(csproj, classFile, code);
            ProjectFileUpdater.Update(csproj, classFile, code);

            var count = XDocument.Load(csproj)
                .Descendants()
                .Count(x => x.Name.LocalName == "PackageReference"
                            && x.Attribute("Include")?.Value == "System.ComponentModel.Annotations");

            Assert.AreEqual(1, count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void TheProjectFileRemainsValidXmlAndKeepsItsSettings()
    {
        var directory = NewProjectDirectory();

        try
        {
            var (csproj, classFile) = Scaffold(directory, "x");

            ProjectFileUpdater.Update(csproj, classFile, "public class Order { }");

            var doc = XDocument.Load(csproj);

            Assert.AreEqual("Project", doc.Root!.Name.LocalName);
            Assert.Contains("net10.0", doc.ToString());
            Assert.Contains("Exe", doc.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void AMissingOrUnreadableProjectFileIsIgnored()
    {
        // Generating a class with no project selected is a supported flow; it must not throw.
        ProjectFileUpdater.Update(null, "Order.cs", "public class Order { }");
        ProjectFileUpdater.Update(string.Empty, "Order.cs", "public class Order { }");
        ProjectFileUpdater.Update(
            Path.Combine(Path.GetTempPath(), "scg-no-such-" + Guid.NewGuid().ToString("N") + ".csproj"),
            "Order.cs",
            "public class Order { }");
    }
}

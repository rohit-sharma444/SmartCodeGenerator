using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Which namespace a generated class should declare.
/// </summary>
/// <remarks>
/// Every class made by <c>create class</c> declared <c>namespace GeneratedCode</c> regardless of the
/// project it was written into, so nothing in that project could see it without a <c>using</c> — and
/// it contradicted the root-namespace work done for <c>build app:</c> in PR #79.
/// </remarks>
[TestClass]
public class ProjectNamespaceTests
{
    /// <summary>The historical name, and still correct when there is no project.</summary>
    private const string Fallback = "GeneratedCode";

    private static string WriteProject(string fileName, string contents)
    {
        var dir = Path.Combine(Path.GetTempPath(), "scg-ns-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, fileName);

        File.WriteAllText(path, contents);

        return path;
    }

    private static void Cleanup(string csproj)
        => Directory.Delete(Path.GetDirectoryName(csproj)!, recursive: true);

    [TestMethod]
    public void WithNoProjectTheHistoricalFallbackIsKept()
    {
        // `create class` with nothing selected writes into the workspace, where there is no project
        // whose namespace it could match. Changing this would break that flow for no gain.
        Assert.AreEqual(Fallback, ProjectNamespace.For(null));
        Assert.AreEqual(Fallback, ProjectNamespace.For(string.Empty));
        Assert.AreEqual(Fallback, ProjectNamespace.For(
            Path.Combine(Path.GetTempPath(), "scg-absent-" + Guid.NewGuid().ToString("N") + ".csproj")));
    }

    [TestMethod]
    public void AnExplicitRootNamespaceWins()
    {
        // What `build app: ... namespace CodisLtd` writes. A class added to such a project afterwards
        // has to land in the same namespace or it will not compile alongside the rest.
        var csproj = WriteProject("Shop.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <RootNamespace>CodisLtd.Shop</RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        try
        {
            Assert.AreEqual("CodisLtd.Shop", ProjectNamespace.For(csproj));
        }
        finally
        {
            Cleanup(csproj);
        }
    }

    [TestMethod]
    public void WithoutOneTheProjectFileNameIsUsed()
    {
        // MSBuild's own default, so this matches what the compiler assumes for Program.cs.
        var csproj = WriteProject("Shop.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        try
        {
            Assert.AreEqual("Shop", ProjectNamespace.For(csproj));
        }
        finally
        {
            Cleanup(csproj);
        }
    }

    [TestMethod]
    public void ANameThatIsNotAValidIdentifierIsMadeIntoOne()
    {
        var csproj = WriteProject("My Shop-App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        try
        {
            var result = ProjectNamespace.For(csproj);

            // Whatever the scheme, the answer must be compilable: no spaces, no hyphens.
            Assert.DoesNotContain(" ", result);
            Assert.DoesNotContain("-", result);
            Assert.IsNotEmpty(result);
        }
        finally
        {
            Cleanup(csproj);
        }
    }

    [TestMethod]
    public void ANameStartingWithADigitIsNotLeftUncompilable()
    {
        var csproj = WriteProject("7Eleven.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        try
        {
            var result = ProjectNamespace.For(csproj);

            Assert.IsFalse(char.IsDigit(result[0]), $"'{result}' cannot start a namespace");
        }
        finally
        {
            Cleanup(csproj);
        }
    }

    [TestMethod]
    public void AnUnreadableProjectFileFallsBackRatherThanThrowing()
    {
        // Generating a class must not fail because the csproj is malformed.
        var csproj = WriteProject("Broken.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><unclosed>");

        try
        {
            Assert.AreEqual("Broken", ProjectNamespace.For(csproj));
        }
        finally
        {
            Cleanup(csproj);
        }
    }
}

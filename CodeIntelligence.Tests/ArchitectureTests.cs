using System.Xml.Linq;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class ArchitectureTests
{
    /// <summary>Repo root: climb from the test bin folder until a folder holding
    /// Directory.Build.props is found.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IReadOnlyList<string> PackageRefs(string csprojRelativePath)
    {
        var full = Path.Combine(RepoRoot(), csprojRelativePath);
        Assert.True(File.Exists(full), $"Missing project file: {full}");

        return XDocument.Load(full)
            .Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void Abstractions_has_no_package_references()
    {
        var refs = PackageRefs(@"CodeIntelligence.Abstractions\CodeIntelligence.Abstractions.csproj");
        Assert.Empty(refs);
    }

    [Fact]
    public void NaturalLanguageBridge_does_not_reference_onnx()
    {
        var refs = PackageRefs(@"NaturalLanguageBridge\NaturalLanguageBridge.csproj");
        Assert.DoesNotContain(refs, r => r.Contains("OnnxRuntime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CodeIntelligence_core_does_not_reference_onnx()
    {
        var refs = PackageRefs(@"CodeIntelligence\CodeIntelligence.csproj");
        Assert.DoesNotContain(refs, r => r.Contains("OnnxRuntime", StringComparison.OrdinalIgnoreCase));
    }
}

using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>The version the tool reports.</summary>
[TestClass]
public class VersionInfoTests
{
    [TestMethod]
    public void TheCommitSuffixIsTrimmed()
    {
        // .NET puts the source revision into AssemblyInformationalVersion as "+<sha>", so a naive
        // read prints "1.0.0+9e1f2a…" at a user. That is the whole reason this is not a one-liner.
        Assert.AreEqual("1.0.0", VersionInfo.Clean("1.0.0+9e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f"));
    }

    [TestMethod]
    public void APlainVersionIsLeftAlone()
    {
        Assert.AreEqual("1.0.0", VersionInfo.Clean("1.0.0"));
        Assert.AreEqual("1.2.3-beta.4", VersionInfo.Clean("1.2.3-beta.4"));
    }

    [TestMethod]
    public void MissingVersionInformationDoesNotProduceAnEmptyLabel()
    {
        // Reported in the banner and in `status`; "Smart Code Generator " with a dangling space
        // looks worse than no version at all, so there is a fallback.
        Assert.IsNotEmpty(VersionInfo.Clean(null));
        Assert.IsNotEmpty(VersionInfo.Clean(""));
        Assert.IsNotEmpty(VersionInfo.Clean("   "));
    }

    [TestMethod]
    public void TheRunningAssemblyReportsTheVersionFromDirectoryBuildProps()
    {
        // Proves the <Version> property actually reaches the CodeGenerator assembly — Current
        // reads that assembly's own attribute — rather than only that Clean() works on a literal.
        Assert.AreEqual("1.0.0", VersionInfo.Current);
    }
}

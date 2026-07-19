using System.IO;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppTargetSelectionTests
{
    private static string[] Parts(params string[] p) => p;

    [TestMethod]
    public void InlineTarget_IsUsedDirectly()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve", "console"), inputRedirected: true, TextReader.Null);
        Assert.AreEqual("console", t!.Key);
    }

    [TestMethod]
    public void InlineUnknownTarget_ReturnsNull()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve", "bogus"), inputRedirected: false, TextReader.Null);
        Assert.IsNull(t);
    }

    [TestMethod]
    public void Redirected_NoInline_DefaultsToWpf_WithoutReading()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve"), inputRedirected: true, TextReader.Null);
        Assert.AreEqual("wpf", t!.Key);
    }

    [TestMethod]
    public void Interactive_BlankLine_DefaultsToWpf()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve"), inputRedirected: false, new StringReader("\n"));
        Assert.AreEqual("wpf", t!.Key);
    }

    [TestMethod]
    public void Interactive_ValidKey_IsChosen()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve"), inputRedirected: false, new StringReader("webapi\n"));
        Assert.AreEqual("webapi", t!.Key);
    }

    [TestMethod]
    public void Interactive_RepromptsUntilValid()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve"), inputRedirected: false, new StringReader("bad\nconsole\n"));
        Assert.AreEqual("console", t!.Key);
    }
}

using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class BuildDiagnosticsTests
{
    private const string Root = @"C:\p";

    [TestMethod]
    public void Parse_ReadsFileLineColumnCodeAndMessage()
    {
        var output = @"C:\p\Models\Order.cs(12,5): error CS0260: Missing partial modifier on declaration of type 'Order'; another partial declaration of this type exists [C:\p\App.csproj]";

        var d = BuildDiagnostics.Parse(FixTestSupport.Result(output), Root).Single();

        Assert.AreEqual(@"C:\p\Models\Order.cs", d.File);
        Assert.AreEqual(12, d.Line);
        Assert.AreEqual(5, d.Column);
        Assert.AreEqual("CS0260", d.Code);
        Assert.AreEqual(
            "Missing partial modifier on declaration of type 'Order'; another partial declaration of this type exists",
            d.Message);
    }

    [TestMethod]
    public void Parse_DropsRepeatedLines()
    {
        var line = @"C:\p\A.cs(1,1): error CS0103: The name 'x' does not exist in the current context [C:\p\App.csproj]";

        var all = BuildDiagnostics.Parse(FixTestSupport.Result(line + "\n" + line), Root);

        Assert.HasCount(1, all);
    }

    [TestMethod]
    public void Parse_IgnoresWarnings()
    {
        var output = @"C:\p\A.cs(1,1): warning CS0108: 'A.ToString()' hides inherited member [C:\p\App.csproj]";

        Assert.HasCount(0, BuildDiagnostics.Parse(FixTestSupport.Result(output), Root));
    }

    [TestMethod]
    public void Parse_ResolvesARelativePathAgainstTheProject()
    {
        var output = @"Models\A.cs(3,2): error CS1002: ; expected";

        var d = BuildDiagnostics.Parse(FixTestSupport.Result(output), Root).Single();

        Assert.AreEqual(Path.GetFullPath(@"C:\p\Models\A.cs"), d.File);
    }

    [TestMethod]
    public void Parse_KeepsAnErrorThatHasNoLocation()
    {
        var output = "CSC : error CS5001: Program does not contain a static 'Main' method suitable for an entry point";

        var d = BuildDiagnostics.Parse(FixTestSupport.Result(output), Root).Single();

        Assert.AreEqual(string.Empty, d.File);
        Assert.AreEqual(0, d.Line);
        Assert.AreEqual("CS5001", d.Code);
    }

    [TestMethod]
    public void Parse_ReadsStandardErrorToo()
    {
        var result = new CodeGenerationEngine.Helper.ProcessResult(
            1, string.Empty, @"C:\p\A.cs(1,1): error CS1002: ; expected", false);

        Assert.HasCount(1, BuildDiagnostics.Parse(result, Root));
    }
}

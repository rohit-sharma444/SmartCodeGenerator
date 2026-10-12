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

    private const string Locked =
        @"C:\Program Files\dotnet\sdk\10.0.100\Microsoft.Common.CurrentVersion.targets(4975,5): error MSB3027: Could not copy ""obj\Debug\App.exe"" to ""bin\Debug\App.exe"". Exceeded retry count of 10. Failed. The file is locked by: ""App (1234)"" [C:\p\App.csproj]";

    private const string CompilerError = @"C:\p\A.cs(1,1): error CS1002: ; expected";

    [TestMethod]
    public void Fixable_CountsOnlyCompilerErrorsInEditableCsFilesOfTheProject()
    {
        var output = string.Join("\n",
            CompilerError,
            Locked,
            @"C:\p\obj\Debug\G.cs(1,1): error CS0101: twice",
            @"C:\other\B.cs(1,1): error CS0103: x",
            @"C:\p\App.csproj(3,3): error CS1000: odd",
            "CSC : error CS5001: Program does not contain a static 'Main' method");

        var all = BuildDiagnostics.Parse(FixTestSupport.Result(output), Root);

        Assert.HasCount(6, all);
        Assert.AreEqual(1, BuildDiagnostics.Fixable(all, Root));
    }

    [TestMethod]
    public void OutputLocked_IsTrueForMsb3027AndMsb3021_Only()
    {
        var locked = BuildDiagnostics.Parse(FixTestSupport.Result(Locked), Root);
        var copy = BuildDiagnostics.Parse(FixTestSupport.Result(Locked.Replace("MSB3027", "MSB3021")), Root);
        var compiler = BuildDiagnostics.Parse(FixTestSupport.Result(CompilerError), Root);

        Assert.IsTrue(BuildDiagnostics.OutputLocked(locked));
        Assert.IsTrue(BuildDiagnostics.OutputLocked(copy));
        Assert.IsFalse(BuildDiagnostics.OutputLocked(compiler));
    }

    [TestMethod]
    public void NoFixReason_SaysStopTheApp_ForALockedExe_AndIsNullWhenSomethingCanBeFixed()
    {
        var locked = BuildDiagnostics.Parse(FixTestSupport.Result(Locked), Root);
        var both = BuildDiagnostics.Parse(FixTestSupport.Result(Locked + "\n" + CompilerError), Root);
        var elsewhere = BuildDiagnostics.Parse(FixTestSupport.Result(@"C:\other\B.cs(1,1): error CS0103: x"), Root);

        Assert.AreEqual(BuildFixReport.LockedOutput, BuildFixReport.NoFixReason(locked, Root));
        StringAssert.Contains(BuildFixReport.LockedOutput, "Stop the app");
        Assert.IsNull(BuildFixReport.NoFixReason(both, Root));
        Assert.AreEqual(BuildFixReport.NothingToFix, BuildFixReport.NoFixReason(elsewhere, Root));
        Assert.IsNull(BuildFixReport.NoFixReason([], Root));
    }
}

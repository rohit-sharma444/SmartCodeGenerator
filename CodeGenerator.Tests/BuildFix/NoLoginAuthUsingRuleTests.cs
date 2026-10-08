using System.Text;
using System.Text.RegularExpressions;
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class NoLoginAuthUsingRuleTests
{
    private const string Source = "using System;\nusing Shop.Auth;\nusing Shop.Models;\n\nnamespace Shop;\n\npublic class MainViewModel { }\n";

    private const string ModelsStub = "namespace Shop.Models { }\n";

    private const string Message = "The type or namespace name 'Auth' does not exist in the namespace 'Shop' (are you missing an assembly reference?)";

    private static BuildDiagnostic Diag(string file, int line = 2, string code = "CS0234", string message = Message)
        => new(file, line, 7, code, message);

    private static FixSession Session(string root)
    {
        var session = new FixSession(root);
        session.BeginRound(1);
        return session;
    }

    private static string Setup(string text, out string root)
    {
        root = FixTestSupport.TempProject();
        return FixTestSupport.Write(root, "ViewModels/MainViewModel.cs", text);
    }

    private static void AssertUntouched(string file, string text)
        => Assert.AreEqual(text, File.ReadAllText(file));

    [TestMethod]
    public void Fix_RemovesOnlyTheUsing_WhenThereIsNoAuthFolder_AndTheFileCompiles()
    {
        var file = Setup(Source, out var root);
        Assert.IsTrue(FixTestSupport.CompileErrors(Source, ModelsStub).Any(e => e.StartsWith("CS0234")));   // RED first

        Assert.IsTrue(new NoLoginAuthUsingRule().Fix(Diag(file), Session(root)));

        var after = File.ReadAllText(file);
        Assert.AreEqual("using System;\nusing Shop.Models;\n\nnamespace Shop;\n\npublic class MainViewModel { }\n", after);
        Assert.HasCount(0, FixTestSupport.CompileErrors(after, ModelsStub));
    }

    [TestMethod]
    public void Fix_PreservesCrlfBomAndTheNextLinesIndent()
    {
        var text = "namespace Shop\r\n{\r\n    using Shop.Auth;\r\n    using Shop.Models;\r\n\r\n    public class MainViewModel { }\r\n}\r\n";
        var root = FixTestSupport.TempProject();
        var file = Path.Combine(root, "MainViewModel.cs");
        File.WriteAllText(file, text, new UTF8Encoding(true));

        Assert.IsTrue(new NoLoginAuthUsingRule().Fix(Diag(file, line: 3), Session(root)));

        var bytes = File.ReadAllBytes(file);
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var after = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.AreEqual("namespace Shop\r\n{\r\n    using Shop.Models;\r\n\r\n    public class MainViewModel { }\r\n}\r\n", after);
        Assert.IsFalse(Regex.IsMatch(after, "(?<!\r)\n"), "a bare LF crept in");
    }

    [TestMethod]
    public void Fix_RemovesAUsingOnTheLastLineWithoutALineBreak()
    {
        var file = Setup("using Shop.Models;\nusing Shop.Auth;", out var root);

        Assert.IsTrue(new NoLoginAuthUsingRule().Fix(Diag(file, line: 2), Session(root)));
        Assert.AreEqual("using Shop.Models;\n", File.ReadAllText(file));
    }

    [TestMethod]
    public void Fix_RemovesOnlyTheLineThatTheCompilerNamed()
    {
        var text = "using Shop.Auth;\nusing Other.Auth;\nusing Shop.Models;\n";
        var file = Setup(text, out var root);

        Assert.IsTrue(new NoLoginAuthUsingRule().Fix(Diag(file, line: 1), Session(root)));
        Assert.AreEqual("using Other.Auth;\nusing Shop.Models;\n", File.ReadAllText(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenTheAuthFolderExists()
    {
        var file = Setup(Source, out var root);
        Directory.CreateDirectory(Path.Combine(root, "Auth"));

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file), Session(root)));
        AssertUntouched(file, Source);
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForALineThatIsAnotherUsing()
    {
        // Line 3 is "using Shop.Models;": the message says Auth, but that line is not an Auth using.
        var file = Setup(Source, out var root);

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 3), Session(root)));
        AssertUntouched(file, Source);
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForALineBeyondTheEndOfTheFile()
    {
        var file = Setup(Source, out var root);

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 99), Session(root)));
        AssertUntouched(file, Source);
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAMessageThatIsNotAboutAuth()
    {
        var file = Setup(Source, out var root);
        var other = Diag(file, message: "The type or namespace name 'Models' does not exist");

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(other, Session(root)));
        AssertUntouched(file, Source);
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAnotherErrorCode()
    {
        var file = Setup(Source, out var root);

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, code: "CS0246"), Session(root)));
        AssertUntouched(file, Source);
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenTheFileIsAbsent()
    {
        var root = FixTestSupport.TempProject();
        var missing = Path.Combine(root, "ViewModels", "Gone.cs");

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(missing), Session(root)));
        Assert.IsFalse(File.Exists(missing));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForANamespaceThatOnlySharesAPrefixWithAuth()
    {
        var text = "using System;\nusing Shop.Authors;\nusing Shop.AuthKit.Models;\n";
        var file = Setup(text, out var root);

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 2), Session(root)));
        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 3), Session(root)));
        AssertUntouched(file, text);
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenTheUsingIsNotOnItsOwnLine()
    {
        var text = "using System; using Shop.Auth;\nusing Shop.Models;\n";
        var file = Setup(text, out var root);

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 1), Session(root)));
        AssertUntouched(file, text);

        var text2 = "using Shop.Auth; using Shop.Models;\n";
        File.WriteAllText(file, text2);
        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 1), Session(root)));
        AssertUntouched(file, text2);
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAnAliasOrAStaticUsing()
    {
        var text = "using A = Shop.Auth;\nusing static Shop.Auth;\n";
        var file = Setup(text, out var root);

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 1), Session(root)));
        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 2), Session(root)));
        AssertUntouched(file, text);
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAUsingInsideAComment()
    {
        var text = "// using Shop.Auth;\nusing Shop.Models;\n";
        var file = Setup(text, out var root);

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 1), Session(root)));
        AssertUntouched(file, text);
    }

    [TestMethod]
    public void Fix_Refuses_AFileOutsideTheProject()
    {
        var root = FixTestSupport.TempProject();
        var outside = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(outside, "MainViewModel.cs", Source);

        Assert.ThrowsExactly<InvalidOperationException>(() => new NoLoginAuthUsingRule().Fix(Diag(file), Session(root)));
        AssertUntouched(file, Source);
    }
}

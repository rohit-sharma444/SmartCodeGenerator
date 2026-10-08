using System.Text;
using System.Text.RegularExpressions;
using AppScaffolding;
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class StaleCustomHookRuleTests
{
    private const string Models = "namespace Shop.Models { public class Order { public int Id { get; set; } } }\n";

    private const string Support = "namespace Shop.Validation { public record ValidationError(string? Field, string Message); }\n";

    private const string Generated =
        "using System.Collections.Generic;\nusing Shop.Models;\n\nnamespace Shop.Validation;\n\n"
        + "public sealed partial class OrderValidator\n{\n"
        + "    private static partial void ValidateCustom(Order item, List<ValidationError> errors);\n}\n";

    private const string OldEmpty =
        "using System.Collections.Generic;\nusing Shop.Models;\n\nnamespace Shop.Validation;\n\n"
        + "public sealed partial class OrderValidator\n{\n"
        + "    partial void ValidateCustom(Order item, List<string> errors)\n    {\n    }\n}\n";

    private const string OldWithCode =
        "using System.Collections.Generic;\nusing Shop.Models;\n\nnamespace Shop.Validation;\n\n"
        + "public sealed partial class OrderValidator\n{\n"
        + "    partial void ValidateCustom(Order item, List<string> errors)\n    {\n"
        + "        if (item.Id < 0) errors.Add(\"negative\");\n    }\n}\n";

    private static BuildDiagnostic Diag(string file, string code = "CS0759") => new(file, 9, 18, code, "No defining declaration");

    private static FixSession Session(string root)
    {
        var session = new FixSession(root);
        session.BeginRound(1);
        return session;
    }

    [TestMethod]
    public void Fix_RewritesAnEmptyOutdatedStub_AndTheProjectCompiles()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldEmpty);
        FixTestSupport.Write(root, "Validation/OrderValidator.Generated.cs", Generated);
        Assert.IsTrue(FixTestSupport.CompileErrors(Models, Support, Generated, OldEmpty).Any(e => e.StartsWith("CS0759")));   // RED first

        Assert.IsTrue(new StaleCustomHookRule().Fix(Diag(file), Session(root)));

        var after = File.ReadAllText(file);
        StringAssert.Contains(after, "private static partial void ValidateCustom(Order item, List<ValidationError> errors)");
        StringAssert.Contains(after, "namespace Shop.Validation;");
        Assert.HasCount(0, FixTestSupport.CompileErrors(Models, Support, Generated, after));
    }

    [TestMethod]
    public void Fix_GivesTheHookTheStubsDocComment_WhenTheOldDeclarationHadNone()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldEmpty);
        FixTestSupport.Write(root, "Validation/OrderValidator.Generated.cs", Generated);

        Assert.IsTrue(new StaleCustomHookRule().Fix(Diag(file), Session(root)));

        var after = File.ReadAllText(file);
        var current = ValidatorTemplate.CustomStub("Shop", "Order");
        var hookDoc = string.Join("\n    ", ValidatorTemplate.CustomHookDocLines);
        StringAssert.Contains(current.Replace("\r\n", "\n"), hookDoc, "the doc lines are the stub's own");
        StringAssert.Contains(after, "    " + hookDoc + "\n    private static partial void ValidateCustom(Order item, List<ValidationError> errors) { }");
        Assert.HasCount(0, FixTestSupport.CompileErrors(Models, Support, Generated, after));
    }

    [TestMethod]
    public void Fix_KeepsADocCommentTheOldDeclarationAlreadyHad()
    {
        var root = FixTestSupport.TempProject();
        var text = OldEmpty.Replace("    partial void", "    /// <summary>My own words.</summary>\n    partial void");
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", text);

        Assert.IsTrue(new StaleCustomHookRule().Fix(Diag(file), Session(root)));

        var after = File.ReadAllText(file);
        StringAssert.Contains(after, "    /// <summary>My own words.</summary>\n    private static partial void ValidateCustom");
        Assert.DoesNotContain("Add rules the generator cannot infer", after);
    }

    [TestMethod]
    public void Fix_UsesTheFilesLineEndingsForTheDocComment()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldEmpty.Replace("\n", "\r\n"));

        Assert.IsTrue(new StaleCustomHookRule().Fix(Diag(file), Session(root)));

        var after = File.ReadAllText(file);
        Assert.AreEqual(after.Count(c => c == '\n'), after.Count(c => c == '\r'), "no bare LF was added");
    }

    [TestMethod]
    public void Fix_NeverOverwritesHandWrittenCode()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldWithCode);
        var bytes = File.ReadAllBytes(file);

        Assert.IsFalse(new StaleCustomHookRule().Fix(Diag(file), Session(root)));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Fix_KeepsEverythingElseInTheFile()
    {
        var root = FixTestSupport.TempProject();
        var text = OldEmpty.Replace("    partial void", "    // my note\n    private static int Helper() => 7;\n\n    partial void");
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", text);

        Assert.IsTrue(new StaleCustomHookRule().Fix(Diag(file), Session(root)));

        var after = File.ReadAllText(file);
        StringAssert.Contains(after, "// my note\n    private static int Helper() => 7;");
        StringAssert.Contains(after, "List<ValidationError> errors)");
        Assert.DoesNotContain("List<string>", after);
    }

    [TestMethod]
    public void Fix_PreservesCrlfAndBom()
    {
        var root = FixTestSupport.TempProject();
        var file = Path.Combine(root, "Validation", "OrderValidator.Custom.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, OldEmpty.Replace("\n", "\r\n"), new UTF8Encoding(true));

        Assert.IsTrue(new StaleCustomHookRule().Fix(Diag(file), Session(root)));

        var bytes = File.ReadAllBytes(file);
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var after = Encoding.UTF8.GetString(bytes);
        Assert.IsFalse(Regex.IsMatch(after, "(?<!\r)\n"), "a bare LF crept in");
        StringAssert.Contains(after, "List<ValidationError>");
    }

    [TestMethod]
    public void Fix_FindsTheCustomFile_FromAnErrorInTheGeneratedHalf()
    {
        var root = FixTestSupport.TempProject();
        var custom = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldEmpty);
        var generated = FixTestSupport.Write(root, "Validation/OrderValidator.Generated.cs", Generated);

        Assert.IsTrue(new StaleCustomHookRule().Fix(Diag(generated, "CS8795"), Session(root)));
        StringAssert.Contains(File.ReadAllText(custom), "private static partial void ValidateCustom");
        Assert.AreEqual(Generated, File.ReadAllText(generated));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenTheCustomFileIsAbsent()
    {
        var root = FixTestSupport.TempProject();
        var generated = FixTestSupport.Write(root, "Validation/OrderValidator.Generated.cs", Generated);

        Assert.IsFalse(new StaleCustomHookRule().Fix(Diag(generated, "CS8795"), Session(root)));
        Assert.IsFalse(File.Exists(Path.Combine(root, "Validation", "OrderValidator.Custom.cs")));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAFileThatIsNotAValidatorHalf()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", OldEmpty);

        Assert.IsFalse(new StaleCustomHookRule().Fix(Diag(file), Session(root)));
        Assert.AreEqual(OldEmpty, File.ReadAllText(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForACurrentStub()
    {
        var root = FixTestSupport.TempProject();
        var current = ValidatorTemplate.CustomStub("Shop", "Order");
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", current);
        var bytes = File.ReadAllBytes(file);

        Assert.IsFalse(new StaleCustomHookRule().Fix(Diag(file), Session(root)));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAnotherCode()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldEmpty);

        Assert.IsFalse(new StaleCustomHookRule().Fix(Diag(file, "CS0103"), Session(root)));
        Assert.AreEqual(OldEmpty, File.ReadAllText(file));
    }

    [TestMethod]
    public void Fix_Refuses_AFileOutsideTheProject()
    {
        var root = FixTestSupport.TempProject();
        var outside = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(outside, "OrderValidator.Custom.cs", OldEmpty);

        Assert.ThrowsExactly<InvalidOperationException>(() => new StaleCustomHookRule().Fix(Diag(file), Session(root)));
        Assert.AreEqual(OldEmpty, File.ReadAllText(file));
    }
}

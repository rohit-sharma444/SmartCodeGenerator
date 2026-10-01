using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class RemovePropertyCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_removeprop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private bool Run(string input)
    {
        ProjectMetaData? project = null;
        return CommandFront.Run(input, ref project, [], _workspace, _ => { });
    }

    private string WriteBook(string body)
    {
        var file = Path.Combine(_workspace, "Book.cs");
        File.WriteAllText(file, body);
        return file;
    }

    private static void AssertCompiles(string code)
    {
        var result = Compiler.Compile(code);
        Assert.IsTrue(result.Success,
            "Edited file does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void Keeps_the_next_members_indentation()
    {
        // The layout `add property` and `add method` write.
        var file = WriteBook(
            "namespace Shop\r\n{\r\n    public class Book\r\n    {\r\n"
            + "        public string Pages { get; set; }\r\n"
            + "\r\n"
            + "        public void Store()\r\n        {\r\n        }\r\n    }\r\n}");

        Assert.IsTrue(Run("remove property Pages from Book"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "\r\n        public void Store()", code);
        Assert.DoesNotContain("Pages", code, code);
    }

    [TestMethod]
    public void Removes_the_initial_value_with_it()
    {
        var file = WriteBook(
            "namespace Shop { public class Book { public string Pages { get; set; } = \"\";\n"
            + "  public int Id { get; set; } } }");

        Assert.IsTrue(Run("remove property Pages from Book"));

        var code = File.ReadAllText(file);
        Assert.DoesNotContain("Pages", code, code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Leaves_a_same_named_property_on_another_class_alone()
    {
        var file = WriteBook(
            "namespace Shop { public class Other { public string Pages { get; set; } }\n"
            + "public class Book { public string Pages { get; set; } } }");

        Assert.IsTrue(Run("remove property Pages from Book"));

        var code = File.ReadAllText(file);
        Assert.HasCount(1, System.Text.RegularExpressions.Regex.Matches(code, @"\bPages\b"), code);
        StringAssert.Contains(code, "class Other { public string Pages");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Keeps_an_endregion_above_the_property()
    {
        var file = WriteBook(
            "namespace Shop\r\n{\r\n    public class Book\r\n    {\r\n"
            + "        #region Data\r\n"
            + "        public int Alpha { get; set; }\r\n"
            + "        #endregion\r\n"
            + "        public string Pages { get; set; } = \"\";\r\n"
            + "    }\r\n}\r\n");

        Assert.IsTrue(Run("remove property Pages from Book"));

        var code = File.ReadAllText(file);
        Assert.DoesNotContain("Pages", code, code);
        StringAssert.Contains(code, "        public int Alpha { get; set; }\r\n        #endregion\r\n    }\r\n", code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Takes_the_properties_own_doc_comment_with_it()
    {
        var file = WriteBook(
            "namespace Shop\r\n{\r\n    public class Book\r\n    {\r\n"
            + "        public int Alpha { get; set; }\r\n"
            + "\r\n"
            + "        /// <summary>Page count.</summary>\r\n"
            + "        public int Pages { get; set; }\r\n"
            + "\r\n"
            + "        public int Beta { get; set; }\r\n"
            + "    }\r\n}\r\n");

        Assert.IsTrue(Run("remove property Pages from Book"));

        var code = File.ReadAllText(file);
        Assert.DoesNotContain("Page count", code, code);
        StringAssert.Contains(code, "        public int Alpha { get; set; }\r\n\r\n        public int Beta { get; set; }\r\n", code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Reports_failure_when_the_property_is_missing()
    {
        WriteBook("namespace Shop { public class Book { public int Id { get; set; } } }");

        Assert.IsFalse(Run("remove property Pages from Book"));
    }
}

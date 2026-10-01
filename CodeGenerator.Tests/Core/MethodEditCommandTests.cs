using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class MethodEditCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_methodedit_" + Guid.NewGuid().ToString("N"));
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

    private static int Count(string code, string word) =>
        System.Text.RegularExpressions.Regex.Matches(code, $@"\b{word}\b").Count;

    // ----- delete ---------------------------------------------------------

    [TestMethod]
    [DataRow("remove method Save from Book")]
    [DataRow("delete method Save from Book")]
    [DataRow("delete method Save in Book")]
    public void Parses_a_method_delete(string input)
    {
        var command = CommandFront.Parse(input, _workspace);

        Assert.AreEqual(IntentType.DeleteMethod, command.Intent);
        Assert.AreEqual("Save", command.MethodName);
        Assert.AreEqual("Book", command.ClassName);
    }

    [TestMethod]
    public void Deletes_the_method_and_still_compiles()
    {
        var file = WriteBook(
            "namespace Shop { public class Book { public int Pages { get; set; }\n"
            + "  public void Save() { }\n"
            + "  public int Count() { return Pages; } } }");

        Assert.IsTrue(Run("remove method Save from Book"));

        var code = File.ReadAllText(file);
        Assert.AreEqual(0, Count(code, "Save"), code);
        StringAssert.Contains(code, "Count()");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Refuses_to_guess_between_overloads()
    {
        var file = WriteBook(
            "namespace Shop { public class Book {\n"
            + "  public void Save() { }\n"
            + "  public void Save(string path) { } } }");
        var before = File.ReadAllText(file);

        Assert.IsFalse(Run("remove method Save from Book"));

        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    public void Leaves_a_same_named_method_on_another_class_alone()
    {
        var file = WriteBook(
            "namespace Shop { public class Other { public void Save() { } }\n"
            + "public class Book { public void Save() { } } }");

        Assert.IsTrue(Run("remove method Save from Book"));

        var code = File.ReadAllText(file);
        Assert.AreEqual(1, Count(code, "Save"), code);
        StringAssert.Contains(code, "class Other");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Delete_keeps_an_endregion_above_the_method()
    {
        // The directive sits in the method's leading trivia; removing that trivia with it
        // left the #region unclosed (CS1038).
        var file = WriteBook(
            "namespace Shop\r\n{\r\n    public class Book\r\n    {\r\n"
            + "        #region Data\r\n"
            + "        public int Alpha { get; set; }\r\n"
            + "        #endregion\r\n"
            + "        public void Run() { }\r\n"
            + "    }\r\n}\r\n");

        Assert.IsTrue(Run("remove method Run from Book"));

        var code = File.ReadAllText(file);
        Assert.AreEqual(0, Count(code, "Run"), code);
        StringAssert.Contains(code, "        public int Alpha { get; set; }\r\n        #endregion\r\n    }\r\n", code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Delete_keeps_a_section_comment_but_takes_the_methods_own_doc_comment()
    {
        var file = WriteBook(
            "namespace Shop\r\n{\r\n    public class Book\r\n    {\r\n"
            + "        public int Alpha { get; set; }\r\n"
            + "\r\n"
            + "        // ---- persistence ----\r\n"
            + "\r\n"
            + "        /// <summary>Writes the book.</summary>\r\n"
            + "        public void Save() { }\r\n"
            + "\r\n"
            + "        public void Load() { }\r\n"
            + "    }\r\n}\r\n");

        Assert.IsTrue(Run("remove method Save from Book"));

        var code = File.ReadAllText(file);
        Assert.DoesNotContain("Writes the book", code, code);
        StringAssert.Contains(code,
            "        public int Alpha { get; set; }\r\n\r\n        // ---- persistence ----\r\n\r\n        public void Load() { }\r\n",
            code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Delete_reports_failure_when_the_method_is_missing()
    {
        WriteBook("namespace Shop { public class Book { public void Load() { } } }");

        Assert.IsFalse(Run("remove method Save from Book"));
    }

    // ----- rename ---------------------------------------------------------

    [TestMethod]
    [DataRow("rename method Save to Store in Book")]
    [DataRow("rename method Save to Store on Book")]
    public void Parses_a_method_rename(string input)
    {
        var command = CommandFront.Parse(input, _workspace);

        Assert.AreEqual(IntentType.RenameMethod, command.Intent);
        Assert.AreEqual("Save", command.MethodName);
        Assert.AreEqual("Store", command.NewMethodName);
        Assert.AreEqual("Book", command.ClassName);
    }

    [TestMethod]
    public void Renames_every_overload_and_still_compiles()
    {
        var file = WriteBook(
            "namespace Shop { public class Book {\n"
            + "  public void Save() { }\n"
            + "  public void Save(string path) { } } }");

        Assert.IsTrue(Run("rename method Save to Store in Book"));

        var code = File.ReadAllText(file);
        Assert.AreEqual(0, Count(code, "Save"), code);
        Assert.AreEqual(2, Count(code, "Store"), code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Rename_refuses_a_name_already_taken_by_a_method()
    {
        var file = WriteBook(
            "namespace Shop { public class Book { public void Save() { } public void Store() { } } }");
        var before = File.ReadAllText(file);

        Assert.IsFalse(Run("rename method Save to Store in Book"));

        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    public void Rename_reports_failure_when_the_method_is_missing()
    {
        WriteBook("namespace Shop { public class Book { public void Load() { } } }");

        Assert.IsFalse(Run("rename method Save to Store in Book"));
    }
}

using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class RetypePropertyCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_retype_" + Guid.NewGuid().ToString("N"));
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
    [DataRow("change type of Pages to int in Book", "int")]
    [DataRow("change the type of property Pages to int in Book", "int")]
    [DataRow("change type of field Pages to decimal? on Book", "decimal?")]
    [DataRow("retype property Pages to List<string> in Book", "List<string>")]
    [DataRow("retype field Pages to int of Book", "int")]
    public void Parses_a_retype(string input, string type)
    {
        var command = CommandFront.Parse(input, _workspace);

        Assert.AreEqual(IntentType.ChangePropertyType, command.Intent);
        Assert.AreEqual("Pages", command.PropertyName);
        Assert.AreEqual(type, command.PropertyType);
        Assert.AreEqual("Book", command.ClassName);
    }

    [TestMethod]
    public void Changes_the_type_and_still_compiles()
    {
        var file = WriteBook(
            "namespace Shop { public class Book { public string Pages { get; set; }\n"
            + "  public string Title { get; set; } } }");

        Assert.IsTrue(Run("change type of Pages to int in Book"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "public int Pages");
        StringAssert.Contains(code, "public string Title");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Drops_an_initial_value_the_new_type_cannot_hold()
    {
        var file = WriteBook(
            "namespace Shop { public class Book { public string Pages { get; set; } = \"\"; } }");

        Assert.IsTrue(Run("change type of Pages to int in Book"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "public int Pages");
        Assert.DoesNotContain("\"\"", code, code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Refuses_a_type_that_is_not_csharp()
    {
        var file = WriteBook("namespace Shop { public class Book { public string Pages { get; set; } } }");
        var before = File.ReadAllText(file);

        Assert.IsFalse(Run("change type of Pages to in<t in Book"));

        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    public void Reports_failure_when_the_property_is_missing()
    {
        WriteBook("namespace Shop { public class Book { public string Title { get; set; } } }");

        Assert.IsFalse(Run("change type of Pages to int in Book"));
    }
}

using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Member names in <c>create class ... with properties ...</c> and <c>add property ...</c> are
/// the names the user typed.
/// </summary>
/// <remarks>
/// Both read the normalised sentence, which rewrites new -> create, manager -> service and
/// entity -> class and drops stop words and punctuation: a property typed New was written as
/// Create, and "add property Y string to A" lost the class A as an article and then reported
/// "Property '' already exists."
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class MemberListParseTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_memberlist_" + Guid.NewGuid().ToString("N"));
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

    private string Source(string className) =>
        File.ReadAllText(Directory.GetFiles(_workspace, className + ".cs", SearchOption.AllDirectories).Single());

    private static void AssertCompiles(string code)
    {
        var result = Compiler.Compile(code);
        Assert.IsTrue(result.Success,
            "File does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    private static string[] Names(ParsedCommand command) => [.. command.Properties.Select(p => p.Name)];

    // ----- create class with properties -----------------------------------

    [TestMethod]
    [DataRow("create class Foo with properties New string, Delete bool", "New,Delete")]
    [DataRow("create class Bar with properties new string, manager string, entity int", "New,Manager,Entity")]
    [DataRow("create class Baz with properties model string, field int", "Model,Field")]
    [DataRow("create class Qux with properties name string, price decimal.", "Name,Price")]
    public void Property_names_are_kept_as_typed(string sentence, string expected)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.CreateClass, command.Intent);
        CollectionAssert.AreEqual(expected.Split(','), Names(command), sentence);
    }

    // The member list is read as typed, so "functions" is no longer rewritten to "methods" for it.
    [TestMethod]
    [DataRow("create class Shop with properties Name string and methods Save")]
    [DataRow("create class Shop with properties Name string and functions Save")]
    public void Methods_are_read_under_either_label(string sentence)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        CollectionAssert.AreEqual(new[] { "Name" }, Names(command), sentence);
        CollectionAssert.AreEqual(new[] { "Save" }, command.Methods.Select(m => m.Name).ToArray(), sentence);
    }

    [TestMethod]
    public void A_class_with_keyword_like_property_names_compiles()
    {
        Assert.IsTrue(Run("create class Bar with properties new string, manager string, entity int"));

        var code = Source("Bar");
        StringAssert.Contains(code, "public string New");
        StringAssert.Contains(code, "public string Manager");
        StringAssert.Contains(code, "public int Entity");
        AssertCompiles(code);
    }

    // The ignored-entry report splits the list on commas, which the normaliser had removed, so on
    // the real command path nothing was ever reported.
    [TestMethod]
    public void A_non_primitive_entry_is_reported_from_the_command_line()
    {
        var command = CommandFront.Parse(
            "create class Order with properties CustomerId int, Customer Customer", _workspace);

        CollectionAssert.AreEqual(new[] { "CustomerId" }, Names(command));
        Assert.HasCount(1, command.IgnoredProperties);
        Assert.AreEqual("Customer Customer", command.IgnoredProperties[0]);
    }
}

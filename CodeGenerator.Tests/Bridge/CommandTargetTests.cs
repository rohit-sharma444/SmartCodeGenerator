using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// A command acts on the class the user named, and says so when it acts on another.
/// </summary>
/// <remarks>
/// "delete the Ids property from Item" answered "Class 'Foo' not found.": the delete pattern
/// wanted "delete property Ids", missed, and the class came silently from an earlier command.
/// "remove property Name from Item and add property Title string to Item" read the add half as
/// the property to remove, and said "Property 'Title' not found in 'Item'."
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class CommandTargetTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_target_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_workspace, "Shop"));
        File.WriteAllText(Path.Combine(_workspace, "Shop", "Item.cs"),
            "namespace Shop;\npublic class Item\n{\n    public string Name { get; set; } = string.Empty;\n    public int Ids { get; set; }\n}\n");
        File.WriteAllText(Path.Combine(_workspace, "Shop", "Foo.cs"),
            "namespace Shop;\npublic class Foo\n{\n    public string Name { get; set; } = string.Empty;\n}\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private (CommandFront.RunResult Result, string Output) Run(string input)
    {
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            ProjectMetaData? project = null;
            var result = CommandFront.RunDetailed(input, ref project, [], _workspace, _ => { });
            return (result, output.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private string Item => File.ReadAllText(Path.Combine(_workspace, "Shop", "Item.cs"));

    [TestMethod]
    [DataRow("delete the Ids property from Item")]
    [DataRow("remove the Ids property from the Item class")]
    [DataRow("delete Ids property from Item")]
    public void The_property_before_the_word_property_is_read(string sentence)
    {
        SessionContext.CurrentClass = "Foo";

        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.DeleteProperty, command.Intent, sentence);
        Assert.AreEqual("Ids", command.PropertyName, sentence);
        Assert.AreEqual("Item", command.ClassName, sentence);
    }

    [TestMethod]
    [DataRow("remove property Name from Item and add property Title string to Item")]
    [DataRow("add property Title string to Item, then delete property Name from Item")]
    [DataRow("delete property Name from Item and then create class Order")]
    public void Two_commands_on_one_line_are_refused_and_nothing_changes(string sentence)
    {
        var before = Item;

        var (result, output) = Run(sentence);

        Assert.IsFalse(result.Handled, output);
        Assert.IsTrue(result.Recognized, output);
        StringAssert.Contains(output, "two commands", output);
        Assert.AreEqual(before, Item);
    }

    // Lines with "and" that are one command.
    [TestMethod]
    [DataRow("create class Shop with properties Name string and methods Save")]
    [DataRow("create class Shop with properties Name string and Age int")]
    public void One_command_with_and_in_it_is_not_refused(string sentence)
    {
        var (result, output) = Run(sentence);

        Assert.IsTrue(result.Handled, output);
        Assert.DoesNotContain("two commands", output);
    }

    [TestMethod]
    public void A_class_taken_from_the_session_is_named()
    {
        SessionContext.CurrentClass = "Foo";

        var (result, output) = Run("rename property Name to Title");

        Assert.IsTrue(result.Handled, output);
        StringAssert.Contains(output, "Using the current class 'Foo'.");
    }

    // The parser fills the session class into every command, a project command included.
    [TestMethod]
    [DataRow("validate project")]
    [DataRow("list classes")]
    public void A_command_that_acts_on_no_class_gets_no_note(string sentence)
    {
        SessionContext.CurrentClass = "Foo";

        var (_, output) = Run(sentence);

        Assert.DoesNotContain("Using the current class", output);
    }

    [TestMethod]
    public void A_class_the_user_named_gets_no_note()
    {
        SessionContext.CurrentClass = "Foo";

        var (result, output) = Run("rename property Name to Title in Item");

        Assert.IsTrue(result.Handled, output);
        Assert.DoesNotContain("Using the current class", output);
    }

    [TestMethod]
    public void A_bare_delete_class_does_not_delete_the_current_class()
    {
        SessionContext.CurrentClass = "Foo";

        var (result, output) = Run("delete class");

        Assert.IsFalse(result.Handled, output);
        Assert.IsTrue(File.Exists(Path.Combine(_workspace, "Shop", "Foo.cs")), output);
    }
}

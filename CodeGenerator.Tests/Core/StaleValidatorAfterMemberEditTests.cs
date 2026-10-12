using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// A validator generated earlier reads the members as they were. Removing, renaming or retyping a
/// property left <c>Item.Validation.cs</c> reading <c>this.Price</c> (CS1061) and a built app's
/// <c>BookValidator.Generated.cs</c> reading <c>item.Isbn is { Length: &gt; 200 }</c> on a long
/// (CS0117), with nothing said.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class StaleValidatorAfterMemberEditTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        ValidationRegistry.Clear();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_stalevalid_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ValidationRegistry.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private string Run(string input)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            ProjectMetaData? project = null;
            CommandFront.Run(input, ref project, [], _workspace, _ => { });
        }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    // In a project folder, as create console project puts it: at the workspace root a class typed
    // in another case is resolved to its own name before the command runs, and the live defect hid.
    private string ItemFolder => Directory.CreateDirectory(Path.Combine(_workspace, "P")).FullName;

    private string ItemWithValidator(params string[] rules)
    {
        var file = Path.Combine(ItemFolder, "Item.cs");
        File.WriteAllText(file,
            "namespace Shop\r\n{\r\n    public class Item\r\n    {\r\n"
            + "        public string Name { get; set; }\r\n"
            + "        public decimal Price { get; set; }\r\n"
            + "    }\r\n}\r\n");

        foreach (var text in rules)
        {
            var ctx = new ValidationParseContext { Fragment = text, ClassName = "Item" };
            foreach (var rule in ValidationRuleCatalog.Default.Parse(text, ValidationCategory.Data, ctx))
                ValidationRegistry.Add("Item", rule);
        }

        Assert.IsGreaterThan(0, ValidationRegistry.Get("Item").Count);
        Run("generate validator for Item");
        var validator = Path.Combine(ItemFolder, "Item.Validation.cs");
        Assert.IsTrue(File.Exists(validator), "generate validator wrote nothing");
        AssertBothCompile();
        return validator;
    }

    private void AssertBothCompile()
    {
        var classFile = Path.Combine(ItemFolder, "Item.cs");
        var validator = Path.Combine(ItemFolder, "Item.Validation.cs");
        var result = Compiler.CompileSources(
        [
            (classFile, File.ReadAllText(classFile)),
            (validator, File.ReadAllText(validator))
        ]);
        Assert.IsTrue(result.Success,
            File.ReadAllText(validator) + "\n" + string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void Remove_property_regenerates_the_validator()
    {
        ItemWithValidator("Price must be positive", "Name is required");

        var output = Run("remove property Price from Item");

        AssertBothCompile();
        StringAssert.Contains(output, "Item.Validation.cs", output);
    }

    // Gaps 5 #5: remove validation left Item.Validation.cs as it was, so the removed rule was still
    // enforced until generate validator was run again.
    [TestMethod]
    [DataRow("remove validation from Item.Price")]
    [DataRow("remove validation 1 from Item")]
    public void Remove_validation_regenerates_the_validator(string command)
    {
        ItemWithValidator("Price must be positive");
        CollectionAssert.AreEqual(new[] { 1 }, new[] { ErrorsForNegativePrice() }, "the rule is not enforced to begin with");

        var output = Run(command);

        StringAssert.Contains(output, "Item.Validation.cs", output);
        AssertBothCompile();
        Assert.AreEqual(0, ErrorsForNegativePrice(), File.ReadAllText(Path.Combine(ItemFolder, "Item.Validation.cs")));
    }

    // How many messages Validate() returns for an Item with Price -1, compiled and run.
    private int ErrorsForNegativePrice()
    {
        var classFile = Path.Combine(ItemFolder, "Item.cs");
        var validator = Path.Combine(ItemFolder, "Item.Validation.cs");
        var result = Compiler.CompileSources(
        [
            (classFile, File.ReadAllText(classFile)),
            (validator, File.ReadAllText(validator))
        ]);
        Assert.IsTrue(result.Success);
        var type = System.Reflection.Assembly.Load(result.AssemblyBytes!).GetType("Shop.Item")!;
        var item = Activator.CreateInstance(type)!;
        type.GetProperty("Name")!.SetValue(item, "x");
        type.GetProperty("Price")!.SetValue(item, -1m);
        return ((System.Collections.Generic.List<string>)type.GetMethod("Validate")!.Invoke(item, null)!).Count;
    }

    [TestMethod]
    public void Remove_the_last_ruled_property_still_leaves_a_validator_that_compiles()
    {
        ItemWithValidator("Price must be positive");

        Run("remove property Price from Item");

        AssertBothCompile();
    }

    [TestMethod]
    public void Rename_property_regenerates_the_validator()
    {
        ItemWithValidator("Price must be positive");

        Run("rename property Price to Cost in Item");

        AssertBothCompile();
    }

    [TestMethod]
    public void Change_type_regenerates_the_validator()
    {
        ItemWithValidator("Name must be at most 20 characters");

        Run("change type of Name to int in Item");

        AssertBothCompile();
    }

    [TestMethod]
    public void A_class_typed_in_another_case_is_regenerated_under_its_own_name()
    {
        // The live run wrote `public partial class item` beside `class Item`.
        ItemWithValidator("Price must be positive");

        Run("change type of name to int in item");

        StringAssert.Contains(File.ReadAllText(Path.Combine(ItemFolder, "Item.Validation.cs")), "partial class Item");
        AssertBothCompile();
    }

    [TestMethod]
    public void Remove_property_does_not_blame_the_validator_it_regenerates()
    {
        // The users-of warning scanned the stale validator before it was rewritten.
        ItemWithValidator("Price must be positive");

        var output = Run("remove property Price from Item");

        Assert.DoesNotContain("Item.Validate()", output, output);
    }

    [TestMethod]
    public void Change_type_does_not_blame_the_validator_it_regenerates()
    {
        ItemWithValidator("Price must be positive");

        var output = Run("change type of Price to double in Item");

        Assert.DoesNotContain("Item.Validate()", output, output);
        AssertBothCompile();
    }

    [TestMethod]
    public void A_hand_written_validation_file_is_left_alone()
    {
        var file = Path.Combine(ItemFolder, "Item.cs");
        File.WriteAllText(file, "namespace Shop { public partial class Item { public decimal Price { get; set; } } }");
        var mine = Path.Combine(ItemFolder, "Item.Validation.cs");
        const string handWritten = "namespace Shop { public partial class Item { public bool Check() => true; } }";
        File.WriteAllText(mine, handWritten);

        Run("remove property Price from Item");

        Assert.AreEqual(handWritten, File.ReadAllText(mine));
    }

    private void BuiltAppBook()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "Models"));
        Directory.CreateDirectory(Path.Combine(_workspace, "Validation"));
        File.WriteAllText(Path.Combine(_workspace, "Models", "Book.cs"),
            "namespace Lib.Models { public class Book { public int Id { get; set; } "
            + "public string Isbn { get; set; } = \"\"; public string Title { get; set; } = \"\"; } }");
        File.WriteAllText(Path.Combine(_workspace, "Validation", "BookValidator.Generated.cs"),
            "namespace Lib.Validation { public sealed partial class BookValidator { "
            + "private static void DataStage(Lib.Models.Book item, System.Collections.Generic.List<string> errors) { "
            + "if (item.Isbn is { Length: > 200 }) errors.Add(\"Isbn\"); "
            + "if (item.Title is { Length: > 100 }) errors.Add(\"Title\"); } } }");
    }

    [TestMethod]
    public void Change_type_names_a_built_apps_validator_that_checks_it()
    {
        BuiltAppBook();

        var output = Run("change type of Isbn to long in Book");

        StringAssert.Contains(output, "BookValidator.Generated.cs", output);
    }

    [TestMethod]
    public void Remove_property_names_a_built_apps_validator_that_checks_it()
    {
        BuiltAppBook();

        var output = Run("remove property Title from Book");

        StringAssert.Contains(output, "BookValidator.Generated.cs", output);
    }

    [TestMethod]
    public void Rename_property_names_a_built_apps_validator_that_checks_it()
    {
        BuiltAppBook();

        var output = Run("rename property Title to Name in Book");

        StringAssert.Contains(output, "BookValidator.Generated.cs", output);
    }

    [TestMethod]
    public void A_built_apps_validator_warning_spells_names_as_the_app_does()
    {
        // The live run said "still checks bookitem.isbn" for a command typed in lower case.
        BuiltAppBook();

        var output = Run("change type of isbn to long in book");

        StringAssert.Contains(output, "checks Book.Isbn as it was", output);
    }

    [TestMethod]
    public void A_built_apps_validator_is_named_once_not_twice()
    {
        // The live run named it in its own warning and again as BookItemValidator.Field() in the
        // users-of warning below it.
        BuiltAppBook();

        var output = Run("remove property Title from Book");

        Assert.DoesNotContain("BookValidator.DataStage()", output, output);
    }

    [TestMethod]
    public void A_built_apps_validator_that_does_not_check_it_is_not_named()
    {
        BuiltAppBook();

        var output = Run("change type of Id to long in Book");

        Assert.DoesNotContain("BookValidator.Generated.cs", output, output);
    }
}

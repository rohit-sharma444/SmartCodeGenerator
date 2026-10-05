using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class RenamePropertyCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_renameprop_" + Guid.NewGuid().ToString("N"));
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

    private string WriteProduct(string body)
    {
        var file = Path.Combine(_workspace, "Product.cs");
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
    public void Parses_the_command_into_old_new_and_class()
    {
        var command = CommandFront.Parse("rename property Sku to Code in Product", _workspace);

        Assert.AreEqual(IntentType.RenameProperty, command.Intent);
        Assert.AreEqual("Sku", command.PropertyName);
        Assert.AreEqual("Code", command.NewPropertyName);
        Assert.AreEqual("Product", command.ClassName);
    }

    [TestMethod]
    public void Renames_the_declaration_and_still_compiles()
    {
        var file = WriteProduct(
            "namespace Shop { public class Product { public string Sku { get; set; } = \"\"; } }");

        Assert.IsTrue(Run("rename property Sku to Code in Product"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "Code");
        Assert.DoesNotContain("Sku", code, "The old name should be gone: " + code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Leaves_a_same_named_property_on_another_class_alone()
    {
        var file = WriteProduct(
            "namespace Shop { public class Other { public string Sku { get; set; } = \"\"; } "
            + "public class Product { public string Sku { get; set; } = \"\"; } }");

        Assert.IsTrue(Run("rename property Sku to Code in Product"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Other");
        StringAssert.Contains(code, "Code");
        // Other.Sku survives, so exactly one "Sku" remains.
        Assert.HasCount(1, System.Text.RegularExpressions.Regex.Matches(code, @"\bSku\b"), code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Reports_failure_when_the_property_is_missing()
    {
        WriteProduct("namespace Shop { public class Product { public int Id { get; set; } } }");

        Assert.IsFalse(Run("rename property Sku to Code in Product"));
    }

    [TestMethod]
    public void Reports_failure_when_the_new_name_is_taken()
    {
        WriteProduct(
            "namespace Shop { public class Product { public string Sku { get; set; } = \"\"; "
            + "public string Code { get; set; } = \"\"; } }");

        Assert.IsFalse(Run("rename property Sku to Code in Product"));
    }

    [TestMethod]
    [DataRow("Code")]
    [DataRow("Product")]
    public void Reports_failure_when_the_new_name_is_a_method_or_the_class(string newName)
    {
        var file = WriteProduct(
            "namespace Shop { public class Product { public string Sku { get; set; } = \"\"; "
            + "public string Code() { return \"\"; } } }");
        var before = File.ReadAllText(file);

        Assert.IsFalse(Run($"rename property Sku to {newName} in Product"));

        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    [DataRow("9x")]
    [DataRow("class")]
    public void Refuses_a_new_name_that_is_not_an_identifier(string newName)
    {
        var file = WriteProduct("namespace Shop { public class Product { public string Sku { get; set; } = \"\"; } }");
        var before = File.ReadAllText(file);

        Assert.IsFalse(Run($"rename property Sku to {newName} in Product"));

        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    [DataRow("9x")]
    [DataRow("class")]
    public void Rename_class_refuses_a_new_name_that_is_not_an_identifier(string newName)
    {
        var file = WriteProduct("namespace Shop { public class Product { } }");
        var before = File.ReadAllText(file);

        Assert.IsFalse(Run($"rename class Product to {newName}"));

        Assert.IsTrue(File.Exists(file), "the class file was moved");
        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    public void Reports_failure_when_the_class_is_missing()
    {
        Assert.IsFalse(Run("rename property Sku to Code in Ghost"));
    }
}

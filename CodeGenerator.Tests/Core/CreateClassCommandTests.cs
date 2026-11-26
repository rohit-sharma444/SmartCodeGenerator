using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary><c>create class</c> run as typed, through <see cref="CommandFront"/>, into a workspace
/// with no project: the class lands in <c>Generated</c>.</summary>
[TestClass]
[DoNotParallelize]
public sealed class CreateClassCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_createclass_" + Guid.NewGuid().ToString("N"));
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

    private string GeneratedDir => Path.Combine(_workspace, "Generated");

    private string[] ClassFilesWritten() =>
        [.. Directory.GetFiles(GeneratedDir, "*.cs").Select(Path.GetFileName).Order()!];

    // The class names the file declares, read from the compiled tree.
    private static string[] AssertCompilesAndDeclares(string file, out string[] properties)
    {
        var code = File.ReadAllText(file);
        var result = Compiler.CompileSources([(file, code)]);
        Assert.IsTrue(result.Success,
            "Class file does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));

        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        properties = [.. root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Select(p => p.Identifier.Text)];
        return [.. root.DescendantNodes().OfType<ClassDeclarationSyntax>().Select(c => c.Identifier.Text)];
    }

    [TestMethod]
    // "... and add method Ship to Order" wrote Order without Ship and said the method was ignored.
    public void And_add_method_to_the_same_class_writes_the_method()
    {
        var (handled, output) = RunCapturing("create class Order with properties Total decimal and add method Ship to Order");

        Assert.IsTrue(handled, output);
        Assert.DoesNotContain("Ignored", output);
        var file = Path.Combine(GeneratedDir, "Order.cs");
        AssertCompilesAndDeclares(file, out var properties);
        CollectionAssert.AreEqual(new[] { "Total" }, properties);
        StringAssert.Contains(File.ReadAllText(file), "void Ship()");
    }

    // It wrote Cart without Ship and then said to create Customer first. The method is for
    // another class, so the line holds two commands: refused whole, nothing written.
    [TestMethod]
    [DataRow("create class Cart with properties Total decimal and add method Ship to Customer")]
    [DataRow("create class Cart and add method Ship to the class Customer")]
    [DataRow("create class Cart and add method Ship for Customer")]
    public void And_add_method_to_another_class_is_refused_and_nothing_written(string line)
    {
        var (handled, output) = RunCapturing(line);

        Assert.IsFalse(handled, output);
        StringAssert.Contains(output, "This line holds two commands");
        StringAssert.Contains(output, "add method Ship");
        Assert.DoesNotContain("create it first", output);
        Assert.IsFalse(Directory.Exists(GeneratedDir) && Directory.GetFiles(GeneratedDir, "*.cs").Length > 0, output);
    }

    // Only the END of the line was checked for another class, so a member for another class in the
    // middle went onto the new class: Size was written on Kart with "Ignored 'to Shape'".
    [TestMethod]
    [DataRow("create class Kart and add property Size int to Shape and add method Roll",
        "create class Kart and add method Roll|add property Size int to Shape")]
    [DataRow("create class Kart and add method Roll to Shape and add property Size int",
        "create class Kart and add property Size int|add method Roll to Shape")]
    [DataRow("create class Kart and add property Size int to Shape and add method Roll to Shape",
        "create class Kart|add property Size int to Shape|add method Roll to Shape")]
    [DataRow("create class Kart and add property Size int and remove property Name from Shape",
        "create class Kart and add property Size int|remove property Name from Shape")]
    [DataRow("create class Kart and add required property Size int to Shape and add method Roll",
        "create class Kart and add method Roll|add required property Size int to Shape")]
    [DataRow("create class Kart and add property Size int to Shape and add function Roll",
        "create class Kart and add function Roll|add property Size int to Shape")]
    [DataRow("create class Kart and add property Size int to Shape and generate method Roll",
        "create class Kart and generate method Roll|add property Size int to Shape")]
    [DataRow("create class Kart and add function Roll to Shape and add property Size int",
        "create class Kart and add property Size int|add function Roll to Shape")]
    [DataRow("create class Kart and add property Size int to the Shape class and add method Roll",
        "create class Kart and add method Roll|add property Size int to the Shape class")]
    [DataRow("create class Kart and add property Size int into Shape and add method Roll",
        "create class Kart and add method Roll|add property Size int into Shape")]
    [DataRow("create class Kart, and add property Size int to Shape",
        "create class Kart|add property Size int to Shape")]
    [DataRow("create class Kart and add property Note string default \"x, add property y\" and add property Size int to Shape",
        "create class Kart and add property Note string default \"x, add property y\"|add property Size int to Shape")]
    public void A_member_for_another_class_in_the_middle_is_refused_and_nothing_written(string line, string expected)
    {
        CollectionAssert.AreEqual(expected.Split('|'), CommandFront.TwoCommands(line)?.ToArray(), line);

        var (handled, output) = RunCapturing(line);

        Assert.IsFalse(handled, output);
        StringAssert.Contains(output, expected.Split('|').Length == 3 ? "This line holds three commands" : "This line holds two commands");
        foreach (var part in expected.Split('|'))
            StringAssert.Contains(output, part, output);
        Assert.IsFalse(Directory.Exists(GeneratedDir) && Directory.GetFiles(GeneratedDir, "*.cs").Length > 0, output);
    }

    // A line that creates nothing was split once, at its first second command, and a second command
    // with a modifier was not seen at all, so the parser read one half.
    [TestMethod]
    [DataRow("remove property Name from Item and add property Title string to Item and rename property Code to Sku in Item",
        "remove property Name from Item|add property Title string to Item|rename property Code to Sku in Item")]
    [DataRow("remove property X from Item and add required property Y int to Shape",
        "remove property X from Item|add required property Y int to Shape")]
    // A create later in the line keeps its own members: "add method Roll" listed alone would go onto
    // the current class if typed.
    [DataRow("remove property X from Item and create class Kart and add method Roll",
        "remove property X from Item|create class Kart and add method Roll")]
    public void Every_command_in_a_line_that_creates_nothing_is_listed(string line, string expected) =>
        CollectionAssert.AreEqual(expected.Split('|'), CommandFront.TwoCommands(line)?.ToArray(), line);

    [TestMethod]
    [DataRow("create class Kart and add property Size int to Kart and add method Roll")]
    [DataRow("create class Kart and add property Size int to the Kart class and add function Roll")]
    [DataRow("create class Kart and add property Note string default \"a, add property b to Shape\"")]
    [DataRow("create class Kart and add required property Size int and add method Roll to Kart")]
    [DataRow("create class Kart : Shape and add property Size int and add property Label string")]
    public void Every_member_for_the_new_class_is_one_command(string line) =>
        Assert.IsNull(CommandFront.TwoCommands(line), line);

    [TestMethod]
    [DataRow("create class Cart with properties Total decimal and add method Ship to Cart")]
    [DataRow("create class cart with properties Total decimal and add method Ship to Cart")]
    [DataRow("create class Cart with properties Total decimal and add method Ship")]
    public void And_add_method_to_the_class_itself_or_no_class_is_one_command(string line)
    {
        Assert.IsNull(CommandFront.TwoCommands(line), line);
        var (handled, output) = RunCapturing(line);

        Assert.IsTrue(handled, output);
        StringAssert.Contains(File.ReadAllText(Path.Combine(GeneratedDir, "Cart.cs")), "void Ship()");
    }

    [TestMethod]
    // "create class employee" printed "Command not recognized".
    [DataRow("create class employee", "Employee")]
    [DataRow("create class manager", "Manager")]
    public void A_lower_case_name_writes_the_PascalCase_class(string input, string expected)
    {
        Assert.IsTrue(Run(input), input);

        CollectionAssert.AreEqual(new[] { expected + ".cs" }, ClassFilesWritten());
        var declared = AssertCompilesAndDeclares(Path.Combine(GeneratedDir, expected + ".cs"), out _);
        CollectionAssert.AreEqual(new[] { expected }, declared);
    }

    private (bool Handled, string Output) RunCapturing(string input)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var handled = Run(input);
            return (handled, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [TestMethod]
    // "create class Employee" over an existing Employee.cs said "Saved generated class" and
    // replaced the file, losing its properties and methods.
    [DataRow("create class Employee")]
    [DataRow("create class employee")]
    [DataRow("create class Employee with properties Code int")]
    public void An_existing_class_is_not_replaced(string again)
    {
        Assert.IsTrue(Run("create class Employee with properties Name string, Age int"));
        var file = Path.Combine(GeneratedDir, "Employee.cs");
        var before = File.ReadAllText(file);

        var (handled, output) = RunCapturing(again);

        Assert.IsFalse(handled, output);
        Assert.AreEqual(before, File.ReadAllText(file), "the class file was rewritten");
        CollectionAssert.AreEqual(new[] { "Employee.cs" }, ClassFilesWritten());
        AssertCompilesAndDeclares(file, out var properties);
        CollectionAssert.AreEquivalent(new[] { "Name", "Age" }, properties);

        StringAssert.Contains(output, file, output);
        StringAssert.Contains(output, "add property", output);
        StringAssert.Contains(output, "delete class Employee", output);
        Assert.DoesNotContain("Saved generated class", output, output);
    }

    [TestMethod]
    // A file this tool did not write by that exact spelling still holds the class on a
    // case-insensitive file system: writing employee.cs would replace Employee.cs.
    public void An_existing_file_differently_cased_is_not_replaced()
    {
        Directory.CreateDirectory(GeneratedDir);
        var file = Path.Combine(GeneratedDir, "employee.cs");
        const string code = "namespace GeneratedCode { public class employee { public string Name { get; set; } } }";
        File.WriteAllText(file, code);

        var (handled, output) = RunCapturing("create class Employee");

        Assert.IsFalse(handled, output);
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    [TestMethod]
    // In a project the class may sit in a folder (a built app keeps them in Models): a second
    // Employee.cs beside the project file would be a second class of that name.
    public void A_class_in_a_project_folder_is_not_created_again()
    {
        var projectDir = Path.Combine(_workspace, "Shop");
        Directory.CreateDirectory(Path.Combine(projectDir, "Models"));
        File.WriteAllText(Path.Combine(projectDir, "Shop.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var file = Path.Combine(projectDir, "Models", "Employee.cs");
        const string code = "namespace Shop.Models { public class Employee { public string Name { get; set; } } }";
        File.WriteAllText(file, code);

        ProjectMetaData? project = new()
        {
            Success = true,
            ProjectName = "Shop",
            ProjectPath = projectDir,
            CsProjPath = Path.Combine(projectDir, "Shop.csproj")
        };
        var handled = CommandFront.Run("create class Employee", ref project, [], _workspace, _ => { });

        Assert.IsFalse(handled);
        Assert.AreEqual(code, File.ReadAllText(file));
        Assert.IsFalse(File.Exists(Path.Combine(projectDir, "Employee.cs")), "a second Employee.cs was written");
    }

    [TestMethod]
    // delete class moves the file to DeletedClasses; the name is free again.
    public void A_deleted_class_can_be_created_again()
    {
        // delete class needs a project to keep DeletedClasses in; one at the workspace root
        // takes the class beside it.
        File.WriteAllText(Path.Combine(_workspace, "Shop.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        Assert.IsTrue(Run("create class Employee with properties Name string"));
        Assert.IsTrue(Run("delete class Employee"));
        Assert.IsNotEmpty(Directory.GetFiles(Path.Combine(_workspace, "DeletedClasses"), "Employee_*"));

        Assert.IsTrue(Run("create class Employee with properties Code int"));

        AssertCompilesAndDeclares(Path.Combine(_workspace, "Employee.cs"), out var properties);
        CollectionAssert.AreEqual(new[] { "Code" }, properties);
    }

    [TestMethod]
    public void A_lower_case_name_with_properties_writes_them()
    {
        Assert.IsTrue(Run("create class employee with properties Name string, Age int"));

        var declared = AssertCompilesAndDeclares(Path.Combine(GeneratedDir, "Employee.cs"), out var properties);
        CollectionAssert.AreEqual(new[] { "Employee" }, declared);
        CollectionAssert.AreEquivalent(new[] { "Name", "Age" }, properties);
    }
}

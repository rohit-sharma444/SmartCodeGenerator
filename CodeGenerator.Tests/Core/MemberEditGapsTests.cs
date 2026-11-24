using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// add method / add property refuse what would not compile, the listings and lookups see every
/// partial file, and the messages name members as they are declared.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MemberEditGapsTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        ValidationRegistry.Clear();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_membergaps_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ValidationRegistry.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private string LastOutput { get; set; } = string.Empty;

    private bool Run(string input)
    {
        ProjectMetaData? project = null;
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try { return CommandFront.Run(input, ref project, [], _workspace, _ => { }); }
        finally
        {
            Console.SetOut(original);
            LastOutput = writer.ToString();
        }
    }

    private string Write(string name, string body)
    {
        var file = Path.Combine(_workspace, name);
        File.WriteAllText(file, body);
        return file;
    }

    private void AssertWorkspaceCompiles()
    {
        var sources = Directory.GetFiles(_workspace, "*.cs").Select(f => (f, File.ReadAllText(f))).ToList();
        var result = Compiler.CompileSources(sources);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    private const string Employee = "namespace Shop { public class Employee { public string Name { get; set; } = \"\"; } }";

    // --- K7 ---

    [TestMethod]
    // rename method checked this; add method did not, and wrote CS0102.
    public void Add_method_refuses_the_name_of_a_property()
    {
        var file = Write("Employee.cs", Employee);

        Assert.IsFalse(ClassMethodService.AddMethod(_workspace, "Employee", "Name", "void", [], false, out var message), message);

        StringAssert.Contains(message, "already has a property named 'Name'");
        Assert.AreEqual(Employee, File.ReadAllText(file));
    }

    [TestMethod]
    // add property Employee string to Employee was accepted (CS0542).
    public void Add_property_refuses_the_class_own_name()
    {
        var file = Write("Employee.cs", Employee);

        Assert.IsFalse(ClassPropertyService.AddProperty(_workspace, "Employee", "Employee", "string", out var message), message);

        StringAssert.Contains(message, "own name");
        Assert.AreEqual(Employee, File.ReadAllText(file));
    }

    // --- K8 ---

    [TestMethod]
    // add property Foo badtype to Employee wrote CS0246.
    public void Add_property_refuses_an_unknown_type_and_suggests_a_close_one()
    {
        var file = Write("Employee.cs", Employee);

        Assert.IsFalse(ClassPropertyService.AddProperty(_workspace, "Employee", "Foo", "badtype", out var bad), bad);
        Assert.IsFalse(ClassPropertyService.AddProperty(_workspace, "Employee", "Born", "datetime", out var close), close);

        StringAssert.Contains(bad, "badtype");
        StringAssert.Contains(close, "DateTime");
        Assert.AreEqual(Employee, File.ReadAllText(file));
    }

    [TestMethod]
    public void Add_property_takes_framework_generic_nullable_and_project_types()
    {
        Write("Employee.cs", Employee);
        Write("Manager.cs", "namespace Shop { public class Manager { } }");

        Assert.IsTrue(ClassPropertyService.AddProperty(_workspace, "Employee", "Tags", "List<string>", out var m1), m1);
        Assert.IsTrue(ClassPropertyService.AddProperty(_workspace, "Employee", "Boss", "Manager?", out var m2), m2);
        Assert.IsTrue(ClassPropertyService.AddProperty(_workspace, "Employee", "Born", "DateTime?", out var m3), m3);

        // No .csproj, so the SDK's implicit usings are assumed, as a project this tool writes has them.
        Write("Usings.cs", "global using System; global using System.Collections.Generic;");
        AssertWorkspaceCompiles();
    }

    // --- K9 ---

    [TestMethod]
    // datetime got "Did you mean DateTime?", a project class typed in lower case got nothing.
    public void Retype_suggests_a_project_class_typed_in_lower_case()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { } }");
        Write("Order.cs", "namespace Shop { public class Order { public string Buyer { get; set; } = \"\"; } }");

        Assert.IsFalse(ClassPropertyTypeService.ChangeType(_workspace, "Order", "Buyer", "customer", out var message), message);

        StringAssert.Contains(message, "Did you mean Customer?");
    }

    // --- K10 ---

    [TestMethod]
    // list properties read Customer.cs only.
    public void List_properties_shows_the_members_of_every_partial_file()
    {
        Write("Customer.cs", "namespace Shop { public partial class Customer { public string Name { get; set; } = \"\"; } }");
        Write("Customer.Extra.cs", "namespace Shop { public partial class Customer { public string Email { get; set; } = \"\"; } }");

        Run("list properties of Customer");

        StringAssert.Contains(LastOutput, "Name");
        StringAssert.Contains(LastOutput, "Email");
    }

    // --- K11 ---

    [TestMethod]
    // rename property Note to email was allowed beside Email in another partial file.
    public void Rename_refuses_a_name_that_differs_only_in_case_from_another_member()
    {
        var a = Write("Customer.cs", "namespace Shop { public partial class Customer { public string Note { get; set; } = \"\"; } }");
        Write("Customer.Extra.cs", "namespace Shop { public partial class Customer { public string Email { get; set; } = \"\"; } }");
        var before = File.ReadAllText(a);

        Assert.IsFalse(ClassPropertyRenameService.RenameProperty(_workspace, "Customer", "Note", "email", out var message), message);

        StringAssert.Contains(message, "Email");
        Assert.AreEqual(before, File.ReadAllText(a));
    }

    [TestMethod]
    // remove property EMAIL beside Email and email said "not found".
    public void A_name_that_fits_two_members_is_reported_as_ambiguous()
    {
        Write("Customer.cs", "namespace Shop { public partial class Customer { public string Email { get; set; } = \"\"; } }");
        Write("Customer.Extra.cs", "namespace Shop { public partial class Customer { public string email { get; set; } = \"\"; } }");

        Assert.IsFalse(ClassPropertyService.RemoveProperty(_workspace, "Customer", "EMAIL", out var message), message);

        StringAssert.Contains(message, "Email");
        StringAssert.Contains(message, "email");
        StringAssert.Contains(message, "exact case");
    }

    // --- K12 ---

    [TestMethod]
    // "Method 'Save' added to 'customer'."
    public void Add_method_names_the_class_as_declared()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { } }");

        Assert.IsTrue(ClassMethodService.AddMethod(_workspace, "customer", "Save", "void", [], false, out var message), message);

        StringAssert.Contains(message, "'Customer'");
    }

    [TestMethod]
    // "1 validation rule(s) still name name. Remove them (remove validation from customer.name)".
    public void The_stale_rule_warning_names_the_declared_property_and_class()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { public string Name { get; set; } = \"\"; } }");
        Assert.IsTrue(Run("add validation required on Name in Customer"), LastOutput);

        Assert.IsTrue(Run("rename property name to FullName in customer"), LastOutput);

        StringAssert.Contains(LastOutput, "still name Name");
        StringAssert.Contains(LastOutput, "remove validation from Customer.Name");
    }

    // --- K13 ---

    [TestMethod]
    // Circle had double Perimeter(); add method Perimeter to IShape (void) gave no warning, and
    // the project then failed with CS0738.
    public void An_implementer_with_a_different_signature_is_named()
    {
        Write("IShape.cs", "namespace Shop { public interface IShape { double Area(); } }");
        Write("Circle.cs", "namespace Shop { public class Circle : IShape { public double Area() => 1; public double Perimeter() => 2; } }");

        Assert.IsTrue(ClassMethodService.AddMethod(_workspace, "IShape", "Perimeter", "void", [], false, out var message), message);
        var warning = InterfaceImplementers.AddedWarning(_workspace, "IShape", "Perimeter");

        Assert.IsNotNull(warning);
        StringAssert.Contains(warning, "Circle");
        StringAssert.Contains(warning, "double Perimeter()");
        StringAssert.Contains(warning, "void Perimeter()");
    }

    [TestMethod]
    public void An_implementer_with_the_same_signature_is_not_named()
    {
        Write("IShape.cs", "namespace Shop { public interface IShape { double Area(); } }");
        Write("Circle.cs", "namespace Shop { public class Circle : IShape { public double Area() => 1; public double Perimeter() => 2; } }");

        Assert.IsTrue(ClassMethodService.AddMethod(_workspace, "IShape", "Perimeter", "double", [], false, out var message), message);

        Assert.IsNull(InterfaceImplementers.AddedWarning(_workspace, "IShape", "Perimeter"));
        AssertWorkspaceCompiles();
    }

    // --- K15 ---

    [TestMethod]
    // "Class 'Nope' not found." and then "Class not found.".
    public void Show_class_of_a_missing_class_says_so_once()
    {
        Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        ProjectManager.CurrentProject = new ProjectMetaData
        {
            ProjectName = "App",
            ProjectPath = Path.Combine(_workspace, "App.csproj"),
        };
        try
        {
            Assert.IsFalse(Run("show class Nope"), LastOutput);
        }
        finally
        {
            ProjectManager.CurrentProject = null;
        }

        Assert.AreEqual(1, LastOutput.Split("not found").Length - 1, LastOutput);
    }

    [TestMethod]
    // The TODO sat at the closing brace's indent.
    public void A_method_stub_indents_its_todo_inside_the_body()
    {
        var file = Write("Customer.cs", "namespace Shop\n{\n    public class Customer\n    {\n    }\n}\n");

        Assert.IsTrue(ClassMethodService.AddMethod(_workspace, "Customer", "Save", "void", [], false, out var message), message);

        var lines = File.ReadAllLines(file);
        var todo = Array.FindIndex(lines, l => l.Contains("// TODO: implement"));
        Assert.IsGreaterThan(0, todo, File.ReadAllText(file));
        var indent = (string l) => l.Length - l.TrimStart().Length;
        Assert.AreEqual(indent(lines[todo + 1]) + 4, indent(lines[todo]), File.ReadAllText(file));
        AssertWorkspaceCompiles();
    }
}

using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// A <c>create class</c> property list reads a default value (<c>default 1</c>, <c>= 1</c>) and the
/// words <c>readonly</c>, <c>private</c>, <c>static</c> and <c>required</c> after a property's type,
/// refuses what C# forbids, and reports every other word instead of dropping it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PropertyListModifierTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;
    private string Shop => Path.Combine(_workspace, "Shop");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_plm_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Shop);
        File.WriteAllText(Path.Combine(Shop, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("Shape.cs", "namespace Shop;\n\npublic class Shape\n{\n}\n");
        ProjectManager.CurrentProject = new ProjectMetaData
        {
            Success = true,
            ProjectName = "Shop",
            ProjectPath = Shop,
            CsProjPath = Path.Combine(Shop, "Shop.csproj")
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(Shop, name), text);

    private string Read(string name) => File.ReadAllText(Path.Combine(Shop, name));

    private bool Run(string input)
    {
        ProjectMetaData? project = ProjectManager.CurrentProject;
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

    private string[] ProjectErrors() =>
    [
        .. Compiler.CompileSources(Directory.GetFiles(Shop, "*.cs").Select(f => (f, File.ReadAllText(f))))
            .Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
    ];

    private void AssertWritten(string sentence, string file, params string[] expected)
    {
        Assert.IsTrue(Run(sentence), LastOutput);

        var text = Read(file);
        foreach (var part in expected)
            StringAssert.Contains(text, part, LastOutput + Environment.NewLine + text);
        Assert.IsEmpty(ProjectErrors(), text);
    }

    private void AssertRefused(string sentence, string file, string expected)
    {
        var before = Directory.GetFiles(Shop).Order().ToArray();

        Assert.IsFalse(Run(sentence), LastOutput);

        StringAssert.Contains(LastOutput, expected);
        Assert.IsFalse(File.Exists(Path.Combine(Shop, file)), LastOutput);
        CollectionAssert.AreEqual(before, Directory.GetFiles(Shop).Order().ToArray());
    }

    [TestMethod]
    [DataRow("create class Line with properties Qty int default 1 and Price decimal")]
    [DataRow("create class Line with properties Qty int = 1 and Price decimal")]
    public void A_default_is_written_and_the_next_property_is_kept(string sentence)
    {
        AssertWritten(sentence, "Line.cs", "public int Qty { get; set; } = 1;", "public decimal Price");
        Assert.DoesNotContain("Ignored", LastOutput);
    }

    [TestMethod]
    public void An_unquoted_text_default_is_quoted()
    {
        AssertWritten("create class Order with properties Status string default New", "Order.cs",
            "public string Status { get; set; } = \"New\";");
    }

    [TestMethod]
    public void A_quoted_default_is_kept_whole_and_the_list_is_not_split_inside_it()
    {
        AssertWritten("create class Paint with properties Color string default \"Black and White\"", "Paint.cs",
            "public string Color { get; set; } = \"Black and White\";");

        Assert.DoesNotContain("White {", Read("Paint.cs"));
        Assert.DoesNotContain("Ignored", LastOutput);
    }

    [TestMethod]
    public void A_quoted_default_keeps_its_articles_and_commas()
    {
        AssertWritten("create class Note with properties Text string default \"a, b\" and Size int", "Note.cs",
            "public string Text { get; set; } = \"a, b\";", "public int Size");
        Assert.DoesNotContain("Ignored", LastOutput);
    }

    [TestMethod]
    public void Required_is_the_CSharp_keyword()
    {
        AssertWritten("create class Customer with properties Name string required and Email string", "Customer.cs",
            "public required string Name { get; set; }", "public string Email");
        Assert.DoesNotContain("[Required]", Read("Customer.cs"));
    }

    [TestMethod]
    public void Validation_words_after_the_type_are_reported_with_the_command_to_use()
    {
        AssertWritten("create class Customer with properties Name string required min length 3", "Customer.cs",
            "public required string Name { get; set; }");

        StringAssert.Contains(LastOutput, "Ignored 'min length 3' after Name string");
        StringAssert.Contains(LastOutput, "add validation");
        StringAssert.Contains(LastOutput, "on Name in Customer");
    }

    [TestMethod]
    public void Readonly_private_and_static_are_written()
    {
        AssertWritten(
            "create class Order with properties Id int readonly and Secret string private and Max int static default 10",
            "Order.cs",
            "public int Id { get; init; }",
            "private string Secret { get; set; }",
            "public static int Max { get; set; } = 10;");
    }

    [TestMethod]
    public void Required_and_readonly_together_are_allowed()
    {
        AssertWritten("create class Customer with properties Name string readonly required", "Customer.cs",
            "public required string Name { get; init; }");
    }

    [TestMethod]
    public void A_record_property_that_is_static_is_settable()
    {
        AssertWritten("create record Money with properties Amount decimal default 1 and Rate decimal static", "Money.cs",
            "public decimal Amount { get; init; } = 1m;",
            "public static decimal Rate { get; set; }");
    }

    [TestMethod]
    public void A_readonly_property_of_a_static_class_has_only_a_getter()
    {
        AssertWritten("create static class Settings with properties Max int readonly default 5", "Settings.cs",
            "public static int Max { get; } = 5;");
    }

    [TestMethod]
    public void Required_on_a_struct_property_is_allowed()
    {
        AssertWritten("create struct Point with properties X int required", "Point.cs",
            "public required int X { get; set; }");
    }

    [TestMethod]
    public void A_value_that_does_not_fit_the_type_is_refused()
    {
        AssertRefused("create class Line with properties Qty int default abc", "Line.cs",
            "'abc' is not a valid int value for Qty.");
    }

    [TestMethod]
    public void Required_with_static_is_refused()
    {
        AssertRefused("create class Order with properties Code string required static", "Order.cs",
            "cannot be both required and static");
    }

    [TestMethod]
    public void Required_on_a_static_class_property_is_refused()
    {
        AssertRefused("create static class Settings with properties Code string required", "Settings.cs",
            "cannot be both required and static");
    }

    [TestMethod]
    public void A_default_on_a_struct_property_is_refused()
    {
        AssertRefused("create struct Point with properties X int default 1", "Point.cs", "add constructor to Point");
    }

    [TestMethod]
    public void An_unquoted_default_is_one_word_and_the_rest_is_reported()
    {
        AssertWritten("create class City with properties Name string default New York", "City.cs",
            "public string Name { get; set; } = \"New\";");

        StringAssert.Contains(LastOutput, "Ignored 'York' after Name string");
        Assert.DoesNotContain("add validation", LastOutput);
    }

    [TestMethod]
    public void A_word_after_the_type_that_means_nothing_is_reported()
    {
        AssertWritten("create class Order with properties Total decimal nullable and Name string", "Order.cs",
            "public decimal Total { get; set; }", "public string Name");

        StringAssert.Contains(LastOutput, "Ignored 'nullable' after Total decimal");
    }

    [TestMethod]
    public void Default_with_no_value_is_reported()
    {
        AssertWritten("create class Line with properties Qty int default", "Line.cs", "public int Qty { get; set; }");

        StringAssert.Contains(LastOutput, "Ignored 'default' after Qty int");
    }

    [TestMethod]
    public void Create_class_and_add_method_stays_one_command()
    {
        AssertWritten("create class Employee and add method AddEmployee", "Employee.cs",
            "public class Employee", "AddEmployee()");
    }

    [TestMethod]
    [DataRow("create class Circle with properties Radius decimal implements IShape", "public class Circle : IShape")]
    [DataRow("create class Circle with Radius decimal inherits Shape", "public class Circle : Shape")]
    [DataRow("create class Circle with properties Radius decimal extends Shape", "public class Circle : Shape")]
    [DataRow("create class Circle with properties Radius decimal derived from Shape", "public class Circle : Shape")]
    [DataRow("create class Circle with properties Radius decimal base class Shape", "public class Circle : Shape")]
    [DataRow("create class Circle with properties Radius decimal with method Save", "Save()")]
    [DataRow("create class Circle with properties Radius decimal and methods Save", "Save()")]
    public void A_clause_after_the_list_is_applied_and_not_reported(string sentence, string expected)
    {
        Write("IShape.cs", "namespace Shop;\n\npublic interface IShape\n{\n}\n");

        AssertWritten(sentence, "Circle.cs", "public decimal Radius { get; set; }", expected);
        Assert.DoesNotContain("Ignored", LastOutput);
    }

    [TestMethod]
    [DataRow("A")]
    [DataRow("an")]
    [DataRow("the")]
    public void A_default_that_is_a_filler_word_is_kept(string value)
    {
        AssertWritten($"create class Grade with properties Letter string default {value}", "Grade.cs",
            $"public string Letter {{ get; set; }} = \"{value}\";");
        AssertWritten($"create class Mark with properties Letter string = {value}", "Mark.cs",
            $"public string Letter {{ get; set; }} = \"{value}\";");
    }

    [TestMethod]
    [DataRow("range 1 10")]
    [DataRow("nonempty")]
    [DataRow("not empty")]
    [DataRow("unique")]
    [DataRow("allowed x")]
    [DataRow("custom: Check")]
    public void Each_validation_word_gets_the_add_validation_pointer(string words)
    {
        AssertWritten($"create class Customer with properties Name string {words}", "Customer.cs", "public string Name");

        StringAssert.Contains(LastOutput, "after Name string: use add validation");
    }

    [TestMethod]
    public void An_interface_property_reports_its_default_modifiers_and_other_words()
    {
        Assert.IsTrue(Run("create interface IPriced with properties Price decimal required default 5 nullable"), LastOutput);

        var text = Read("IPriced.cs");
        StringAssert.Contains(text, "decimal Price", LastOutput);
        Assert.DoesNotContain("required", text);
        Assert.DoesNotContain("= 5", text);
        StringAssert.Contains(LastOutput, "Ignored 'default 5' after Price decimal: an interface property has no default value");
        StringAssert.Contains(LastOutput, "Ignored 'required' after Price decimal: an interface property has no modifier");
        StringAssert.Contains(LastOutput, "Ignored 'nullable' after Price decimal");
        Assert.DoesNotContain("add validation", LastOutput);
        Assert.IsEmpty(ProjectErrors());
    }

    [TestMethod]
    public void A_modifier_before_the_name_is_applied()
    {
        AssertWritten("create class Order2 with properties readonly Id int, private Secret string", "Order2.cs",
            "public int Id { get; init; }",
            "private string Secret { get; set; }");
        Assert.DoesNotContain("Ignored", LastOutput);
    }

    [TestMethod]
    public void Required_before_the_first_name_applies_to_that_property_only()
    {
        AssertWritten("create class Order3 with properties required Name string and Age int", "Order3.cs",
            "public required string Name { get; set; }",
            "public int Age { get; set; }");
        Assert.DoesNotContain("required int", Read("Order3.cs"));
        Assert.DoesNotContain("Ignored", LastOutput);
    }

    [TestMethod]
    public void A_word_before_the_name_that_means_nothing_is_reported()
    {
        AssertWritten("create class Order4 with properties Id int and mandatory Name string", "Order4.cs",
            "public int Id { get; set; }", "public string Name { get; set; }");

        StringAssert.Contains(LastOutput, "Ignored 'mandatory' before Name string");
    }

    [TestMethod]
    public void A_single_quoted_default_is_a_string_without_its_quotes()
    {
        AssertWritten("create class Order5 with properties Status string default 'New'", "Order5.cs",
            "public string Status { get; set; } = \"New\";");
        Assert.DoesNotContain("'", Read("Order5.cs"));
    }

    [TestMethod]
    public void A_single_quoted_default_of_two_words_is_kept_whole()
    {
        AssertWritten("create class City2 with properties Name string default 'New York' and Size int", "City2.cs",
            "public string Name { get; set; } = \"New York\";", "public int Size");
        Assert.DoesNotContain("Ignored", LastOutput);
    }

    [TestMethod]
    public void A_clause_word_inside_a_quoted_default_does_not_end_the_list()
    {
        AssertWritten("create class Note2 with properties Text string default \"extends the base\" and Size int", "Note2.cs",
            "public string Text { get; set; } = \"extends the base\";", "public int Size");
        Assert.DoesNotContain("Note2 :", Read("Note2.cs"));
        Assert.DoesNotContain("Ignored", LastOutput);
    }

    [TestMethod]
    public void A_quoted_default_may_hold_escaped_quotes()
    {
        AssertWritten("create class Greeting with properties Text string default \"say \\\"hi\\\"\" and Size int", "Greeting.cs",
            "public string Text { get; set; } = \"say \\\"hi\\\"\";", "public int Size");
        Assert.DoesNotContain("Ignored", LastOutput);
    }

    [TestMethod]
    public void The_bridge_carries_the_raw_default_and_the_modifiers()
    {
        var command = CommandEntityExtractor.Extract(
            "create class Order with properties Status string default New static", IntentType.CreateClass, "");

        var status = command.Properties.Single();
        Assert.AreEqual("New", status.Initializer);
        Assert.AreEqual(PropertyModifiers.Static, status.Modifiers);
        Assert.IsEmpty(command.IgnoredWords);
    }
}

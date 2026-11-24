using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <c>add constructor</c> writes a constructor that sets the properties the class declares (or the
/// ones named), refuses what C# forbids and writes nothing then.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AddConstructorTests
{
    private const string OrderSource =
        "namespace Shop;\n\npublic class Order\n{\n"
        + "    public decimal Total { get; set; }\n"
        + "    public string Name { get; set; } = string.Empty;\n"
        + "    public static int Count { get; set; }\n"
        + "    private string Secret { get; set; } = \"\";\n"
        + "    public decimal Tax => Total * 0.2m;\n"
        + "}\n";

    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;
    private string Shop => Path.Combine(_workspace, "Shop");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_ctor_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Shop);
        File.WriteAllText(Path.Combine(Shop, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("Order.cs", OrderSource);
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

    private void AssertWritten(string file, string sentence, params string[] expected)
    {
        Assert.IsTrue(Run(sentence), LastOutput);

        var text = Read(file);
        foreach (var part in expected)
            StringAssert.Contains(text, part, LastOutput + Environment.NewLine + text);
        Assert.IsEmpty(ProjectErrors(), text);
    }

    private void AssertRefused(string sentence, string expected)
    {
        var before = Directory.GetFiles(Shop).OrderBy(f => f).ToDictionary(f => f, File.ReadAllText);

        Assert.IsFalse(Run(sentence), LastOutput);

        StringAssert.Contains(LastOutput, expected);
        var after = Directory.GetFiles(Shop).OrderBy(f => f).ToDictionary(f => f, File.ReadAllText);
        CollectionAssert.AreEqual(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var (file, text) in before)
            Assert.AreEqual(text, after[file], file);
    }

    [TestMethod]
    public void No_list_takes_every_settable_instance_property_in_order()
    {
        AssertWritten("Order.cs", "add constructor to Order",
            "public Order(decimal total, string name)", "Total = total;", "Name = name;");

        var text = Read("Order.cs");
        var written = text[text.IndexOf("public Order(", StringComparison.Ordinal)..];
        Assert.DoesNotContain("count", written);
        Assert.DoesNotContain("secret", written);
        Assert.DoesNotContain("tax", written);
        StringAssert.Contains(LastOutput, "Constructor Order(decimal, string) added to 'Order'.");
    }

    [TestMethod]
    public void A_list_takes_only_the_properties_named()
    {
        AssertWritten("Order.cs", "add constructor with Name to Order", "public Order(string name)", "Name = name;");
        Assert.DoesNotContain("decimal total", Read("Order.cs"));
    }

    [TestMethod]
    public void A_list_keeps_the_order_typed()
    {
        AssertWritten("Order.cs", "add constructor with Name and Total to Order", "public Order(string name, decimal total)");
    }

    [TestMethod]
    public void A_list_may_use_commas_and_taking()
    {
        AssertWritten("Order.cs", "add constructor taking Name, Total to Order", "public Order(string name, decimal total)");
    }

    [TestMethod]
    public void A_named_private_property_is_allowed()
    {
        AssertWritten("Order.cs", "add constructor with Secret to Order", "public Order(string secret)", "Secret = secret;");
    }

    [TestMethod]
    public void For_names_the_class_as_to_does()
    {
        AssertWritten("Order.cs", "add constructor for Order", "public Order(decimal total, string name)");
    }

    [TestMethod]
    public void In_class_names_the_class()
    {
        AssertWritten("Order.cs", "create a new constructor in class Order", "public Order(decimal total, string name)");
    }

    [TestMethod]
    public void A_class_with_no_such_properties_gets_an_empty_constructor()
    {
        Write("Empty.cs", "namespace Shop;\n\npublic class Empty\n{\n}\n");

        AssertWritten("Empty.cs", "add constructor to Empty", "public Empty()");
    }

    [TestMethod]
    public void A_name_that_is_not_a_property_is_refused()
    {
        AssertRefused("add constructor with Ghost to Order", "'Ghost' is not a property of 'Order'");
    }

    [TestMethod]
    public void A_second_constructor_with_the_same_parameter_types_is_refused()
    {
        Assert.IsTrue(Run("add constructor to Order"), LastOutput);

        AssertRefused("add constructor to Order", "already has a constructor");
    }

    [TestMethod]
    public void A_constructor_that_sets_every_required_property_gets_the_attribute()
    {
        Write("Account.cs",
            "namespace Shop;\n\npublic class Account\n{\n"
            + "    public required string Id { get; set; }\n"
            + "    public decimal Balance { get; set; }\n}\n");

        AssertWritten("Account.cs", "add constructor to Account",
            "[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]", "public Account(string id, decimal balance)");
    }

    [TestMethod]
    public void A_constructor_that_leaves_a_required_property_out_names_it_in_a_note()
    {
        Write("Account.cs",
            "namespace Shop;\n\npublic class Account\n{\n"
            + "    public required string Id { get; set; }\n"
            + "    public decimal Balance { get; set; }\n}\n");

        Assert.IsTrue(Run("add constructor with Balance to Account"), LastOutput);

        var text = Read("Account.cs");
        Assert.DoesNotContain("SetsRequiredMembers", text);
        StringAssert.Contains(text, "public Account(decimal balance)");
        StringAssert.Contains(LastOutput, "Id");
        StringAssert.Contains(LastOutput, "required");
    }

    [TestMethod]
    public void A_property_named_like_a_keyword_gets_an_at_parameter()
    {
        Write("Course.cs", "namespace Shop;\n\npublic class Course\n{\n    public string Class { get; set; } = \"\";\n}\n");

        AssertWritten("Course.cs", "add constructor to Course", "public Course(string @class)", "Class = @class;");
    }

    [TestMethod]
    public void A_static_class_is_refused()
    {
        Write("Util.cs", "namespace Shop;\n\npublic static class Util\n{\n    public static int N { get; set; }\n}\n");

        AssertRefused("add constructor to Util", "static class");
    }

    [TestMethod]
    public void An_enum_is_refused()
    {
        Write("Color.cs", "namespace Shop;\n\npublic enum Color\n{\n    Red,\n    Blue\n}\n");

        AssertRefused("add constructor to Color", "'Color' is an enum");
    }

    [TestMethod]
    public void An_interface_is_refused()
    {
        Write("IShape.cs", "namespace Shop;\n\npublic interface IShape\n{\n    double Area { get; }\n}\n");

        AssertRefused("add constructor to IShape", "interface");
    }

    [TestMethod]
    public void A_positional_record_is_refused()
    {
        Write("Money.cs", "namespace Shop;\n\npublic record Money(decimal Amount);\n");

        AssertRefused("add constructor to Money", "positional record");
    }

    [TestMethod]
    public void A_record_with_a_body_gets_a_constructor()
    {
        Write("Point.cs", "namespace Shop;\n\npublic record Point\n{\n    public int X { get; init; }\n}\n");

        AssertWritten("Point.cs", "add constructor to Point", "public Point(int x)", "X = x;");
    }

    [TestMethod]
    public void A_struct_gets_a_constructor()
    {
        Write("Pair.cs", "namespace Shop;\n\npublic struct Pair\n{\n    public int A { get; set; }\n    public int B { get; set; }\n}\n");

        AssertWritten("Pair.cs", "add constructor to Pair", "public Pair(int a, int b)");
    }

    [TestMethod]
    public void A_constructor_that_does_not_compile_is_refused_with_the_compiler_text()
    {
        Write("Base.cs", "namespace Shop;\n\npublic class Base\n{\n    public Base(int id) { }\n}\n");
        Write("Kid.cs",
            "namespace Shop;\n\npublic class Kid : Base\n{\n    public string Label { get; set; } = \"\";\n"
            + "    public Kid(int id) : base(id) { }\n}\n");

        // The new Kid(string label) has no base call, and Base has no parameterless constructor.
        AssertRefused("add constructor to Kid", "CS7036");
    }

    [TestMethod]
    public void Create_class_and_add_method_is_still_one_command()
    {
        Assert.IsNull(CommandFront.TwoCommands("create class Cart and add method Total"));
    }

    [TestMethod]
    public void Add_constructor_is_a_guarded_intent_the_parser_reads()
    {
        var command = CommandFront.Parse("add constructor with Name and Total to Order", _workspace);

        Assert.AreEqual(IntentType.AddConstructor, command.Intent);
        Assert.AreEqual("Order", command.ClassName);
        CollectionAssert.AreEqual(new[] { "Name", "Total" }, command.ConstructorProperties);
    }
}

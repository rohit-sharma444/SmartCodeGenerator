using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <c>add property</c> reads a default value (<c>default 5</c>, <c>= 5</c>) and the words
/// <c>readonly</c>, <c>private</c>, <c>static</c> and <c>required</c> before <c>property</c>,
/// refuses what C# forbids and writes nothing then.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AddPropertyModifierTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;
    private string Shop => Path.Combine(_workspace, "Shop");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_apm_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Shop);
        File.WriteAllText(Path.Combine(Shop, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("Order.cs", "namespace Shop;\n\npublic class Order\n{\n}\n");
        Write("Line.cs", "namespace Shop;\n\npublic class Line\n{\n}\n");
        Write("Point.cs", "namespace Shop;\n\npublic struct Point\n{\n}\n");
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

    private void AssertWritten(string sentence, params string[] expected)
    {
        Assert.IsTrue(Run(sentence), LastOutput);

        var text = Read("Order.cs");
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
    public void A_default_after_equals_goes_to_the_class_named_and_not_the_current_one()
    {
        SessionContext.CurrentClass = "Line";
        var line = Read("Line.cs");

        AssertWritten("add property Count int = 5 to Order", "public int Count { get; set; } = 5;");

        Assert.AreEqual(line, Read("Line.cs"));
        Assert.DoesNotContain("Using the current class", LastOutput);
    }

    [TestMethod]
    public void A_quoted_default_is_kept()
    {
        AssertWritten("add property Status string default \"New\" to Order", "public string Status { get; set; } = \"New\";");
    }

    [TestMethod]
    public void An_unquoted_text_default_is_quoted()
    {
        AssertWritten("add property Status string default New to Order", "public string Status { get; set; } = \"New\";");
    }

    [TestMethod]
    [DataRow("A")]
    [DataRow("the")]
    public void A_default_that_is_a_filler_word_is_kept(string value)
    {
        AssertWritten($"add property Letter string default {value} to Order", $"public string Letter {{ get; set; }} = \"{value}\";");
    }

    [TestMethod]
    public void Readonly_is_init()
    {
        AssertWritten("add readonly property Id int to Order", "public int Id { get; init; }");
    }

    [TestMethod]
    public void Private_is_private()
    {
        AssertWritten("add private property Secret string to Order", "private string Secret { get; set; }");
    }

    [TestMethod]
    public void Static_with_a_default()
    {
        AssertWritten("add static property Max int default 10 to Order", "public static int Max { get; set; } = 10;");
    }

    [TestMethod]
    public void Required_is_the_keyword()
    {
        AssertWritten("add required property Code string to Order", "public required string Code { get; set; }");
        Assert.DoesNotContain("[Required]", Read("Order.cs"));
    }

    [TestMethod]
    public void Required_and_readonly_together_are_allowed()
    {
        AssertWritten("add required readonly property Code2 string to Order", "public required string Code2 { get; init; }");
    }

    [TestMethod]
    public void A_value_that_does_not_fit_the_type_is_refused()
    {
        AssertRefused("add property Count2 int default abc to Order", "'abc' is not a valid int value for Count2.");
    }

    [TestMethod]
    public void A_default_on_a_record_struct_property_is_refused()
    {
        Write("Pair.cs", "namespace Shop;\n\npublic record struct Pair\n{\n}\n");
        AssertRefused("add property X int default 1 to Pair", "add constructor to Pair");
    }

    [TestMethod]
    public void A_default_on_an_interface_property_is_refused()
    {
        Write("IPriced.cs", "namespace Shop;\n\npublic interface IPriced\n{\n}\n");
        AssertRefused("add property X int default 1 to IPriced", "An interface property has no default value.");
    }

    [TestMethod]
    public void A_modifier_on_an_interface_property_is_refused()
    {
        Write("IPriced.cs", "namespace Shop;\n\npublic interface IPriced\n{\n}\n");
        AssertRefused("add required property X int to IPriced", "An interface property takes no modifiers.");
    }

    [TestMethod]
    public void A_default_on_a_record_property_is_init()
    {
        Write("Money.cs", "namespace Shop;\n\npublic record Money\n{\n}\n");
        Assert.IsTrue(Run("add property Amount int default 5 to Money"), LastOutput);
        StringAssert.Contains(Read("Money.cs"), "public int Amount { get; init; } = 5;");
        Assert.IsEmpty(ProjectErrors());
    }

    [TestMethod]
    public void Required_with_static_is_refused()
    {
        AssertRefused("add required static property X int to Order", "cannot be both required and static");
    }

    [TestMethod]
    public void A_default_on_a_struct_property_is_refused()
    {
        AssertRefused("add property X int default 1 to Point", "add constructor to Point");
    }

    [TestMethod]
    public void A_property_with_no_modifier_or_default_is_unchanged()
    {
        AssertWritten("add property Title string to Order", "public string Title { get; set; }");
        Assert.DoesNotContain("default", LastOutput);
    }

    [TestMethod]
    public void The_success_message_names_the_default_and_the_modifiers()
    {
        AssertWritten("add static property Max int default 10 to Order", "Max");
        StringAssert.Contains(LastOutput, "default 10");
        StringAssert.Contains(LastOutput, "static");
    }

    [TestMethod]
    public void A_single_quoted_default_is_a_string_without_its_quotes()
    {
        AssertWritten("add property Status string default 'New' to Order", "public string Status { get; set; } = \"New\";");
        Assert.DoesNotContain("'", Read("Order.cs"));
    }

    [TestMethod]
    public void A_single_quoted_default_of_two_words_is_kept_whole()
    {
        AssertWritten("add property City string default 'New York' to Order", "public string City { get; set; } = \"New York\";");
    }

    [TestMethod]
    public void A_single_quoted_char_default_stays_a_char()
    {
        AssertWritten("add property Grade char default 'A' to Order", "public char Grade { get; set; } = 'A';");
    }

    [TestMethod]
    public void A_quoted_default_may_hold_escaped_quotes()
    {
        AssertWritten("add property Text string default \"say \\\"hi\\\"\" to Order",
            "public string Text { get; set; } = \"say \\\"hi\\\"\";");
    }
}

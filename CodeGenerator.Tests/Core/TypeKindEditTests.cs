using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <c>add property</c> and <c>add method</c> follow the kind of the type they edit: a record
/// takes <c>init</c>, a static class takes <c>static</c> members, an abstract class takes an
/// abstract method on request.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TypeKindEditTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;
    private string Shop => Path.Combine(_workspace, "Shop");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_tke_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Shop);
        File.WriteAllText(Path.Combine(Shop, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("Money.cs", "namespace Shop;\n\npublic record Money\n{\n}\n");
        Write("Helpers.cs", "namespace Shop;\n\npublic static class Helpers\n{\n}\n");
        Write("Shape.cs", "namespace Shop;\n\npublic abstract class Shape\n{\n}\n");
        Write("Order.cs", "namespace Shop;\n\npublic class Order\n{\n}\n");
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

    [TestMethod]
    [DataRow("add property Amount decimal to Money", "Money.cs", "{ get; init; }")]
    [DataRow("add property Count int to Helpers", "Helpers.cs", "public static int Count { get; set; }")]
    [DataRow("add method Format returns string to Helpers", "Helpers.cs", "public static string Format()")]
    [DataRow("add method Describe returns string to Shape", "Shape.cs", "public string Describe()")]
    [DataRow("add abstract method Area returns decimal to Shape", "Shape.cs", "public abstract decimal Area();")]
    [DataRow("add abstract method Area(decimal scale) returns decimal to Shape", "Shape.cs",
        "public abstract decimal Area(decimal scale);")]
    [DataRow("add method Save to Order", "Order.cs", "public void Save()")]
    public void An_edit_follows_the_kind(string command, string file, string expected)
    {
        Assert.IsTrue(Run(command), LastOutput);
        var text = Read(file);
        StringAssert.Contains(text, expected);
        CollectionAssert.AreEqual(Array.Empty<string>(), ProjectErrors());
    }

    [TestMethod]
    public void A_method_added_to_an_abstract_class_has_a_body()
    {
        Assert.IsTrue(Run("add method Describe returns string to Shape"), LastOutput);
        Assert.DoesNotContain("abstract string Describe", Read("Shape.cs"));
        StringAssert.Contains(Read("Shape.cs"), "return string.Empty;");
    }

    [TestMethod]
    public void An_abstract_method_on_a_type_that_is_not_abstract_is_refused()
    {
        var before = Read("Order.cs");
        Assert.IsFalse(Run("add abstract method Area to Order"));
        StringAssert.Contains(LastOutput, "'Order' is not abstract");
        Assert.AreEqual(before, Read("Order.cs"));
    }
}

using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <c>add method</c> takes parameters, in the C# form or the English form.
/// </summary>
/// <remarks>
/// "add method ApplyDiscount(decimal percent) returns decimal to Order" and each English phrasing
/// answered "A method name is required.": the command had no parameter part at all.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class AddMethodParameterTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;
    private string Shop => Path.Combine(_workspace, "Shop");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_amp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Shop);
        File.WriteAllText(Path.Combine(Shop, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public decimal Total { get; set; }\n}\n");
        Write("Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n}\n");
        Write("IShape.cs", "namespace Shop;\n\npublic interface IShape\n{\n}\n");
        Write("Square.cs", "namespace Shop;\n\npublic class Square : IShape\n{\n}\n");
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

    private string Order => File.ReadAllText(Path.Combine(Shop, "Order.cs"));

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
    [DataRow("add method ApplyDiscount(decimal percent) returns decimal to Order", "public decimal ApplyDiscount(decimal percent)")]
    [DataRow("add method Ship with parameters address string, express bool to Order", "public void Ship(string address, bool express)")]
    [DataRow("add method Ship taking address string to Order", "public void Ship(string address)")]
    [DataRow("add method Ship(string address, bool express) to Order", "public void Ship(string address, bool express)")]
    [DataRow("add method Assign(Customer customer) to Order", "public void Assign(Customer customer)")]
    [DataRow("add method Save to Order", "public void Save()")]
    [DataRow("add method Ship with parameters address string returns bool to Order", "public bool Ship(string address)")]
    // The fixture project has no implicit usings, so the generic type is named in full.
    [DataRow("add method Lookup(string key) returns System.Collections.Generic.Dictionary<string, int> to Order",
        "public System.Collections.Generic.Dictionary<string, int> Lookup(string key)")]
    public void The_method_is_written_with_its_parameters(string sentence, string expected)
    {
        Assert.IsTrue(Run(sentence), LastOutput);

        StringAssert.Contains(Order, expected, LastOutput);
        Assert.IsEmpty(ProjectErrors());
    }

    [TestMethod]
    [DataRow("add method Assign(Ghost ghost) to Order", "Ghost")]
    [DataRow("add method Bad(int a, int a) to Order", "'a' is repeated")]
    [DataRow("add method Bad(int) to Order", "'int' needs a type and a name")]
    [DataRow("add method Bad with parameters class string to Order", "'class' is not a valid parameter name")]
    public void A_bad_parameter_is_refused_and_nothing_is_written(string sentence, string expected)
    {
        var before = Order;

        Assert.IsFalse(Run(sentence), LastOutput);

        StringAssert.Contains(LastOutput, expected);
        Assert.AreEqual(before, Order);
    }

    [TestMethod]
    public void An_interface_method_names_its_implementers_with_the_parameter_types()
    {
        Assert.IsTrue(Run("add method Scale(decimal factor) to IShape"), LastOutput);

        StringAssert.Contains(File.ReadAllText(Path.Combine(Shop, "IShape.cs")), "Scale(decimal factor)");
        StringAssert.Contains(LastOutput, "Square");
        StringAssert.Contains(LastOutput, "has no void Scale(decimal) yet");
    }
}

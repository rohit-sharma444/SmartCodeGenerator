using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <c>create enum</c> writes an enum with the values it is given, and refuses what C# forbids.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EnumCommandTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;
    private string Shop => Path.Combine(_workspace, "Shop");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_enum_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Shop);
        File.WriteAllText(Path.Combine(Shop, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("Shape.cs", "namespace Shop;\n\npublic class Shape\n{\n}\n");
        Write("IShape.cs", "namespace Shop;\n\npublic interface IShape\n{\n}\n");
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

    private void AssertValuesInOrder(string code, params string[] values)
    {
        var last = -1;
        foreach (var value in values)
        {
            var at = code.IndexOf(value, last + 1, StringComparison.Ordinal);
            Assert.IsGreaterThan(last, at, $"{value} should follow the value before it in:\n{code}");
            last = at;
        }
    }

    [TestMethod]
    [DataRow("create enum Status with values Active, Closed, Pending")]
    [DataRow("create enum Status with members Active, Closed, Pending")]
    [DataRow("create enum Status with Active, Closed, Pending")]
    [DataRow("create enum Status having Active, Closed, Pending")]
    [DataRow("create enum Status with values Active, Closed and Pending")]
    public void CreateEnum_WritesTheValuesInOrder(string input)
    {
        Assert.IsTrue(Run(input), LastOutput);

        var code = Read("Status.cs");
        StringAssert.Contains(code, "public enum Status");
        AssertValuesInOrder(code, "Active,", "Closed,", "Pending");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    public void CreateEnum_WithNoValues_WritesAnEmptyEnum()
    {
        Assert.IsTrue(Run("create enum Status"), LastOutput);

        StringAssert.Contains(Read("Status.cs"), "public enum Status");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    public void CreateEnum_LowerCaseName_IsPascalCasedAndValueKeptAsTyped()
    {
        Assert.IsTrue(Run("create enum status with values active"), LastOutput);

        var code = Read("Status.cs");
        StringAssert.Contains(code, "public enum Status");
        StringAssert.Contains(code, "active");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    [DataRow("create enum Status with values Active, active", "'active' is repeated")]
    [DataRow("create enum Status with values Active, class", "'class' is not a valid enum value name")]
    [DataRow("create enum Status inherits Base", "An enum cannot inherit")]
    public void CreateEnum_Refused_WritesNothing(string input, string reason)
    {
        Assert.IsFalse(Run(input));

        StringAssert.Contains(LastOutput, reason);
        Assert.IsFalse(File.Exists(Path.Combine(Shop, "Status.cs")), "a refusal writes nothing");
    }

    [TestMethod]
    public void CreateEnum_ExistingFile_IsRefusedAndLeftAsItIs()
    {
        Write("Status.cs", "namespace Shop;\n\npublic enum Status\n{\n    Old\n}\n");

        Assert.IsFalse(Run("create enum Status with values Active"));

        StringAssert.Contains(LastOutput, "already exists");
        StringAssert.Contains(Read("Status.cs"), "Old");
    }

    [TestMethod]
    [DataRow("A, B and C", new[] { "A", "B", "C" })]
    [DataRow("A,B", new[] { "A", "B" })]
    [DataRow("", new string[0])]
    public void EnumValueParser_SplitsOnCommasAndAnd(string text, string[] expected)
    {
        Assert.IsTrue(EnumValueParser.TryParse(text, out var values, out var error), error);
        CollectionAssert.AreEqual(expected, values);
    }
}

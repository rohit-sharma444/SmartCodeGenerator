using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <c>create</c> writes a record, a struct and an abstract, static or sealed class, and refuses
/// what C# forbids.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TypeKindCreateTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;
    private string Shop => Path.Combine(_workspace, "Shop");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_tkc_" + Guid.NewGuid().ToString("N")[..8]);
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

    [TestMethod]
    [DataRow("create record Money with properties Amount decimal and Currency string", "Money.cs",
        new[] { "public record Money", "public decimal Amount { get; init; }" })]
    [DataRow("create struct Point with properties X int and Y int", "Point.cs",
        new[] { "public struct Point", "public int X { get; set; }" })]
    [DataRow("create abstract class Figure with properties Name string and methods Area returns decimal", "Figure.cs",
        new[] { "public abstract class Figure", "public abstract decimal Area();", "public string Name { get; set; }" })]
    [DataRow("create static class Helpers with properties Count int and methods Format returns string", "Helpers.cs",
        new[] { "public static class Helpers", "public static int Count", "public static string Format()" })]
    [DataRow("create sealed class Token", "Token.cs", new[] { "public sealed class Token" })]
    [DataRow("create sealed record Money", "Money.cs", new[] { "public sealed record Money" })]
    [DataRow("create record money with properties Amount decimal and methods Total returns decimal", "Money.cs",
        new[] { "public record Money", "Amount", "Total()" })]
    public void Each_kind_is_written_and_the_project_compiles(string sentence, string file, string[] expected)
    {
        Assert.IsTrue(Run(sentence), LastOutput);

        var text = Read(file);
        foreach (var part in expected)
            StringAssert.Contains(text, part, LastOutput);
        Assert.IsEmpty(ProjectErrors());
    }

    [TestMethod]
    public void A_struct_property_has_no_initializer()
    {
        Assert.IsTrue(Run("create struct Label with properties Text string"), LastOutput);

        StringAssert.Contains(Read("Label.cs"), "public string Text { get; set; }");
        Assert.DoesNotContain("= string.Empty", Read("Label.cs"));
        Assert.IsEmpty(ProjectErrors());
    }

    [TestMethod]
    [DataRow("create struct Point inherits Shape", "A struct cannot inherit from a class", "Point.cs")]
    [DataRow("create static class Helpers implements IShape", "A static class cannot implement an interface", "Helpers.cs")]
    [DataRow("create static class Helpers inherits Shape", "A static class cannot inherit", "Helpers.cs")]
    [DataRow("create static struct P", "A struct cannot be static.", "P.cs")]
    public void What_C_forbids_is_refused_and_nothing_is_written(string sentence, string expected, string file)
    {
        var before = Directory.GetFiles(Shop).Order().ToArray();

        Assert.IsFalse(Run(sentence), LastOutput);

        StringAssert.Contains(LastOutput, expected);
        Assert.IsFalse(File.Exists(Path.Combine(Shop, file)));
        CollectionAssert.AreEqual(before, Directory.GetFiles(Shop).Order().ToArray());
    }
}

using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Gaps 5 #4: <c>create class</c> was compiled alone, so a project interface or base class was
/// "CS0246 could not be found", while the class, saved anyway, then failed <c>dotnet build</c>
/// with CS0535 for the interface members it never implemented.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CreateClassInProjectTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_cip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_workspace, "Shop"));
        File.WriteAllText(Path.Combine(_workspace, "Shop", "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        ProjectManager.CurrentProject = new ProjectMetaData
        {
            Success = true,
            ProjectName = "Shop",
            ProjectPath = Path.Combine(_workspace, "Shop"),
            CsProjPath = Path.Combine(_workspace, "Shop", "Shop.csproj")
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

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

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_workspace, "Shop", name), text);

    private string[] ProjectErrors() =>
    [
        .. Compiler.CompileSources(Directory.GetFiles(Path.Combine(_workspace, "Shop"), "*.cs")
                .Select(f => (f, File.ReadAllText(f))))
            .Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
    ];

    [TestMethod]
    public void A_class_implementing_a_project_interface_gets_its_members_and_compiles()
    {
        Write("IShape.cs", """
            namespace Shop;

            public interface IShape
            {
                decimal Area { get; }
                void Draw(int scale, string? label);
                string Name { get; set; }
            }
            """);

        Assert.IsTrue(Run("create class Circle with properties Radius decimal implements IShape"), LastOutput);

        Assert.DoesNotContain("CS0246", LastOutput, LastOutput);
        Assert.DoesNotContain("does not compile", LastOutput, LastOutput);
        var errors = ProjectErrors();
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors) + Environment.NewLine
            + File.ReadAllText(Path.Combine(_workspace, "Shop", "Circle.cs")));
        StringAssert.Contains(LastOutput, "Area");
        StringAssert.Contains(LastOutput, "Draw");
        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Shop", "Circle.cs")), "NotImplementedException");
    }

    [TestMethod]
    public void A_class_inheriting_a_project_class_is_not_reported_as_unknown()
    {
        Write("Base1.cs", "namespace Shop;\n\npublic class Base1 { public int Id { get; set; } }\n");

        Assert.IsTrue(Run("create class Square inherits Base1"), LastOutput);

        Assert.DoesNotContain("CS0246", LastOutput, LastOutput);
        Assert.DoesNotContain("does not compile", LastOutput, LastOutput);
        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Shop", "Square.cs")), "Base1");
        Assert.IsEmpty(ProjectErrors());
    }

    // It was reported and saved anyway, and "create class" said it had succeeded: the project then
    // failed dotnet build. Inside a project the class is checked with the project's own files, so
    // an error is the class's own, and it is not written.
    [TestMethod]
    public void A_class_that_does_not_compile_in_its_project_is_not_saved()
    {
        Assert.IsFalse(Run("create class Square inherits Nowhere1"), LastOutput);

        StringAssert.Contains(LastOutput, "CS0246");
        StringAssert.Contains(LastOutput, "Square was not created");
        Assert.DoesNotContain("Validate succeeded", LastOutput, LastOutput);
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Shop", "Square.cs")), LastOutput);
        Assert.IsEmpty(ProjectErrors());
    }

    [TestMethod]
    public void The_class_can_be_created_once_its_base_exists()
    {
        Assert.IsFalse(Run("create class Square inherits Shape1"), LastOutput);
        Assert.IsTrue(Run("create class Shape1"), LastOutput);

        Assert.IsTrue(Run("create class Square inherits Shape1"), LastOutput);

        Assert.IsTrue(File.Exists(Path.Combine(_workspace, "Shop", "Square.cs")), LastOutput);
        Assert.IsEmpty(ProjectErrors());
    }
}

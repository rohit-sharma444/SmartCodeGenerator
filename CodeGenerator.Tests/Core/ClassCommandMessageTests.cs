using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>The smaller messages of the class commands say what happened, once.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ClassCommandMessageTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_classmsg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
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

    private void Write(string relative, string text)
    {
        var file = Path.Combine(_workspace, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    [TestMethod]
    // "Classes: 2" (every .cs, obj included) above "Classes: 1".
    public void Analyze_project_prints_one_class_count_from_the_source_only()
    {
        Write("Alpha/Alpha.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("Alpha/Customer.cs", "namespace Alpha { public partial class Customer { public int Id { get; set; } } }");
        Write("Alpha/Customer.Validation.cs", "namespace Alpha { public partial class Customer { } }");
        Write("Alpha/obj/Debug/Alpha.AssemblyInfo.cs", "namespace Alpha { class Generated { } }");
        ProjectManager.CurrentProject = new ProjectMetaData { ProjectName = "Alpha", ProjectPath = Path.Combine(_workspace, "Alpha") };

        Run("analyze project");

        Assert.AreEqual(1, LastOutput.Split("Classes:").Length - 1, LastOutput);
        StringAssert.Contains(LastOutput, "Classes: 1");
    }

    [TestMethod]
    // "Creating project 'Alpha'..." and then "Existing project loaded".
    public void Creating_a_project_that_exists_says_so_and_creates_nothing()
    {
        Write("Alpha/Alpha.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        Assert.IsTrue(Run("create console project Alpha"), LastOutput);

        Assert.DoesNotContain("Creating project", LastOutput, LastOutput);
        StringAssert.Contains(LastOutput, "already exists");
        StringAssert.Contains(LastOutput, "current project");
    }

    [TestMethod]
    // With no project the class went to Generated\ with no .csproj, and nothing was said.
    public void Creating_a_class_with_no_project_warns_that_nothing_builds_it()
    {
        Assert.IsTrue(Run("create class Loose with properties Name string"), LastOutput);

        StringAssert.Contains(LastOutput, "No project is selected");
        StringAssert.Contains(LastOutput, "create console project");
    }
}

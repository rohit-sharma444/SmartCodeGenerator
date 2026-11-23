using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Bug hunt 2, E1: with Alpha and Beta both holding a Customer and Beta selected, the edit
/// commands changed Alpha's file, and <c>delete class</c> moved both. The selected project's class
/// is the one edited; with nothing selected and two files of the name, the command refuses.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SelectedProjectEditTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;

    private string AlphaFile => Path.Combine(_workspace, "Alpha", "Customer.cs");
    private string BetaFile => Path.Combine(_workspace, "Beta", "Customer.cs");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_e1_" + Guid.NewGuid().ToString("N"));

        foreach (var name in new[] { "Alpha", "Beta" })
        {
            var dir = Path.Combine(_workspace, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + ".csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(dir, "Customer.cs"),
                $"namespace {name};\n\npublic class Customer\n{{\n    public int Id {{ get; set; }}\n}}\n");
        }
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private void SelectBeta() =>
        ProjectManager.CurrentProject = new ProjectMetaData
        {
            Success = true,
            ProjectName = "Beta",
            ProjectPath = Path.Combine(_workspace, "Beta"),
            CsProjPath = Path.Combine(_workspace, "Beta", "Beta.csproj"),
        };

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

    [TestMethod]
    public void Member_edits_change_only_the_selected_projects_class()
    {
        SelectBeta();
        var alpha = File.ReadAllBytes(AlphaFile);

        Assert.IsTrue(Run("add property Age int to Customer"), LastOutput);
        StringAssert.Contains(File.ReadAllText(BetaFile), "Age");
        CollectionAssert.AreEqual(alpha, File.ReadAllBytes(AlphaFile), "Alpha's Customer was changed");

        Assert.IsTrue(Run("add method Save to Customer"), LastOutput);
        StringAssert.Contains(File.ReadAllText(BetaFile), "Save");
        CollectionAssert.AreEqual(alpha, File.ReadAllBytes(AlphaFile), "Alpha's Customer was changed");

        Assert.IsTrue(Run("remove property Age from Customer"), LastOutput);
        Assert.DoesNotContain("Age", File.ReadAllText(BetaFile));
        CollectionAssert.AreEqual(alpha, File.ReadAllBytes(AlphaFile), "Alpha's Customer was changed");
    }

    [TestMethod]
    public void Rename_class_renames_only_the_selected_projects_class()
    {
        SelectBeta();
        var alpha = File.ReadAllBytes(AlphaFile);

        Assert.IsTrue(Run("rename class Customer to Client"), LastOutput);

        Assert.IsTrue(File.Exists(Path.Combine(_workspace, "Beta", "Client.cs")), LastOutput);
        Assert.IsFalse(File.Exists(BetaFile), LastOutput);
        CollectionAssert.AreEqual(alpha, File.ReadAllBytes(AlphaFile), "Alpha's Customer was changed");
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Alpha", "Client.cs")));
        // Alpha's own Customer does not use Beta's, so it is not named as a file still using it.
        Assert.DoesNotContain(Path.Combine("Alpha", "Customer.cs"), LastOutput, LastOutput);
    }

    [TestMethod]
    public void Delete_class_moves_only_the_selected_projects_file()
    {
        SelectBeta();

        Assert.IsTrue(Run("delete class Customer"), LastOutput);

        Assert.IsFalse(File.Exists(BetaFile), LastOutput);
        Assert.IsTrue(File.Exists(AlphaFile), "Alpha's Customer was deleted too: " + LastOutput);
        Assert.IsFalse(Directory.Exists(Path.Combine(_workspace, "Alpha", "DeletedClasses")), LastOutput);
    }

    [TestMethod]
    [DataRow("add property Age int to Customer")]
    [DataRow("add method Save to Customer")]
    [DataRow("remove property Id from Customer")]
    [DataRow("rename class Customer to Client")]
    [DataRow("delete class Customer")]
    public void With_no_project_selected_two_files_of_the_name_are_refused_and_named(string input)
    {
        var alpha = File.ReadAllBytes(AlphaFile);
        var beta = File.ReadAllBytes(BetaFile);

        Run(input);

        CollectionAssert.AreEqual(alpha, File.ReadAllBytes(AlphaFile), LastOutput);
        CollectionAssert.AreEqual(beta, File.ReadAllBytes(BetaFile), LastOutput);
        StringAssert.Contains(LastOutput, Path.Combine("Alpha", "Customer.cs"));
        StringAssert.Contains(LastOutput, Path.Combine("Beta", "Customer.cs"));
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Alpha", "Client.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Beta", "Client.cs")));
    }
}

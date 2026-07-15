using NaturalLanguageBridge;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

// These tests exercise the static ProjectManager.CurrentProject selection, so
// they must not run in parallel with each other.
[TestClass]
[DoNotParallelize]
public sealed class SaveWorkspaceReproTests
{
    [TestMethod]
    public void CreateClass_SavesIntoSpecifiedWorkspace_ParentHasCsproj()
    {
        // A parent directory that already contains an unrelated .csproj.
        var parent = Path.Combine(
            Path.GetTempPath(),
            "cg_parent_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        File.WriteAllText(Path.Combine(parent, "Unrelated.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        // The user's SPECIFIED workspace is a subfolder of that parent.
        var workspace = Path.Combine(parent, "MyWorkspace");
        Directory.CreateDirectory(workspace);

        ProjectManager.CurrentProject = null;

        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        ProjectMetaData? currentProject = null;

        CommandFront.Run(
            input,
            ref currentProject,
            configuredRules: new List<PropertyRule>(),
            workspace: workspace,
            setLastCreatedProject: p => ProjectManager.CurrentProject = p);

        var underWorkspace = Directory.GetFiles(
            workspace, "Employee.cs", SearchOption.AllDirectories);
        var underParentOnly = Directory.GetFiles(
            parent, "Employee.cs", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(workspace)).ToArray();

        Console.WriteLine("WORKSPACE: " + workspace);
        Console.WriteLine("UNDER_WORKSPACE: " + (underWorkspace.Length == 0 ? "<none>" : string.Join(", ", underWorkspace)));
        Console.WriteLine("OUTSIDE_WORKSPACE: " + (underParentOnly.Length == 0 ? "<none>" : string.Join(", ", underParentOnly)));

        Assert.IsNotEmpty(underWorkspace,
            $"Employee.cs was NOT saved under the specified workspace '{workspace}'. " +
            $"Outside-workspace hits: [{string.Join(", ", underParentOnly)}]");
    }

    [TestMethod]
    public void CreateClass_StaleCurrentProjectOutsideWorkspace_IsIgnored()
    {
        // A "stale" project selected in a previous session, living in its own
        // directory that is NOT under the workspace the user now specifies.
        var staleProjectDir = Path.Combine(
            Path.GetTempPath(),
            "cg_stale_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staleProjectDir);
        var staleCsproj = Path.Combine(staleProjectDir, "OldProject.csproj");
        File.WriteAllText(staleCsproj,
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        ProjectManager.CurrentProject = new ProjectMetaData
        {
            Success = true,
            ProjectName = "OldProject",
            ProjectPath = staleCsproj,
            CsProjPath = staleCsproj,
            Message = "stale"
        };

        // The workspace the user specifies now — completely separate.
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "cg_ws_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        ProjectMetaData? currentProject = null;

        CommandFront.Run(
            input,
            ref currentProject,
            configuredRules: new List<PropertyRule>(),
            workspace: workspace,
            setLastCreatedProject: p => ProjectManager.CurrentProject = p);

        var underWorkspace = Directory.GetFiles(
            workspace, "Employee.cs", SearchOption.AllDirectories);
        var inStaleProject = Directory.GetFiles(
            staleProjectDir, "Employee.cs", SearchOption.AllDirectories);

        Console.WriteLine("WORKSPACE: " + workspace);
        Console.WriteLine("UNDER_WORKSPACE: " + (underWorkspace.Length == 0 ? "<none>" : string.Join(", ", underWorkspace)));
        Console.WriteLine("IN_STALE_PROJECT: " + (inStaleProject.Length == 0 ? "<none>" : string.Join(", ", inStaleProject)));

        Assert.IsNotEmpty(underWorkspace,
            $"Employee.cs was NOT saved under the specified workspace '{workspace}'.");
        Assert.IsEmpty(inStaleProject,
            $"Employee.cs leaked into the stale project '{staleProjectDir}'.");
    }

    [TestMethod]
    public void ClearCurrentProjectIfOutside_DropsStaleSelection()
    {
        var staleDir = Path.Combine(
            Path.GetTempPath(), "cg_stale2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staleDir);
        var staleCsproj = Path.Combine(staleDir, "OldProject.csproj");
        File.WriteAllText(staleCsproj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        ProjectManager.CurrentProject = new ProjectMetaData
        {
            Success = true,
            ProjectName = "OldProject",
            ProjectPath = staleCsproj,
            CsProjPath = staleCsproj
        };

        var workspace = Path.Combine(
            Path.GetTempPath(), "cg_ws2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        ProjectManager.ClearCurrentProjectIfOutside(workspace);

        Assert.IsNull(ProjectManager.CurrentProject,
            "A selected project outside the workspace should be cleared at startup.");
    }

    [TestMethod]
    public void ClearCurrentProjectIfOutside_KeepsInWorkspaceSelection()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(), "cg_ws3_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(workspace, "Api");
        Directory.CreateDirectory(projectDir);
        var csproj = Path.Combine(projectDir, "Api.csproj");
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var selection = new ProjectMetaData
        {
            Success = true,
            ProjectName = "Api",
            ProjectPath = csproj,
            CsProjPath = csproj
        };
        ProjectManager.CurrentProject = selection;

        ProjectManager.ClearCurrentProjectIfOutside(workspace);

        Assert.AreSame(selection, ProjectManager.CurrentProject,
            "A selected project inside the workspace must be preserved.");
    }

    [TestMethod]
    public void CreateClass_CurrentProjectInsideWorkspace_IsHonored()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "cg_wsin_" + Guid.NewGuid().ToString("N"));
        // A real project nested inside the specified workspace.
        var projectDir = Path.Combine(workspace, "Api");
        Directory.CreateDirectory(projectDir);
        var csproj = Path.Combine(projectDir, "Api.csproj");
        File.WriteAllText(csproj,
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        ProjectManager.CurrentProject = new ProjectMetaData
        {
            Success = true,
            ProjectName = "Api",
            ProjectPath = csproj,
            CsProjPath = csproj,
            Message = "in-workspace"
        };

        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        ProjectMetaData? currentProject = null;

        CommandFront.Run(
            input,
            ref currentProject,
            configuredRules: new List<PropertyRule>(),
            workspace: workspace,
            setLastCreatedProject: p => ProjectManager.CurrentProject = p);

        var inProject = Directory.GetFiles(
            projectDir, "Employee.cs", SearchOption.AllDirectories);

        Console.WriteLine("PROJECT_DIR: " + projectDir);
        Console.WriteLine("IN_PROJECT: " + (inProject.Length == 0 ? "<none>" : string.Join(", ", inProject)));

        Assert.IsNotEmpty(inProject,
            "A project inside the workspace should still receive the generated class.");
    }

    [TestMethod]
    public void CreateClass_SavesIntoSpecifiedWorkspace()
    {
        // Fresh, isolated workspace that the user "specifies".
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "cg_repro_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        // No project selected — simulate a plain specified workspace.
        ProjectManager.CurrentProject = null;

        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        ProjectMetaData? currentProject = null;

        CommandFront.Run(
            input,
            ref currentProject,
            configuredRules: new List<PropertyRule>(),
            workspace: workspace,
            setLastCreatedProject: p => ProjectManager.CurrentProject = p);

        var found = Directory.Exists(workspace)
            ? Directory.GetFiles(workspace, "Employee.cs", SearchOption.AllDirectories)
            : Array.Empty<string>();

        Console.WriteLine("WORKSPACE: " + workspace);
        Console.WriteLine("FOUND: " + (found.Length == 0 ? "<none>" : string.Join(", ", found)));

        Assert.IsNotEmpty(found,
            $"Employee.cs was not saved anywhere under the specified workspace '{workspace}'.");
    }
}

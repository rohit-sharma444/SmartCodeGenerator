using CodeGenerationEngine.Core;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Re-rooting a workspace: what gets registered, what gets dropped, and what follows the root.
/// </summary>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> because <c>ProjectManager</c> is static, process-wide
/// state. Two of these at once register into each other's registry.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class WorkspaceTests
{
    [TestMethod]
    public void A_folder_registers_every_project_under_it()
    {
        var root = NewTree("Api", "Core");

        var workspace = new Workspace(root, projectPathSet: false);
        var load = workspace.LoadFrom(root);

        Assert.IsTrue(load.Ok);
        Assert.AreEqual(2, load.ProjectCount);
        Assert.AreEqual(root, workspace.Root);
        Assert.IsNull(load.SolutionPath);
    }

    [TestMethod]
    public void A_solution_registers_only_the_projects_it_declares()
    {
        var root = NewTree("Api", "Core", "NotInTheSolution");
        var solution = Path.Combine(root, "App.slnx");

        File.WriteAllText(solution, """
            <Solution>
              <Project Path="Api/Api.csproj" />
              <Project Path="Core/Core.csproj" />
            </Solution>
            """);

        var workspace = new Workspace(root, projectPathSet: false);
        var load = workspace.LoadFrom(solution);

        Assert.IsTrue(load.Ok);
        Assert.AreEqual(2, load.ProjectCount);
        Assert.AreEqual(solution, load.SolutionPath);
        Assert.IsFalse(
            ProjectManager.Projects.Any(p => p.ProjectName == "NotInTheSolution"),
            "a project the solution does not declare must not be registered");
    }

    /// <summary>
    /// The workspace keeps the solution it loaded, not just the fact that it loaded one.
    /// </summary>
    /// <remarks>
    /// The chat page's tree names its root node from this. It used to go looking for a solution
    /// file under the root instead, which on a repository holding four of them named whichever
    /// sorted first — here, a project's own solution rather than the one that was opened.
    /// </remarks>
    [TestMethod]
    public void The_workspace_remembers_the_solution_it_was_loaded_from()
    {
        var root = NewTree("Api", "Core");
        var solution = Path.Combine(root, "App.slnx");

        File.WriteAllText(solution, """
            <Solution>
              <Project Path="Api/Api.csproj" />
            </Solution>
            """);

        var workspace = new Workspace(root, projectPathSet: false);

        Assert.IsNull(workspace.SolutionPath, "nothing loaded yet");

        workspace.LoadFrom(solution);

        Assert.AreEqual(solution, workspace.SolutionPath);
    }

    /// <summary>A folder is a folder, and must not be reported as some solution inside it.</summary>
    [TestMethod]
    public void Loading_a_folder_leaves_the_workspace_with_no_solution()
    {
        var root = NewTree("Api");

        File.WriteAllText(Path.Combine(root, "Api", "Api.slnx"), "<Solution />");

        var workspace = new Workspace(root, projectPathSet: false);

        workspace.LoadFrom(Path.Combine(root, "Api", "Api.slnx"));
        workspace.LoadFrom(root);

        Assert.IsNull(workspace.SolutionPath, "the folder load must clear the earlier solution");
    }

    [TestMethod]
    public void Projects_outside_the_new_root_are_dropped()
    {
        var first = NewTree("Old");
        var second = NewTree("New");

        var workspace = new Workspace(first, projectPathSet: false);
        workspace.LoadFrom(first);

        workspace.LoadFrom(second);

        Assert.IsFalse(
            ProjectManager.Projects.Any(p => p.ProjectName == "Old"),
            "the previous root's projects must not leak into the new one");
    }

    [TestMethod]
    public void A_current_project_outside_the_new_root_is_cleared()
    {
        var first = NewTree("Old");
        var second = NewTree("New");

        var workspace = new Workspace(first, projectPathSet: false);
        workspace.LoadFrom(first);
        ProjectManager.Select("Old");

        workspace.LoadFrom(second);

        Assert.IsNull(ProjectManager.CurrentProject);
    }

    [TestMethod]
    public void The_history_file_follows_the_root()
    {
        var first = NewTree("A");
        var second = NewTree("B");

        var workspace = new Workspace(first, projectPathSet: false);

        Assert.AreEqual(Path.Combine(first, ".codegen_history"), workspace.HistoryPath);

        workspace.LoadFrom(second);

        Assert.AreEqual(Path.Combine(second, ".codegen_history"), workspace.HistoryPath);
    }

    [TestMethod]
    public void Rules_are_reloaded_from_the_new_root()
    {
        var first = NewTree("A");
        var second = NewTree("B");

        File.WriteAllText(
            Path.Combine(second, "rules.txt"), "Name should not be blank");

        var workspace = new Workspace(first, projectPathSet: false);
        workspace.LoadFrom(first);

        Assert.IsEmpty(workspace.Rules);

        workspace.LoadFrom(second);

        Assert.IsNotEmpty(workspace.Rules);
    }

    [TestMethod]
    public void A_path_that_does_not_exist_is_reported_not_thrown()
    {
        var root = NewTree("A");
        var workspace = new Workspace(root, projectPathSet: false);

        var load = workspace.LoadFrom(Path.Combine(root, "nowhere"));

        Assert.IsFalse(load.Ok);
        Assert.IsNotNull(load.Problem);
        Assert.AreEqual(root, workspace.Root);
    }

    /// <summary>A temp directory with one folder and one .csproj per name.</summary>
    private static string NewTree(params string[] projects)
    {
        var root = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]);

        foreach (var name in projects)
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, name + ".csproj"),
                """<Project Sdk="Microsoft.NET.Sdk"></Project>""");
        }

        return root;
    }
}

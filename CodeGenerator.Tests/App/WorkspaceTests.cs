using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
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
    public void The_project_count_follows_projects_created_after_the_load()
    {
        // The chat header read "0 projects" after `create console project Shop` at the console,
        // while the tree beside it drew Shop: the count was set by LoadFrom and nowhere else.
        var root = NewTree("Api");
        var workspace = new Workspace(root, projectPathSet: false);
        workspace.LoadFrom(root);
        Assert.AreEqual(1, workspace.ProjectCount);

        var lib = Path.Combine(NewTree("Lib"), "Lib");
        var moved = Path.Combine(root, "Lib");
        Directory.Move(lib, moved);
        ProjectManager.Register(new ProjectMetaData
        {
            Success = true,
            ProjectPath = moved,
            CsProjPath = Path.Combine(moved, "Lib.csproj"),
            ProjectName = "Lib"
        });

        Assert.AreEqual(2, workspace.ProjectCount);
    }

    [TestMethod]
    public void The_project_count_of_a_workspace_never_loaded_counts_its_registered_projects()
    {
        // Startup registers without LoadFrom; a project registered elsewhere is not counted.
        var root = NewTree("Shop");
        var elsewhere = NewTree("Other");
        var workspace = new Workspace(root, projectPathSet: false);

        foreach (var (folder, name) in new[] { (root, "Shop"), (elsewhere, "Other") })
        {
            ProjectManager.Register(new ProjectMetaData
            {
                Success = true,
                ProjectPath = Path.Combine(folder, name),
                CsProjPath = Path.Combine(folder, name, name + ".csproj"),
                ProjectName = name
            });
        }

        Assert.AreEqual(1, workspace.ProjectCount);
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

    /// <summary>
    /// Startup selects a project in the workspace even when the registry file already knew it.
    /// </summary>
    /// <remarks>
    /// The registry is read back with no selection, and registering a project it already holds
    /// only refreshed the entry, so the second start of any workspace selected nothing and said
    /// "Found 1 project(s),  selected." with the name missing. The first start, where the project
    /// was new to the registry, selected it.
    /// </remarks>
    [TestMethod]
    public void A_project_already_in_the_registry_is_selected_when_nothing_is()
    {
        var root = NewTree("Shop");
        var csproj = Path.Combine(root, "Shop", "Shop.csproj");

        // What ProjectManager.LoadProject leaves: the project registered, nothing selected.
        ProjectManager.Register(new CodeGenerationEngine.Models.ProjectMetaData
        {
            Success = true,
            ProjectPath = Path.GetDirectoryName(csproj)!,
            CsProjPath = csproj,
            ProjectName = "Shop",
            Message = "Loaded"
        });
        ProjectManager.CurrentProject = null;

        var workspace = new Workspace(root, projectPathSet: false);
        workspace.LoadFrom(root);

        Assert.AreEqual("Shop", ProjectManager.CurrentProject?.ProjectName);
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
    public void Validation_rules_saved_in_a_workspace_are_loaded_with_it_and_replaced_on_re_root()
    {
        var first = NewTree("A");
        var second = NewTree("B");
        CodeGenerationEngine.Core.Validation.ValidationRuleFile.Write(
            CodeGenerationEngine.Core.Validation.ValidationRuleFile.PathFor(second),
            [new("Employee", CodeGenerationEngine.Core.Validation.ValidationCategory.Data, "Name is required")]);

        try
        {
            var workspace = new Workspace(first, projectPathSet: false);
            workspace.LoadFrom(first);
            Assert.IsEmpty(ValidationRegistry.Get("Employee"));

            workspace.LoadFrom(second);
            Assert.AreEqual("Name is required", ValidationRegistry.Get("Employee").Single().Describe());

            workspace.LoadFrom(first);
            Assert.IsEmpty(ValidationRegistry.Get("Employee"));
        }
        finally
        {
            ValidationRegistry.Clear();
        }
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

    /// <summary>
    /// A relative path is the workspace's: "Api" typed into the chat page's box is the Api folder
    /// of the workspace being worked on.
    /// </summary>
    /// <remarks>
    /// It was resolved against the process's current directory, which is wherever the console was
    /// started — the bin folder under Visual Studio, anywhere at all when PROJECTPATH chose the
    /// root — so "Api" said "There is nothing at" a path the user never meant.
    /// </remarks>
    [TestMethod]
    public void A_relative_folder_is_found_in_the_workspace()
    {
        var root = NewTree("Api", "Core");
        var workspace = new Workspace(root, projectPathSet: false);

        var load = workspace.LoadFrom("Api");

        Assert.IsTrue(load.Ok, load.Problem);
        Assert.AreEqual(Path.Combine(root, "Api"), workspace.Root);
        Assert.AreEqual(1, load.ProjectCount);
    }

    [TestMethod]
    public void A_relative_solution_and_a_sibling_folder_are_found_from_the_workspace()
    {
        var root = NewTree("Api", "Core");
        File.WriteAllText(Path.Combine(root, "App.slnx"), """
            <Solution>
              <Project Path="Api/Api.csproj" />
            </Solution>
            """);
        var workspace = new Workspace(Path.Combine(root, "Core"), projectPathSet: false);

        var solution = workspace.LoadFrom(Path.Combine("..", "App.slnx"));

        Assert.IsTrue(solution.Ok, solution.Problem);
        Assert.AreEqual(Path.Combine(root, "App.slnx"), workspace.SolutionPath);

        // The solution roots the workspace at its projects' folder, Api; Core is beside it.
        var sibling = workspace.LoadFrom(Path.Combine("..", "Core"));

        Assert.IsTrue(sibling.Ok, sibling.Problem);
        Assert.AreEqual(Path.Combine(root, "Core"), workspace.Root);
    }

    /// <summary>
    /// The current directory is still looked in when the workspace has nothing of that name, so a
    /// relative path that loaded before still loads.
    /// </summary>
    [TestMethod]
    public void A_relative_path_only_the_current_directory_has_still_loads()
    {
        var root = NewTree("Api");
        var name = "ws-cwd-" + Guid.NewGuid().ToString("N")[..8];
        var inCurrent = Path.GetFullPath(name);
        Directory.CreateDirectory(inCurrent);

        try
        {
            var workspace = new Workspace(root, projectPathSet: false);

            var load = workspace.LoadFrom(name);

            Assert.IsTrue(load.Ok, load.Problem);
            Assert.AreEqual(inCurrent, workspace.Root);
        }
        finally
        {
            Directory.Delete(inCurrent, recursive: true);
        }
    }

    [TestMethod]
    public void A_relative_path_found_nowhere_is_named_in_the_workspace()
    {
        var root = NewTree("Api");
        var workspace = new Workspace(root, projectPathSet: false);

        var load = workspace.LoadFrom("nowhere-" + Guid.NewGuid().ToString("N")[..8]);

        Assert.IsFalse(load.Ok);
        StringAssert.Contains(load.Problem, root, load.Problem);
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

using Bunit;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The project tree: what it reads, when it re-reads it, and what a click on it does.
/// </summary>
/// <remarks>
/// The tree exists to be traversed without running anything, so the tests that matter are the
/// ones about what it will and will not do on its own: it reads the same classes
/// <c>list classes</c> prints, it counts nothing until a node is open, and the only thing a click
/// produces is a command handed back to the page.
/// <para>
/// <see cref="DoNotParallelizeAttribute"/>: <see cref="ProjectManager"/> is static, and these
/// register projects into it.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ProjectTreeTests
{
    /// <summary>The tree and `list classes` must never be able to disagree.</summary>
    [TestMethod]
    public void The_classes_of_a_project_are_the_ones_the_command_lists()
    {
        var project = NewProject("Acme.Core", "Customer", "Invoice");

        var fromTree = new ProjectTree().ClassesOf(project).Select(c => c.Name);
        var fromCommand = ProjectManager.ClassesOf(project);

        CollectionAssert.AreEqual(fromCommand.ToList(), fromTree.ToList());
    }

    [TestMethod]
    public void A_class_carries_the_file_it_was_found_in()
    {
        var project = NewProject("Acme.Files", "Customer");

        var found = new ProjectTree().ClassesOf(project).Single();

        Assert.AreEqual("Customer", found.Name);
        Assert.IsTrue(File.Exists(found.File), found.File);
    }

    /// <summary>
    /// Build output must not win the race against the source it was compiled from.
    /// </summary>
    /// <remarks>
    /// The source lives under <c>Source</c>, not at the project root, and that is the whole
    /// fixture. <c>Directory.GetFiles</c> with <see cref="SearchOption.AllDirectories"/> yields a
    /// folder's own files before it recurses, and walks subfolders in name order — so a class at
    /// the root, or in a folder sorting before <c>obj</c>, is found first whether the filter is
    /// there or not. Written that way this test passed with the filter deleted.
    /// </remarks>
    [TestMethod]
    public void A_class_under_obj_is_not_the_one_the_tree_opens()
    {
        var project = NewProject("Acme.Stale");
        var folder = ProjectManager.ProjectFolderOf(project);

        var source = Path.Combine(folder, "Source");
        var obj = Path.Combine(folder, "obj");

        Directory.CreateDirectory(source);
        Directory.CreateDirectory(obj);

        File.WriteAllText(Path.Combine(source, "Customer.cs"), "public class Customer { }");
        File.WriteAllText(Path.Combine(obj, "Customer.cs"), "public class Customer { }");

        var found = new ProjectTree().ClassesOf(project).Single();

        StringAssert.Contains(found.File, "Source");
        StringAssert.DoesNotMatch(found.File, new System.Text.RegularExpressions.Regex(@"\\obj\\"));
    }

    [TestMethod]
    public void The_members_of_a_class_are_its_properties_then_its_methods()
    {
        var project = NewProject("Acme.Members");
        var folder = ProjectManager.ProjectFolderOf(project);

        File.WriteAllText(
            Path.Combine(folder, "Customer.cs"),
            """
            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }

                public bool Validate() { return true; }
            }
            """);

        var tree = new ProjectTree();
        var members = tree.MembersOf(tree.ClassesOf(project).Single());

        CollectionAssert.AreEqual(
            new List<string> { "Id", "Name", "Validate" },
            members.Select(m => m.Name).ToList());

        CollectionAssert.AreEqual(
            new List<bool> { false, false, true },
            members.Select(m => m.IsMethod).ToList());

        Assert.AreEqual("int", members[0].Type);
        Assert.AreEqual("bool", members[2].Type);
    }

    /// <summary>
    /// A command has run, so what is on disk may have moved.
    /// </summary>
    /// <remarks>
    /// The whole reason the cache exists is that nothing beneath it caches anything — every read
    /// is a fresh directory walk. A cache that never let go would show a class the reader had just
    /// deleted.
    /// </remarks>
    [TestMethod]
    public void Forgetting_makes_the_tree_read_the_folder_again()
    {
        var project = NewProject("Acme.Fresh", "Customer");
        var folder = ProjectManager.ProjectFolderOf(project);
        var tree = new ProjectTree();

        Assert.HasCount(1, tree.ClassesOf(project));

        File.WriteAllText(Path.Combine(folder, "Invoice.cs"), "public class Invoice { }");

        Assert.HasCount(1, tree.ClassesOf(project), "cached until something says otherwise");

        tree.Forget();

        Assert.HasCount(2, tree.ClassesOf(project));
    }

    [TestMethod]
    public void Clicking_a_class_asks_for_show_class_and_runs_nothing_itself()
    {
        using var context = new BunitContext();
        var project = NewProject("Acme.Click", "Customer");
        string? asked = null;

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Projects, new List<ProjectMetaData> { project })
            .Add(c => c.OnCommand, (string s) => asked = s));

        view.Find(".tree-twisty").Click();
        view.Find(".tree-open-class").Click();

        Assert.AreEqual("show class Customer", asked);
    }

    [TestMethod]
    public void Clicking_use_asks_for_use_project()
    {
        using var context = new BunitContext();
        var project = NewProject("Acme.Use", "Customer");
        string? asked = null;

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Projects, new List<ProjectMetaData> { project })
            .Add(c => c.OnCommand, (string s) => asked = s));

        view.Find(".tree-use").Click();

        Assert.AreEqual("use project Acme.Use", asked);
    }

    /// <summary>
    /// The count is the promise the header would not make.
    /// </summary>
    /// <remarks>
    /// ContextStrip draws no class count because keeping one true means walking every project
    /// after every turn. The tree may draw one only for a node it has just read — so a collapsed
    /// project must carry no number at all.
    /// </remarks>
    [TestMethod]
    public void A_collapsed_project_shows_no_class_count()
    {
        using var context = new BunitContext();
        var project = NewProject("Acme.Count", "Customer", "Invoice");

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Projects, new List<ProjectMetaData> { project }));

        Assert.IsEmpty(view.FindAll(".tree-count"));

        view.Find(".tree-twisty").Click();

        Assert.AreEqual("2", view.Find(".tree-count").TextContent.Trim());
    }

    /// <summary>While a command is in flight the tree must not be able to start a second.</summary>
    [TestMethod]
    public void Nothing_can_be_run_from_the_tree_while_a_command_is_running()
    {
        using var context = new BunitContext();
        var project = NewProject("Acme.Busy", "Customer");
        var asked = 0;

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Projects, new List<ProjectMetaData> { project })
            .Add(c => c.Disabled, true)
            .Add(c => c.OnCommand, (string _) => asked++));

        view.Find(".tree-twisty").Click();

        Assert.IsTrue(view.Find(".tree-open-class").HasAttribute("disabled"));

        view.Find(".tree-open-class").Click();

        Assert.AreEqual(0, asked);
    }

    [TestMethod]
    public void With_no_projects_the_tree_says_where_to_load_one()
    {
        using var context = new BunitContext();

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Projects, new List<ProjectMetaData>()));

        StringAssert.Contains(view.Find(".tree-empty").TextContent, "Change");
    }

    /// <summary>A project folder with the named classes in it, registered the way the tool does.</summary>
    private static ProjectMetaData NewProject(string name, params string[] classes)
    {
        var folder = Path.Combine(
            Path.GetTempPath(), "tree-" + Guid.NewGuid().ToString("N")[..8], name);

        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, name + ".csproj"), "<Project />");

        foreach (var @class in classes)
            File.WriteAllText(Path.Combine(folder, @class + ".cs"), $"public class {@class} {{ }}");

        return new ProjectMetaData
        {
            Success = true,
            ProjectName = name,
            ProjectPath = folder,
            CsProjPath = Path.Combine(folder, name + ".csproj"),
        };
    }
}

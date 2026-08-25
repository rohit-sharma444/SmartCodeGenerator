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

    /// <summary>
    /// A deleted class must not be the file a live one resolves to.
    /// </summary>
    /// <remarks>
    /// <c>ClassNamesIn</c> skips <c>DeletedClasses</c> and <c>FileOf</c> did not, so a class with a
    /// backup of an older namesake beside it resolved to the backup — <c>DeletedClasses</c> sorts
    /// before nearly every folder a project has, and the first match by name wins. Flat, that
    /// showed only as the wrong members. Nested, the tree grows a <c>DeletedClasses</c> folder
    /// holding a class that is not deleted at all.
    /// </remarks>
    [TestMethod]
    public void A_class_backed_up_under_DeletedClasses_is_not_the_one_the_tree_opens()
    {
        var project = NewProjectWithFiles(
            "Acme.Deleted", "DeletedClasses/Customer.cs", "Models/Customer.cs");

        var tree = new ProjectTree();

        StringAssert.Contains(tree.ClassesOf(project).Single().File, "Models");

        CollectionAssert.AreEqual(
            new List<string> { "Models" },
            tree.FolderOf(project).Folders.Select(f => f.Name).ToList());
    }

    /// <summary>
    /// The shape Solution Explorer draws: a project is its folders, not one flat list.
    /// </summary>
    /// <remarks>
    /// Nothing new is read to build this. Every class already carries the file it was found in,
    /// so the folder it sits in is that path relative to the project — which keeps the tree and
    /// <c>list classes</c> reading the same enumeration.
    /// </remarks>
    [TestMethod]
    public void A_project_is_nested_by_the_folders_its_files_are_in()
    {
        var project = NewProjectWithFiles(
            "Acme.Nested",
            "Program.cs",
            "Core/ProjectManager.cs",
            "Models/MethodSignature.cs",
            "Web/Chat/ChatSession.cs");

        var root = new ProjectTree().FolderOf(project);

        CollectionAssert.AreEqual(
            new List<string> { "Core", "Models", "Web" },
            root.Folders.Select(f => f.Name).ToList());

        CollectionAssert.AreEqual(
            new List<string> { "Program" },
            root.Classes.Select(c => c.Name).ToList());

        var web = root.Folders.Single(f => f.Name == "Web");

        Assert.IsEmpty(web.Classes, "Web holds a folder, not a file");
        Assert.AreEqual("Chat", web.Folders.Single().Name);
        Assert.AreEqual("ChatSession", web.Folders.Single().Classes.Single().Name);
    }

    [TestMethod]
    public void Folders_are_in_name_order_and_so_are_the_classes_beside_them()
    {
        var project = NewProjectWithFiles(
            "Acme.Order", "Zebra.cs", "Apple.cs", "Zulu/Thing.cs", "Alpha/Other.cs");

        var root = new ProjectTree().FolderOf(project);

        CollectionAssert.AreEqual(
            new List<string> { "Alpha", "Zulu" },
            root.Folders.Select(f => f.Name).ToList());

        CollectionAssert.AreEqual(
            new List<string> { "Apple", "Zebra" },
            root.Classes.Select(c => c.Name).ToList());
    }

    /// <summary>
    /// A class with no file has no folder, and must not go missing over it.
    /// </summary>
    /// <remarks>
    /// The tree must list exactly what <c>list classes</c> lists. Dropping a class the folder
    /// walk could not place would make the sidebar quietly shorter than the command.
    /// </remarks>
    [TestMethod]
    public void A_class_whose_file_was_never_found_sits_at_the_project_root()
    {
        var nested = ProjectTree.Nest(
            Path.Combine(Path.GetTempPath(), "no-such-project"),
            [new TreeClass("Ghost", string.Empty)]);

        Assert.IsEmpty(nested.Folders);
        Assert.AreEqual("Ghost", nested.Classes.Single().Name);
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

        view.Find(".tree-project .tree-twisty").Click();
        view.Find(".tree-open-class").Click();

        Assert.AreEqual("show class Customer", asked);
    }

    /// <summary>
    /// Solution Explorer opens on the solution, and so does this.
    /// </summary>
    /// <remarks>
    /// The count is the registry's own length — no folder is walked to produce it — which is why
    /// this node may carry a number when a collapsed project may not.
    /// </remarks>
    [TestMethod]
    public void The_tree_is_rooted_in_the_solution_it_was_loaded_from()
    {
        using var context = new BunitContext();
        var root = NewRoot("Acme.Repo", "Acme.slnx");
        var project = NewProject("Acme.Core", "Customer");

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Root, root)
            .Add(c => c.Solution, Path.Combine(root, "Acme.slnx"))
            .Add(c => c.Projects, new List<ProjectMetaData> { project }));

        Assert.AreEqual(
            "Solution 'Acme' (1 project)",
            view.Find(".tree-solution .tree-name").TextContent.Trim());
    }

    /// <summary>
    /// A folder can be loaded without a solution in it, and the root must not claim otherwise.
    /// </summary>
    [TestMethod]
    public void With_no_solution_file_the_root_says_it_is_a_folder()
    {
        using var context = new BunitContext();
        var root = NewRoot("Acme.Loose", solution: null);

        // A solution file lying about under the root, which is not the same thing as one loaded.
        File.WriteAllText(Path.Combine(root, "Stray.slnx"), string.Empty);

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Root, root)
            .Add(c => c.Projects, new List<ProjectMetaData>
            {
                NewProject("Acme.One"), NewProject("Acme.Two"),
            }));

        Assert.AreEqual(
            "Folder 'Acme.Loose' (2 projects)",
            view.Find(".tree-solution .tree-name").TextContent.Trim());
    }

    /// <summary>
    /// The root is named after the solution that was loaded, and never after one merely lying
    /// about under the root.
    /// </summary>
    /// <remarks>
    /// This tree went looking for a solution file once, taking the first one in the root or in a
    /// folder just below it. Run against this repository — which holds four, one of them a
    /// project's own — it named <c>CodeGenerator</c>, a solution nobody had opened. There is only
    /// one correct answer available and it is the one the workspace already has.
    /// </remarks>
    [TestMethod]
    public void The_root_is_named_after_the_solution_that_was_loaded()
    {
        var root = NewRoot("Acme.Repo", solution: null);

        Directory.CreateDirectory(Path.Combine(root, "Inner"));
        File.WriteAllText(Path.Combine(root, "Inner", "Inner.slnx"), string.Empty);

        var folder = ProjectTree.RootOf(root, solution: null);

        Assert.IsFalse(folder.IsSolution, "a folder was loaded, whatever is lying under it");
        Assert.AreEqual("Acme.Repo", folder.Name);

        var loaded = ProjectTree.RootOf(root, Path.Combine(root, "Acme.slnx"));

        Assert.IsTrue(loaded.IsSolution);
        Assert.AreEqual("Acme", loaded.Name);
    }

    [TestMethod]
    public void Collapsing_the_solution_hides_every_project_under_it()
    {
        using var context = new BunitContext();

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Root, NewRoot("Acme.Repo", "Acme.slnx"))
            .Add(c => c.Projects, new List<ProjectMetaData> { NewProject("Acme.Core") }));

        Assert.HasCount(1, view.FindAll(".tree-project"), "open on arrival");

        view.Find(".tree-solution .tree-twisty").Click();

        Assert.IsEmpty(view.FindAll(".tree-project"));
    }

    /// <summary>
    /// Every kind of node is drawn as what it is, the way Solution Explorer draws it.
    /// </summary>
    /// <remarks>
    /// One assertion per kind, because the failure being guarded against is one node type quietly
    /// borrowing another's glyph — which reads as a folder full of folders, or a property that
    /// looks like a method.
    /// </remarks>
    [TestMethod]
    public void Every_kind_of_node_is_drawn_as_what_it_is()
    {
        using var context = new BunitContext();
        var project = NewProject("Acme.Icons");
        var folder = Path.Combine(ProjectManager.ProjectFolderOf(project), "Core");

        Directory.CreateDirectory(folder);

        File.WriteAllText(
            Path.Combine(folder, "Customer.cs"),
            """
            public class Customer
            {
                public int Id { get; set; }

                public bool Validate() { return true; }
            }
            """);

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Root, NewRoot("Acme.Repo", "Acme.slnx"))
            .Add(c => c.Projects, new List<ProjectMetaData> { project }));

        Assert.HasCount(1, view.FindAll(".tree-icon-solution"));
        Assert.HasCount(1, view.FindAll(".tree-icon-project"));

        view.Find(".tree-project .tree-twisty").Click();

        Assert.HasCount(1, view.FindAll(".tree-icon-folder"), "shut, and drawn shut");

        view.Find(".tree-folder .tree-twisty").Click();

        Assert.HasCount(1, view.FindAll(".tree-icon-folder-open"));
        Assert.IsEmpty(view.FindAll(".tree-icon-folder"), "open, so not drawn shut as well");
        Assert.HasCount(1, view.FindAll(".tree-icon-class"));

        view.Find(".tree-class .tree-twisty").Click();

        Assert.HasCount(1, view.FindAll(".tree-icon-property"));
        Assert.HasCount(1, view.FindAll(".tree-icon-method"));
    }

    /// <summary>Solution Explorer's own order: the folders, then the files beside them.</summary>
    [TestMethod]
    public void A_project_opens_to_its_folders_before_its_files()
    {
        using var context = new BunitContext();
        var project = NewProjectWithFiles("Acme.Rows", "Program.cs", "Core/Customer.cs");

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Projects, new List<ProjectMetaData> { project }));

        view.Find(".tree-project .tree-twisty").Click();

        CollectionAssert.AreEqual(
            new List<string> { "Core", "Program" },
            view.FindAll(".tree-folder .tree-name, .tree-class .tree-name")
                .Select(e => e.TextContent.Trim())
                .ToList());
    }

    /// <summary>A folder is a node like any other: nothing inside it is drawn until it is open.</summary>
    [TestMethod]
    public void A_folder_opens_to_what_is_inside_it()
    {
        using var context = new BunitContext();
        var project = NewProjectWithFiles("Acme.Inside", "Core/Customer.cs");

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Projects, new List<ProjectMetaData> { project }));

        view.Find(".tree-project .tree-twisty").Click();

        Assert.IsEmpty(view.FindAll(".tree-class"), "closed, so the class inside is not drawn");

        view.Find(".tree-folder .tree-twisty").Click();

        Assert.AreEqual("Customer", view.Find(".tree-class .tree-name").TextContent.Trim());
    }

    /// <summary>
    /// Two projects, one folder name. Opening one must not open the other.
    /// </summary>
    /// <remarks>
    /// Which is why the open set is keyed on the absolute folder: nearly every project in a
    /// solution has a <c>Core</c> or a <c>Models</c>, and both the bare name and the path relative
    /// to the project are the same string in each of them.
    /// </remarks>
    [TestMethod]
    public void Two_projects_with_the_same_folder_name_open_independently()
    {
        using var context = new BunitContext();
        var one = NewProjectWithFiles("Acme.One", "Core/First.cs");
        var two = NewProjectWithFiles("Acme.Two", "Core/Second.cs");

        var view = context.Render<ProjectTreeView>(p => p
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.Projects, new List<ProjectMetaData> { one, two }));

        // Re-found between the clicks: the first one re-renders the tree, and an element held from
        // before that has no handler to click any more.
        view.FindAll(".tree-project .tree-twisty")[0].Click();
        view.FindAll(".tree-project .tree-twisty")[1].Click();

        view.FindAll(".tree-folder .tree-twisty")[0].Click();

        CollectionAssert.AreEqual(
            new List<string> { "First" },
            view.FindAll(".tree-class .tree-name").Select(e => e.TextContent.Trim()).ToList());
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

        Assert.IsEmpty(view.FindAll(".tree-project .tree-count"));

        view.Find(".tree-project .tree-twisty").Click();

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

        view.Find(".tree-project .tree-twisty").Click();

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

    /// <summary>A workspace root, with a solution file in it or without one.</summary>
    private static string NewRoot(string name, string? solution)
    {
        var root = Path.Combine(
            Path.GetTempPath(), "tree-root-" + Guid.NewGuid().ToString("N")[..8], name);

        Directory.CreateDirectory(root);

        if (solution is not null)
            File.WriteAllText(Path.Combine(root, solution), string.Empty);

        return root;
    }

    /// <summary>
    /// A project whose files sit where the paths say, folders and all.
    /// </summary>
    /// <remarks>
    /// Paths are given with forward slashes and combined a segment at a time, so the fixture reads
    /// as the tree it describes.
    /// </remarks>
    private static ProjectMetaData NewProjectWithFiles(string name, params string[] paths)
    {
        var project = NewProject(name);
        var folder = ProjectManager.ProjectFolderOf(project);

        foreach (var path in paths)
        {
            var file = Path.Combine([folder, .. path.Split('/')]);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            File.WriteAllText(
                file, $"public class {Path.GetFileNameWithoutExtension(file)} {{ }}");
        }

        return project;
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

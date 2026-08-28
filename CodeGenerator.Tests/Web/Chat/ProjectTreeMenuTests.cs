using Bunit;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The right-click that opens the validation page. The menu is one item because there is one
/// thing to do; it exists at all because a right-click that navigates without asking is a
/// right-click people learn to avoid.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ProjectTreeMenuTests
{
    private static TreeFolder OneClass(string name, string file) =>
        new(Name: "Models", Path: "/models", Folders: [], Classes: [new TreeClass(name, file)]);

    [TestMethod]
    public void Right_clicking_a_class_raises_it()
    {
        using var context = new BunitContext();

        TreeClass? raised = null;

        var view = context.Render<TreeFolderView>(p => p
            .Add(c => c.Folder, OneClass("Employee", @"C:\ws\Employee.cs"))
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.OpenFolders, new HashSet<string>())
            .Add(c => c.OpenClasses, new HashSet<string>())
            .Add(c => c.OnValidate, (TreeClass entry) => raised = entry));

        view.Find(".tree-class").ContextMenu();

        Assert.IsNotNull(raised);
        Assert.AreEqual("Employee", raised!.Name);
    }

    [TestMethod]
    public void A_disabled_tree_raises_nothing()
    {
        using var context = new BunitContext();

        TreeClass? raised = null;

        var view = context.Render<TreeFolderView>(p => p
            .Add(c => c.Folder, OneClass("Employee", @"C:\ws\Employee.cs"))
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.OpenFolders, new HashSet<string>())
            .Add(c => c.OpenClasses, new HashSet<string>())
            .Add(c => c.Disabled, true)
            .Add(c => c.OnValidate, (TreeClass entry) => raised = entry));

        view.Find(".tree-class").ContextMenu();

        Assert.IsNull(raised, "a command is in flight; the tree must start nothing");
    }

    /// <summary>
    /// A class the tree could not find a file for cannot be validated.
    /// </summary>
    /// <remarks>
    /// The page needs the file, not the name - so raising a nameless-file entry would open a page
    /// that can only refuse. The twisty is already disabled for the same reason.
    /// </remarks>
    [TestMethod]
    public void A_class_with_no_file_raises_nothing()
    {
        using var context = new BunitContext();

        TreeClass? raised = null;

        var view = context.Render<TreeFolderView>(p => p
            .Add(c => c.Folder, OneClass("Ghost", string.Empty))
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.OpenFolders, new HashSet<string>())
            .Add(c => c.OpenClasses, new HashSet<string>())
            .Add(c => c.OnValidate, (TreeClass entry) => raised = entry));

        view.Find(".tree-class").ContextMenu();

        Assert.IsNull(raised);
    }
}

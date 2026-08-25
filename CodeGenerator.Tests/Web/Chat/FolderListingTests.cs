using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// What the solution picker shows for a directory.
/// </summary>
/// <remarks>
/// Listed by the host process rather than the browser: an <c>&lt;input type="file"&gt;</c> never
/// gives the server a real path, and a native dialog would open behind the browser.
/// </remarks>
[TestClass]
public class FolderListingTests
{
    [TestMethod]
    public void Lists_subfolders_and_solution_files_only()
    {
        var dir = NewDir();
        Directory.CreateDirectory(Path.Combine(dir, "Api"));
        File.WriteAllText(Path.Combine(dir, "App.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(dir, "Old.sln"), "");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "");

        var entries = FolderListing.For(dir);

        Assert.IsTrue(entries.Any(e => e.Name == "Api" && e.IsDirectory));
        Assert.IsTrue(entries.Any(e => e.Name == "App.slnx" && !e.IsDirectory));
        Assert.IsTrue(entries.Any(e => e.Name == "Old.sln"));
        Assert.IsFalse(entries.Any(e => e.Name == "notes.txt"));
    }

    [TestMethod]
    public void Solution_files_come_before_folders()
    {
        // The thing you came to click is the thing at the top.
        var dir = NewDir();
        Directory.CreateDirectory(Path.Combine(dir, "Api"));
        File.WriteAllText(Path.Combine(dir, "App.slnx"), "<Solution />");

        Assert.IsFalse(FolderListing.For(dir)[0].IsDirectory);
    }

    [TestMethod]
    public void Build_output_folders_are_not_offered()
    {
        // bin and obj are never where a solution is, and they are the two folders every project
        // has. Offering them is offering noise.
        var dir = NewDir();
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        Directory.CreateDirectory(Path.Combine(dir, "src"));

        var names = FolderListing.For(dir).Select(e => e.Name).ToList();

        Assert.Contains("src", names);
        Assert.DoesNotContain("bin", names);
        Assert.DoesNotContain("obj", names);
    }

    [TestMethod]
    public void A_directory_that_is_gone_lists_nothing_rather_than_throwing()
    {
        Assert.IsEmpty(FolderListing.For(Path.Combine(Path.GetTempPath(), "not-here-at-all")));
    }

    [TestMethod]
    public void The_parent_of_a_drive_root_is_null()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.IsNull(FolderListing.ParentOf(root));
        Assert.IsNotNull(FolderListing.ParentOf(NewDir()));
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

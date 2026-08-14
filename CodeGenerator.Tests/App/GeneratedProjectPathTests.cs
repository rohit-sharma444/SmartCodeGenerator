using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Where a generated project lands. One definition, shared by the scaffolder that writes it
/// and the guard that asks before overwriting it — a guard checking a different folder than
/// the scaffolder writes to would simply stop firing, silently.
/// </summary>
[TestClass]
public class GeneratedProjectPathTests
{
    private const string Workspace = @"D:\Work";

    [TestMethod]
    public void ProjectName_IsTheAppNamePlusTheTargetSuffix()
    {
        foreach (var key in TargetScaffolders.Keys)
        {
            var target = TargetScaffolders.ByKey[key];

            Assert.AreEqual(
                "Shop" + target.FolderSuffix,
                GeneratedProjectPath.ProjectName("Shop", target),
                $"'{key}'");
        }
    }

    [TestMethod]
    public void Wpf_KeepsTheUnsuffixedName()
    {
        // WPF's suffix is empty so its output folder is unchanged from the earliest versions.
        Assert.AreEqual("Shop", GeneratedProjectPath.ProjectName("Shop", TargetScaffolders.Default));
        Assert.AreEqual(@"D:\Work\Shop", GeneratedProjectPath.Folder(Workspace, "Shop", TargetScaffolders.Default));
    }

    [TestMethod]
    public void Folder_SitsDirectlyUnderTheWorkspace()
    {
        Assert.AreEqual(
            @"D:\Work\Shop.Console",
            GeneratedProjectPath.Folder(Workspace, "Shop", TargetScaffolders.ByKey["console"]));
    }

    [TestMethod]
    public void CsprojPath_IsNamedAfterItsFolderForEveryTarget()
    {
        foreach (var key in TargetScaffolders.Keys)
        {
            var target = TargetScaffolders.ByKey[key];
            var folder = GeneratedProjectPath.Folder(Workspace, "Shop", target);

            Assert.AreEqual(
                Path.Combine(folder, Path.GetFileName(folder) + ".csproj"),
                GeneratedProjectPath.CsprojPath(Workspace, "Shop", target),
                $"'{key}'");
        }
    }

    [TestMethod]
    public void Exists_IsFalseWhenNothingIsThere()
    {
        Assert.IsFalse(GeneratedProjectPath.Exists(
            Path.Combine(Path.GetTempPath(), "scg-no-such-workspace-" + Guid.NewGuid().ToString("N")),
            "Shop",
            TargetScaffolders.Default));
    }

    [TestMethod]
    public void Exists_IsTrueOnlyOnceTheCsprojIsThere()
    {
        // The csproj, not the folder: the scaffolder keys its reuse decision on exactly that
        // file, so an empty folder left behind by a failed run must not read as a project.
        var workspace = Path.Combine(Path.GetTempPath(), "scg-" + Guid.NewGuid().ToString("N"));
        var target = TargetScaffolders.Default;

        try
        {
            Directory.CreateDirectory(GeneratedProjectPath.Folder(workspace, "Shop", target));

            Assert.IsFalse(GeneratedProjectPath.Exists(workspace, "Shop", target));

            File.WriteAllText(GeneratedProjectPath.CsprojPath(workspace, "Shop", target), "<Project/>");

            Assert.IsTrue(GeneratedProjectPath.Exists(workspace, "Shop", target));
        }
        finally
        {
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, recursive: true);
        }
    }
}

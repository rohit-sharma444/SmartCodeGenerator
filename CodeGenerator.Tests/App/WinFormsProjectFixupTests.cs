using System.IO;
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsProjectFixupTests
{
    private static string NewProjectFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wffixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Form1.cs"), "// template");
        File.WriteAllText(Path.Combine(folder, "Form1.Designer.cs"), "// template");
        File.WriteAllText(Path.Combine(folder, "Program.cs"), "// ours, overwritten already");
        return folder;
    }

    [TestMethod]
    public void Apply_DeletesTheTemplatesForm()
    {
        var folder = NewProjectFolder();
        try
        {
            WinFormsProjectFixup.Apply(folder);

            Assert.IsFalse(File.Exists(Path.Combine(folder, "Form1.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "Form1.Designer.cs")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_LeavesOurOwnFilesAlone()
    {
        var folder = NewProjectFolder();
        try
        {
            WinFormsProjectFixup.Apply(folder);

            // Program.cs is ours (it overwrote the template's), so the fixup must not touch it.
            Assert.IsTrue(File.Exists(Path.Combine(folder, "Program.cs")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_IsIdempotentAndSurvivesAMissingForm()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wffixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            // Newer SDK templates may not emit Form1.resx at all. A fixup that throws on a file
            // that was never there would fail every generation.
            WinFormsProjectFixup.Apply(folder);
            WinFormsProjectFixup.Apply(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

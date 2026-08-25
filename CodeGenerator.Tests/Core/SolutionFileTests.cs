using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Reading a solution, and deciding where the workspace it implies actually starts.
/// </summary>
/// <remarks>
/// The root is the interesting half. This repository's own .slnx points every path at `../`, so a
/// root taken from the solution file's own folder contains none of its projects — and every
/// command afterwards would be right about a wrong tree.
/// </remarks>
[TestClass]
public class SolutionFileTests
{
    [TestMethod]
    public void Recognizes_both_solution_extensions()
    {
        Assert.IsTrue(SolutionFile.IsSolution(@"C:\x\App.sln"));
        Assert.IsTrue(SolutionFile.IsSolution(@"C:\x\App.slnx"));
        Assert.IsFalse(SolutionFile.IsSolution(@"C:\x\App.csproj"));
        Assert.IsFalse(SolutionFile.IsSolution(@"C:\x"));
        Assert.IsFalse(SolutionFile.IsSolution(null));
    }

    [TestMethod]
    public void Reads_an_slnx_and_resolves_its_paths()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "App.slnx");

        File.WriteAllText(path, """
            <Solution>
              <Project Path="Api/Api.csproj" />
              <Project Path="Core/Core.csproj" />
            </Solution>
            """);

        var read = SolutionFile.Read(path);

        Assert.HasCount(2, read.ProjectFiles);
        Assert.Contains(Path.Combine(dir, "Api", "Api.csproj"), read.ProjectFiles);
        Assert.AreEqual(path, read.SolutionPath);
    }

    [TestMethod]
    public void Reads_a_classic_sln_and_skips_solution_folders()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "App.sln");

        // A solution folder is a Project line whose path is not a project file. It must not
        // become a registered project.
        File.WriteAllText(path, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Api", "Api\Api.csproj", "{1}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Docs", "Docs", "{2}"
            EndProject
            """);

        var read = SolutionFile.Read(path);

        Assert.HasCount(1, read.ProjectFiles);
        Assert.AreEqual(Path.Combine(dir, "Api", "Api.csproj"), read.ProjectFiles[0]);
    }

    [TestMethod]
    public void Roots_at_the_common_ancestor_not_the_solution_folder()
    {
        // The shape of this repository's own SmartCodeGenerator.slnx: the solution sits in a
        // folder of its own and every project is a sibling of that folder.
        var repo = NewDir();
        var solutionDir = Path.Combine(repo, "Solution");
        Directory.CreateDirectory(solutionDir);

        var path = Path.Combine(solutionDir, "App.slnx");

        File.WriteAllText(path, """
            <Solution>
              <Project Path="../Api/Api.csproj" />
              <Project Path="../Core/Core.csproj" />
            </Solution>
            """);

        var read = SolutionFile.Read(path);

        Assert.AreEqual(repo, read.Root);
        Assert.AreNotEqual(solutionDir, read.Root);
    }

    [TestMethod]
    public void A_single_project_roots_at_its_own_folder()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "App.slnx");

        File.WriteAllText(path, """
            <Solution>
              <Project Path="Api/Api.csproj" />
            </Solution>
            """);

        Assert.AreEqual(Path.Combine(dir, "Api"), SolutionFile.Read(path).Root);
    }

    [TestMethod]
    public void An_unreadable_solution_reports_no_projects_rather_than_throwing()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "Broken.slnx");

        File.WriteAllText(path, "<Solution><Project Path=");

        var read = SolutionFile.Read(path);

        Assert.IsEmpty(read.ProjectFiles);
        Assert.AreEqual(dir, read.Root);
    }

    /// <summary>
    /// A drive root has to keep its separator: "C:" alone is a relative path, and a workspace
    /// rooted there resolves against the process's current directory instead of the drive.
    /// </summary>
    [TestMethod]
    public void A_drive_root_keeps_its_separator()
    {
        Assert.AreEqual(
            @"C:\",
            SolutionFile.CommonAncestorDirectory([@"C:\Api\Api.csproj", @"C:\Core\Core.csproj"]));
    }

    /// <summary>
    /// Compared segment by segment, not by string prefix — a prefix comparison makes C:\App an
    /// ancestor of C:\AppData, and the workspace quietly scans a sibling tree.
    /// </summary>
    [TestMethod]
    public void A_shared_name_prefix_is_not_a_shared_folder()
    {
        Assert.AreEqual(
            @"C:\",
            SolutionFile.CommonAncestorDirectory([@"C:\App\A.csproj", @"C:\AppData\B.csproj"]));
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sln-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

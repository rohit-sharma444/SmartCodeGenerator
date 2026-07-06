using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

// Reproduces the Visual Studio / dotnet run scenario where the process current
// directory is the build-output folder (bin\Debug\net10.0). The generated class
// must land in the real project root, not buried under bin.
[TestClass]
[DoNotParallelize]
public sealed class WorkspaceRootResolutionTests
{
    [TestMethod]
    public void ResolveWorkspaceRoot_FromBinOutput_ClimbsToProjectRoot()
    {
        var projectRoot = NewTempDir("cg_proj_");
        File.WriteAllText(Path.Combine(projectRoot, "MyApp.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        var binDir = Path.Combine(projectRoot, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(binDir);

        var resolved = PathHelper.ResolveWorkspaceRoot(
            projectPathEnv: null, currentDirectory: binDir);

        Assert.AreEqual(projectRoot, resolved);
    }

    [TestMethod]
    public void ResolveWorkspaceRoot_FromProjectDir_ReturnsItself()
    {
        var projectRoot = NewTempDir("cg_proj2_");
        File.WriteAllText(Path.Combine(projectRoot, "MyApp.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var resolved = PathHelper.ResolveWorkspaceRoot(
            projectPathEnv: null, currentDirectory: projectRoot);

        Assert.AreEqual(projectRoot, resolved);
    }

    [TestMethod]
    public void ResolveWorkspaceRoot_ExplicitEnv_Wins()
    {
        var envDir = NewTempDir("cg_env_");
        var somewhereElse = NewTempDir("cg_other_");

        var resolved = PathHelper.ResolveWorkspaceRoot(
            projectPathEnv: envDir, currentDirectory: somewhereElse);

        Assert.AreEqual(Path.GetFullPath(envDir), resolved);
    }

    [TestMethod]
    public void EndToEnd_VisualStudioBinCwd_SavesEmployeeInProjectRoot()
    {
        // A real project with a bin output dir, mirroring an F5 launch.
        var projectRoot = NewTempDir("cg_vsproj_");
        File.WriteAllText(Path.Combine(projectRoot, "MyApp.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        var binCwd = Path.Combine(projectRoot, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(binCwd);

        ProjectManager.CurrentProject = null;

        // What the console startup does: resolve the workspace, then reconcile
        // any stale selection against it.
        var workspace = PathHelper.ResolveWorkspaceRoot(null, binCwd);
        ProjectManager.ClearCurrentProjectIfOutside(workspace);
        foreach (var proj in Directory.GetFiles(workspace, "*.csproj", SearchOption.AllDirectories))
        {
            ProjectManager.Register(new ProjectMetaData
            {
                Success = true,
                ProjectPath = Path.GetDirectoryName(proj)!,
                CsProjPath = proj,
                ProjectName = Path.GetFileNameWithoutExtension(proj),
                Message = "Loaded"
            });
        }

        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        ProjectMetaData? project = null;
        CommandHandlingService.ProcessInput(
            input, ref project, new List<PropertyRule>(), workspace,
            p => ProjectManager.CurrentProject = p);

        var file = Directory
            .GetFiles(projectRoot, "Employee.cs", SearchOption.AllDirectories)
            .FirstOrDefault();

        Console.WriteLine("WORKSPACE: " + workspace);
        Console.WriteLine("FILE: " + (file ?? "<none>"));

        Assert.IsNotNull(file, "Employee.cs was not generated in the project.");
        Assert.DoesNotContain(
            "/bin/",
            file!.Replace('\\', '/'),
            $"Employee.cs was buried under bin: {file}");

        var content = File.ReadAllText(file);
        StringAssert.Contains(content, "AddEmployee(");
        StringAssert.Contains(content, "EmployeeList(");
    }

    private static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
    }
}

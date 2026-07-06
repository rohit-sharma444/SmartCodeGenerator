using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

// Mirrors the user's real environment: PROJECTPATH=D:\SampleProjects containing
// several projects, with a nested DesktopApp/DesktopApp/DesktopApp.csproj that
// gets auto-selected at startup.
[TestClass]
[DoNotParallelize]
public sealed class SampleProjectsReproTests
{
    [TestMethod]
    public void CreateEmployee_WithAutoSelectedNestedProject_LandsWhereAndWithMethods()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(), "cg_sample_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        // Replicate the multi-project layout, including the nested DesktopApp.
        void MakeProject(string relCsprojDir, string name)
        {
            var dir = Path.Combine(workspace, relCsprojDir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + ".csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        }

        MakeProject("Dashboard", "Dashboard");
        MakeProject(Path.Combine("DesktopApp", "DesktopApp"), "DesktopApp");
        MakeProject("MyApp", "MyApp");
        MakeProject("NewProject", "NewProject");

        // Simulate startup: a malformed persisted entry is present, and the
        // registry is loaded — but startup must NOT auto-select any project.
        ProjectManager.CurrentProject = new ProjectMetaData
        {
            Success = true,
            ProjectName = "",
            ProjectPath = Path.Combine(workspace, "DesktopApp", "DesktopApp"),
            CsProjPath = "",
            Message = "The template WPF Application was created successfully."
        };
        // This is what the console startup now does after registering projects.
        ProjectManager.CurrentProject = null;

        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        ProjectMetaData? project = null;
        CommandHandlingService.ProcessInput(
            input, ref project, new List<PropertyRule>(), workspace,
            p => ProjectManager.CurrentProject = p);

        var files = Directory.GetFiles(
            workspace, "Employee.cs", SearchOption.AllDirectories);

        Console.WriteLine("WORKSPACE: " + workspace);
        Console.WriteLine("EMPLOYEE_FILES: " +
            (files.Length == 0 ? "<none>" : string.Join(" | ",
                files.Select(f => f.Substring(workspace.Length)))));

        Assert.IsNotEmpty(files, "No Employee.cs generated at all.");

        var file = files[0];
        var content = File.ReadAllText(file);
        Console.WriteLine("HAS_ADD: " + content.Contains("AddEmployee("));
        Console.WriteLine("HAS_LIST: " + content.Contains("EmployeeList("));

        // Must NOT be buried inside any of the discovered projects; it should
        // land in the predictable workspace-level Generated folder.
        var expected = Path.Combine(workspace, "Generated", "Employee.cs");
        Assert.AreEqual(expected, file,
            "Employee.cs should save to <workspace>\\Generated when no project is selected.");
        StringAssert.Contains(content, "AddEmployee(");
        StringAssert.Contains(content, "EmployeeList(");
    }
}

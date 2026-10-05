using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class ClassRenameServiceTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "cg_renameclass_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private string Write(string relative, string code)
    {
        var file = Path.GetFullPath(Path.Combine(_workspace, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, code);
        return file;
    }

    private static void AssertCompiles(params string[] files)
    {
        var result = Compiler.CompileSources([.. files.Select(f => (f, File.ReadAllText(f)))]);
        Assert.IsTrue(result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void A_class_is_renamed()
    {
        Write("Employee.cs", "namespace App { public class Employee { public string Name { get; set; } } }");

        Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "Employee", "Staff", out var message), message);

        var renamed = Path.Combine(_workspace, "Staff.cs");
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Employee.cs")));
        StringAssert.Contains(File.ReadAllText(renamed), "class Staff");
        AssertCompiles(renamed);
    }

    [TestMethod]
    // "rename class Employee to Manager" with Manager.cs there moved Employee.cs over it
    // (File.Move with overwrite), and the Manager class was gone.
    [DataRow("Manager.cs")]
    [DataRow("manager.cs")]
    [DataRow("Models/Manager.cs")]
    public void A_class_is_not_renamed_onto_an_existing_one(string existingPath)
    {
        const string employee = "namespace App { public class Employee { public string Name { get; set; } } }";
        const string manager = "namespace App.Models { public class Manager { public int Reports { get; set; } } }";
        var employeeFile = Write("Employee.cs", employee);
        var managerFile = Write(existingPath, manager);

        Assert.IsFalse(ClassRenameService.RenameClass(_workspace, "Employee", "Manager", out var message), message);

        Assert.AreEqual(employee, File.ReadAllText(employeeFile));
        Assert.AreEqual(manager, File.ReadAllText(managerFile));
        StringAssert.Contains(message, managerFile, message);
        AssertCompiles(employeeFile, managerFile);
    }
}

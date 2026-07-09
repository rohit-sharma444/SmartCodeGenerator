using System.IO;
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppScaffolderWriteTests
{
    private static List<ClassDefinition> OneClass() => new()
    {
        new()
        {
            Name = "Employee",
            Properties = { new PropertyDefinition { Name = "Salary", Type = "decimal" } }
        }
    };

    [TestMethod]
    public void WriteClasses_WritesOneFilePerClass_InTheGivenNamespace()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scaffold_" + Path.GetRandomFileName());
        try
        {
            var written = new AppScaffolder().WriteClasses(folder, OneClass(), "Payroll.Models");

            Assert.AreEqual(1, written.Count);
            var file = Path.Combine(folder, "Employee.cs");
            Assert.IsTrue(File.Exists(file), "Employee.cs should exist");

            var source = File.ReadAllText(file);
            StringAssert.Contains(source, "namespace Payroll.Models");
            StringAssert.Contains(source, "class Employee");
            StringAssert.Contains(source, "Salary");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void WriteClasses_CreatesTheTargetFolderIfMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "scaffold_" + Path.GetRandomFileName());
        var nested = Path.Combine(root, "Models");
        try
        {
            var written = new AppScaffolder().WriteClasses(nested, OneClass(), "Payroll.Models");

            Assert.AreEqual(1, written.Count);
            Assert.IsTrue(File.Exists(Path.Combine(nested, "Employee.cs")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

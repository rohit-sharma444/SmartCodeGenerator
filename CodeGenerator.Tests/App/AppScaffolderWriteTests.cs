using System.IO;
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppScaffolderWriteTests
{
    [TestMethod]
    public void WriteClasses_WritesOneFilePerClass_WithClassAndProperty()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scaffold_" + Path.GetRandomFileName());
        try
        {
            var classes = new List<ClassDefinition>
            {
                new()
                {
                    Name = "Employee",
                    Properties = { new PropertyDefinition { Name = "Salary", Type = "decimal" } }
                }
            };

            var written = new AppScaffolder().WriteClasses(folder, classes);

            Assert.AreEqual(1, written.Count);
            var file = Path.Combine(folder, "Employee.cs");
            Assert.IsTrue(File.Exists(file), "Employee.cs should exist");

            var source = File.ReadAllText(file);
            StringAssert.Contains(source, "class Employee");
            StringAssert.Contains(source, "Salary");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}

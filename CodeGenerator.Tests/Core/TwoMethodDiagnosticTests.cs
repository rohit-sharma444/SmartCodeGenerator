using NaturalLanguageBridge;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

// A "create ... class" command that also lists methods must resolve to
// CreateClass, not CreateMethod. Regression for the case where a second method
// clause ("add method A and B method") tipped intent scoring to CreateMethod so
// the class was never generated.
[TestClass]
[DoNotParallelize]
public sealed class CreateClassIntentTests
{
    [TestMethod]
    public void TwoMethodCreateClass_GeneratesFileWithBothMethods()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "cg_2mfix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        ProjectManager.CurrentProject = null;

        const string input =
            "create class Employee and add method AddEmployee and EmployeeList method";

        ProjectMetaData? project = null;
        var handled = CommandFront.Run(
            input, ref project, new List<PropertyRule>(), workspace,
            p => ProjectManager.CurrentProject = p);

        var file = Directory.GetFiles(workspace, "Employee.cs", SearchOption.AllDirectories).FirstOrDefault();

        Assert.IsTrue(handled, "Command was not handled.");
        Assert.IsNotNull(file, "Employee.cs was not generated.");
        var content = File.ReadAllText(file!);
        StringAssert.Contains(content, "AddEmployee(");
        StringAssert.Contains(content, "EmployeeList(");
    }
}

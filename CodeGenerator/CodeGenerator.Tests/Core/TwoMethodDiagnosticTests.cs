using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

// A "create ... class" command that also lists methods must resolve to
// CreateClass, not CreateMethod. Regression for the case where a second method
// clause ("add method A and B method") tipped intent scoring to CreateMethod so
// the class was never generated.
[TestClass]
[DoNotParallelize]
public sealed class CreateClassIntentTests
{
    [DataTestMethod]
    [DataRow("create class Employee and add method AddEmployee")]
    [DataRow("create class Employee and add method AddEmployee and EmployeeList method")]
    [DataRow("create a class Employee with properties EmpId int, Name string and add method AddEmployee and EmployeeList method")]
    [DataRow("generate Order class with methods Save, Load and Delete method")]
    public void CreateClassSentences_ResolveToCreateClass(string input)
    {
        var semantic = SemanticAnalyzer.Analyze(input);
        var intent = CodeGenerationEngine.Core.IntentAnalyzer.Detect(semantic);

        Assert.AreEqual(IntentType.CreateClass, intent,
            $"'{input}' should be CreateClass but was {intent}.");
    }

    [DataTestMethod]
    [DataRow("add method Save to Order")]
    [DataRow("create method Validate in Customer")]
    public void AddMethodSentences_StayCreateMethod(string input)
    {
        var semantic = SemanticAnalyzer.Analyze(input);
        var intent = CodeGenerationEngine.Core.IntentAnalyzer.Detect(semantic);

        Assert.AreEqual(IntentType.CreateMethod, intent,
            $"'{input}' should stay CreateMethod but was {intent}.");
    }

    [TestMethod]
    public void TwoMethodCreateClass_GeneratesFileWithBothMethods()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "cg_2mfix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        ProjectManager.CurrentProject = null;

        const string input =
            "create class Employee and add method AddEmployee and EmployeeList method";

        ProjectMetaData? project = null;
        var handled = CommandHandlingService.ProcessInput(
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

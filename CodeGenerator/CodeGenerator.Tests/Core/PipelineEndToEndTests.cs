using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

// Exercises the exact path the interactive console uses: NaturalLanguagePipeline
// -> NlpActionProvider (NLE) -> CommandHandlingService.ProcessInput.
[TestClass]
[DoNotParallelize]
public sealed class PipelineEndToEndTests
{
    [TestMethod]
    public void Pipeline_CreateEmployee_GeneratedFileContainsBothMethods()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "cg_pipe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        ProjectManager.CurrentProject = null;

        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        ProjectMetaData? project = null;
        var pipeline = new NaturalLanguagePipeline();

        var nlpAction = NlpActionProvider.GetPrimaryAction(input);
        Console.WriteLine("NLP_ACTION: " + (nlpAction ?? "<null>"));

        var outcome = pipeline.Handle(
            input,
            ref project,
            new List<PropertyRule>(),
            workspace,
            p => ProjectManager.CurrentProject = p);

        Console.WriteLine("HANDLED: " + outcome.Handled);

        var file = Directory
            .GetFiles(workspace, "Employee.cs", SearchOption.AllDirectories)
            .FirstOrDefault();

        Console.WriteLine("FILE: " + (file ?? "<none>"));
        var content = file != null ? File.ReadAllText(file) : "<no file>";
        Console.WriteLine("----- GENERATED -----");
        Console.WriteLine(content);
        Console.WriteLine("---------------------");

        Assert.IsNotNull(file, "Employee.cs was not generated at all.");
        StringAssert.Contains(content, "AddEmployee(");
        StringAssert.Contains(content, "EmployeeList(");
    }
}

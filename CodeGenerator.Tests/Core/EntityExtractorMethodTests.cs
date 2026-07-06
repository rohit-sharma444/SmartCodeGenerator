using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class EntityExtractorMethodTests
{
    [TestMethod]
    public void CreateClass_WithMethodsSeparatedByAnd_ExtractsAllMethods()
    {
        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        var semantic = SemanticAnalyzer.Analyze(input);

        var command = EntityExtractor.Extract(
            semantic, IntentType.CreateClass, workspace: string.Empty);

        var methodNames = command.Methods
            .Select(m => m.Name)
            .ToList();

        CollectionAssert.Contains(methodNames, "AddEmployee");
        CollectionAssert.Contains(methodNames, "EmployeeList");
        Assert.AreEqual(2, command.Methods.Count,
            $"Expected 2 methods but got: [{string.Join(", ", methodNames)}]");
    }

    [TestMethod]
    public void ProcessCommand_GeneratesEmployeeClassWithBothMethods()
    {
        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        var code = NaturalLanguageCodeEngine.ProcessCommand(
            input, workspaceRoot: string.Empty);

        Console.WriteLine(code);

        StringAssert.Contains(code, "class Employee");
        StringAssert.Contains(code, "AddEmployee(");
        StringAssert.Contains(code, "EmployeeList(");
    }

    [TestMethod]
    public void CreateClass_StillExtractsProperties()
    {
        const string input =
            "Please create a class Employee with properties EmpId int, " +
            "Name string, Age int, Salary decimal and add method AddEmployee " +
            "and EmployeeList method";

        var semantic = SemanticAnalyzer.Analyze(input);

        var command = EntityExtractor.Extract(
            semantic, IntentType.CreateClass, workspace: string.Empty);

        var propertyNames = command.Properties
            .Select(p => p.Name)
            .ToList();

        CollectionAssert.Contains(propertyNames, "EmpId");
        CollectionAssert.Contains(propertyNames, "Name");
        CollectionAssert.Contains(propertyNames, "Age");
        CollectionAssert.Contains(propertyNames, "Salary");
    }
}

using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class RoslynClassGeneratorTests
{
    private static ClassDefinition Employee() => new()
    {
        Name = "Employee",
        Properties = { new PropertyDefinition { Name = "Salary", Type = "decimal" } }
    };

    [TestMethod]
    public void Generate_WithoutNamespace_StillEmitsGeneratedCode()
    {
        var source = RoslynClassGenerator.Generate(Employee());

        StringAssert.Contains(source, "namespace GeneratedCode");
        StringAssert.Contains(source, "class Employee");
    }

    [TestMethod]
    public void Generate_WithNamespace_EmitsThatNamespace()
    {
        var source = RoslynClassGenerator.Generate(Employee(), "Payroll.Models");

        StringAssert.Contains(source, "namespace Payroll.Models");
        Assert.DoesNotContain("GeneratedCode", source, "the default namespace must not leak in");
    }
}

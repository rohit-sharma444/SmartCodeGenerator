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

    [TestMethod]
    public void Emits_the_usings_the_class_declares()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Usings.Add("System.Text.Json.Serialization");

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        StringAssert.Contains(source, "using System.Text.Json.Serialization;");
    }

    [TestMethod]
    public void Emits_an_attribute_a_property_declares()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition
        {
            Name = "Customer",
            Type = "Customer",
            Attributes = { "JsonIgnore" }
        });

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        StringAssert.Contains(source, "[JsonIgnore]");
        StringAssert.Contains(source, "public Customer Customer");
    }

    [TestMethod]
    public void Emits_a_generic_collection_property()
    {
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition
        {
            Name = "Orders",
            Type = "List<Order>"
        });

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        StringAssert.Contains(source, "public List<Order> Orders");
    }

    [TestMethod]
    public void A_class_with_no_usings_emits_none()
    {
        var definition = new ClassDefinition { Name = "Order" };

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        Assert.IsFalse(source.Contains("using "));
    }
}

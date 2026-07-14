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

    /// <summary>
    /// The generated app used to compile with 106 CS8618 warnings — one for every non-nullable
    /// reference property, which is every string a class has. A scaffold that greets its user with a
    /// screenful of warnings is a scaffold they distrust.
    /// </summary>
    [TestMethod]
    public void Generate_InitializesAStringProperty_SoItIsNotNullOnConstruction()
    {
        var definition = new ClassDefinition
        {
            Name = "Invoice",
            Properties = { new PropertyDefinition { Name = "Number", Type = "string" } }
        };

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        StringAssert.Contains(source, "public string Number { get; set; } = string.Empty;");
    }

    /// <summary>
    /// A navigation property cannot be initialized to anything meaningful — the object it points at
    /// is resolved later, from the store — so it takes the idiom EF Core's own scaffolder uses.
    /// </summary>
    [TestMethod]
    public void Generate_MarksAnEntityReference_AsAssignedLater()
    {
        var definition = new ClassDefinition
        {
            Name = "Payment",
            Properties = { new PropertyDefinition { Name = "Customer", Type = "Customer" } }
        };

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        StringAssert.Contains(source, "public Customer Customer { get; set; } = null!;");
    }

    [TestMethod]
    public void Generate_LeavesValueTypesAndNullablesUninitialized()
    {
        var definition = new ClassDefinition
        {
            Name = "Order",
            Properties =
            {
                new PropertyDefinition { Name = "Total", Type = "decimal" },
                new PropertyDefinition { Name = "PlacedOn", Type = "DateTime" },
                new PropertyDefinition { Name = "Count", Type = "int" },
                new PropertyDefinition { Name = "Paid", Type = "bool" },
                new PropertyDefinition { Name = "Notes", Type = "string?" },
            }
        };

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        // A value type is never null, and a nullable reference is allowed to be — neither warns,
        // so neither needs an initializer cluttering it.
        Assert.DoesNotContain("= string.Empty", source);
        Assert.DoesNotContain("null!", source);
    }

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

        Assert.DoesNotContain("using ", source);
    }
}

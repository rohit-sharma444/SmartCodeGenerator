using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>A foreign key is an int &lt;Target&gt;Id whose owner also declares a nav property
/// typed as that target. These tests pin that definition, which the seeder, the console picker
/// and the generated validators all depend on agreeing about.</summary>
[TestClass]
public class ForeignKeysTests
{
    private static ClassDefinition Class(string name, params PropertyDefinition[] properties)
    {
        var definition = new ClassDefinition { Name = name };
        definition.Properties.AddRange(properties);
        return definition;
    }

    private static PropertyDefinition Prop(string name, string type) =>
        new() { Name = name, Type = type };

    [TestMethod]
    public void Int_target_id_with_a_matching_nav_property_is_a_foreign_key()
    {
        var order = Class("Order", Prop("Id", "int"), Prop("CustomerId", "int"), Prop("Customer", "Customer"));
        var customer = Class("Customer", Prop("Id", "int"));
        var classes = new List<ClassDefinition> { order, customer };

        var targets = ForeignKeys.TargetsOf(order, ForeignKeys.ClassNameSet(classes));

        Assert.HasCount(1, targets);
        Assert.AreEqual("Customer", targets[0]);
    }

    [TestMethod]
    public void Int_target_id_without_a_nav_property_is_not_a_foreign_key()
    {
        var order = Class("Order", Prop("Id", "int"), Prop("CustomerId", "int"));
        var customer = Class("Customer", Prop("Id", "int"));
        var classes = new List<ClassDefinition> { order, customer };

        var targets = ForeignKeys.TargetsOf(order, ForeignKeys.ClassNameSet(classes));

        Assert.IsEmpty(targets);
    }

    [TestMethod]
    public void The_entitys_own_Id_is_not_a_foreign_key()
    {
        var customer = Class("Customer", Prop("Id", "int"));
        var classes = new List<ClassDefinition> { customer };

        Assert.IsEmpty(ForeignKeys.TargetsOf(customer, ForeignKeys.ClassNameSet(classes)));
    }

    [TestMethod]
    public void FieldNames_returns_each_foreign_key_field_once()
    {
        var order = Class("Order", Prop("CustomerId", "int"), Prop("Customer", "Customer"));
        var invoice = Class("Invoice", Prop("CustomerId", "int"), Prop("Customer", "Customer"));
        var customer = Class("Customer", Prop("Id", "int"));

        var fields = ForeignKeys.FieldNames(new List<ClassDefinition> { order, invoice, customer });

        Assert.HasCount(1, fields);
        Assert.AreEqual("CustomerId", fields[0]);
    }
}

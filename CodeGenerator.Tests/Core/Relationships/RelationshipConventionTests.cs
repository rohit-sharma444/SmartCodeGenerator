using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>The one definition of what a relationship is made of. `build app` and the
/// relationship flow both emit through this, so a change here changes both.</summary>
[TestClass]
public class RelationshipConventionTests
{
    [TestMethod]
    public void The_foreign_key_is_an_int_named_after_its_target()
    {
        var key = RelationshipConvention.ForeignKey("Customer");

        Assert.AreEqual("CustomerId", key.Name);
        Assert.AreEqual("int", key.Type);
        Assert.IsEmpty(key.Attributes);
    }

    [TestMethod]
    public void The_forward_reference_is_typed_as_the_target_and_hidden_from_json_and_grids()
    {
        var forward = RelationshipConvention.Forward("Customer");

        Assert.AreEqual("Customer", forward.Name);
        Assert.AreEqual("Customer", forward.Type);
        Assert.Contains("JsonIgnore", forward.Attributes);
        Assert.Contains("Browsable(false)", forward.Attributes);
    }

    [TestMethod]
    public void The_inverse_of_a_one_to_many_is_a_pluralized_list()
    {
        var inverse = RelationshipConvention.InverseCollection("Order");

        Assert.AreEqual("Orders", inverse.Name);
        Assert.AreEqual("List<Order>", inverse.Type);
    }

    [TestMethod]
    public void The_inverse_of_a_one_to_one_is_a_single_reference()
    {
        var inverse = RelationshipConvention.InverseReference("Employee");

        Assert.AreEqual("Employee", inverse.Name);
        Assert.AreEqual("Employee", inverse.Type);
    }

    [TestMethod]
    public void A_y_after_a_consonant_pluralizes_to_ies()
        => Assert.AreEqual("Companies", RelationshipConvention.Pluralize("Company"));

    [TestMethod]
    public void A_name_that_collides_with_the_class_itself_cannot_be_added()
    {
        // CS0542: a member may not have the same name as its enclosing type.
        var customer = new ClassDefinition { Name = "Customer" };

        Assert.IsFalse(RelationshipConvention.CanAdd(customer, "Customer"));
    }

    [TestMethod]
    public void A_name_already_taken_cannot_be_added()
    {
        var customer = new ClassDefinition { Name = "Customer" };
        customer.Properties.Add(new PropertyDefinition { Name = "Orders", Type = "List<Order>" });

        Assert.IsFalse(RelationshipConvention.CanAdd(customer, "Orders"));
    }
}

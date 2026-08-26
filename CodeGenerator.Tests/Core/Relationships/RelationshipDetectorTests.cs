using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>
/// What the tool proposes on its own, and — more importantly — what it does not.
/// </summary>
/// <remarks>
/// Every proposal is a schema change the user is asked to approve, so a wrong one costs more than
/// a missing one. The negative cases below are the ones that matter.
/// </remarks>
[TestClass]
public class RelationshipDetectorTests
{
    [TestMethod]
    public void An_orphan_foreign_key_is_proposed_as_a_one_to_many()
    {
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("CustomerId", "int")),
            Snapshot("Customer", ("Id", "int"))
        };

        var proposals = RelationshipDetector.Detect(classes);

        Assert.HasCount(1, proposals);
        Assert.AreEqual("Order", proposals[0].Owner);
        Assert.AreEqual("Customer", proposals[0].Target);
        Assert.AreEqual(RelationshipKind.OneToMany, proposals[0].Kind);
        Assert.Contains("CustomerId", proposals[0].Reason);
    }

    [TestMethod]
    public void An_orphan_navigation_property_is_proposed_too()
    {
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("Customer", "Customer")),
            Snapshot("Customer", ("Id", "int"))
        };

        var proposals = RelationshipDetector.Detect(classes);

        Assert.HasCount(1, proposals);
        Assert.AreEqual("Order", proposals[0].Owner);
        Assert.AreEqual("Customer", proposals[0].Target);
    }

    [TestMethod]
    public void A_class_named_after_another_is_proposed_as_its_child()
    {
        var classes = new[]
        {
            Snapshot("OrderLine", ("Id", "int"), ("Quantity", "int")),
            Snapshot("Order", ("Id", "int"))
        };

        var proposals = RelationshipDetector.Detect(classes);

        Assert.HasCount(1, proposals);
        Assert.AreEqual("OrderLine", proposals[0].Owner);
        Assert.AreEqual("Order", proposals[0].Target);
        Assert.Contains("named after", proposals[0].Reason);
    }

    [TestMethod]
    public void A_complete_relationship_is_not_proposed_again()
    {
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("CustomerId", "int"), ("Customer", "Customer")),
            Snapshot("Customer", ("Id", "int"), ("Orders", "List<Order>"))
        };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void A_nullable_foreign_key_is_not_a_foreign_key_and_is_not_proposed()
    {
        // ForeignKeys.TargetsOf requires exactly int. Proposing a navigation property for an int?
        // would produce something that looks related and behaves unrelated.
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("CustomerId", "int?")),
            Snapshot("Customer", ("Id", "int"))
        };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void An_id_pointing_at_no_such_class_is_not_proposed()
    {
        var classes = new[] { Snapshot("Order", ("Id", "int"), ("SupplierId", "int")) };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void The_classs_own_Id_is_not_a_relationship_to_itself()
    {
        var classes = new[] { Snapshot("Order", ("Id", "int")), Snapshot("Id", ("Value", "string")) };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void A_self_reference_is_not_proposed()
    {
        var classes = new[] { Snapshot("Order", ("Id", "int"), ("OrderId", "int")) };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void Nothing_is_proposed_when_the_inverse_name_is_already_taken()
    {
        // Customer already has an `Orders` of its own. Adding a second is CS0102, and the
        // convention refuses it, so there is nothing to propose.
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("CustomerId", "int")),
            Snapshot("Customer", ("Id", "int"), ("Orders", "string"))
        };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void A_proposal_keys_on_its_two_class_names_so_it_can_be_remembered()
        => Assert.AreEqual(
            "Order->Customer",
            new RelationshipProposal("Order", "Customer", RelationshipKind.OneToMany, "because").Key);

    private static ClassSnapshot Snapshot(string name, params (string Name, string Type)[] properties)
        => new(
            name,
            $"C:\\ws\\{name}.cs",
            [.. properties.Select(p => new PropertyDefinition { Name = p.Name, Type = p.Type })]);
}

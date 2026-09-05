using System.Linq;
using AppScaffolding;
using AppScaffolding.Screens;
using DomainDiscovery.Llm;

namespace CodeGenerator.Tests.App;

/// <summary>
/// What a language model says, all the way through to the screen each class gets.
/// </summary>
/// <remarks>
/// The unit tests either side of this one were both green while the thing they exist for did not
/// happen. LlmModelNormalizerTests proved a relationship was recorded; EntityRolesTests proved a
/// class with a foreign key and a transactional name is a Transaction. Between them the foreign
/// key was being dropped, so the second test's precondition was never met on a real run and
/// `build app: purchase ordering system for a wholesaler` produced seven masters, no document
/// screen, and no lines grid.
/// <para>
/// So this asserts the OUTCOME -- the role each class ends up with and which document the lines
/// hang off -- across the three stages that have to agree: LlmModelNormalizer records the
/// reference, DomainModelMapper turns it into navigation properties beside the key, and
/// EntityRoles reads both back. Deleting the foreign key half at any one of the three turns this
/// red, which is the property none of the per-stage tests had.
/// </para>
/// </remarks>
[TestClass]
public class LlmModelScreenSplitTests
{
    private static LlmClass Class(string name, string role, params LlmProperty[] properties)
        => new() { Name = name, Role = role, Purpose = "a thing", Properties = properties.ToList() };

    private static LlmProperty Property(string name, string type)
        => new() { Name = name, Type = type };

    private static EntityRoleMap SplitOf(LlmDomainModel raw)
        => EntityRoles.Classify(
            DomainModelMapper.ToClassDefinitions(LlmModelNormalizer.Normalize(raw)).ToList());

    private static LlmDomainModel PurchaseOrdering() => new()
    {
        Classes =
        {
            Class(
                "PurchaseOrder",
                "Transaction",
                Property("supplier_id", "int"),
                Property("total_price", "decimal")),
            Class(
                "PurchaseOrderLine",
                "Document",
                Property("purchase_order_id", "int"),
                Property("quantity", "int")),
            Class("Supplier", "Party", Property("Name", "string"))
        }
    };

    [TestMethod]
    public void A_document_the_model_described_reaches_the_split_as_a_transaction()
    {
        var split = SplitOf(PurchaseOrdering());

        Assert.AreEqual(EntityRole.Transaction, split.RoleOf("PurchaseOrder"));
    }

    [TestMethod]
    public void Its_lines_are_edited_inside_it_rather_than_given_a_screen_of_their_own()
    {
        var split = SplitOf(PurchaseOrdering());

        Assert.AreEqual(EntityRole.DocumentLine, split.RoleOf("PurchaseOrderLine"));
        CollectionAssert.Contains(split.LinesOf("PurchaseOrder").ToList(), "PurchaseOrderLine");
    }

    /// <summary>
    /// The party the document points at stays a master: this is the half of the split that was
    /// never broken, and a fix that made everything a transaction would pass the two tests above.
    /// </summary>
    [TestMethod]
    public void The_party_it_references_is_still_a_master()
    {
        var split = SplitOf(PurchaseOrdering());

        Assert.AreEqual(EntityRole.Master, split.RoleOf("Supplier"));
    }

    /// <summary>
    /// The foreign key is on the class the model put it on, pointing where the model pointed it.
    /// </summary>
    /// <remarks>
    /// Asserted on the emitted class rather than the role map because the role map cannot tell a
    /// key that resolved from a key that pointed the wrong way -- both give a Transaction. A
    /// reversed reference would put SupplierId on Supplier and still split the screens correctly.
    /// </remarks>
    [TestMethod]
    public void The_key_and_its_navigation_property_are_both_on_the_referencing_class()
    {
        var classes = DomainModelMapper.ToClassDefinitions(
            LlmModelNormalizer.Normalize(PurchaseOrdering()));

        var order = classes.Single(c => c.Name == "PurchaseOrder");

        Assert.IsTrue(
            order.Properties.Any(p => p.Name == "SupplierId" && p.Type == "int"),
            "PurchaseOrder has no int SupplierId");
        Assert.IsTrue(
            order.Properties.Any(p => p.Type == "Supplier"),
            "PurchaseOrder has no Supplier navigation property");

        var supplier = classes.Single(c => c.Name == "Supplier");

        Assert.IsFalse(
            supplier.Properties.Any(p => p.Name == "PurchaseOrderId"),
            "the reference points backwards: Supplier carries the key");
    }
}

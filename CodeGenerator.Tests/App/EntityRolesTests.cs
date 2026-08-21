using AppScaffolding.Screens;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The heuristic. A transaction needs BOTH an outgoing foreign key AND a reason to think it is
/// dated or transactional by name — either half alone misfires on an ordinary model.
/// </summary>
[TestClass]
public class EntityRolesTests
{
    /// <summary>
    /// An entity shaped the way <c>DomainModelMapper</c> shapes one.
    /// </summary>
    /// <remarks>
    /// The audit fields are here in every fixture on purpose: the mapper puts them on EVERY
    /// discovered class, so a heuristic that counts "has a DateTime" is true of everything. Leaving
    /// them out of the fixture would hide the trap this whole file exists to pin.
    /// </remarks>
    internal static ClassDefinition Entity(string name, params (string Name, string Type)[] fields)
    {
        var definition = new ClassDefinition { Name = name };

        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        foreach (var (fieldName, type) in fields)
            definition.Properties.Add(new PropertyDefinition { Name = fieldName, Type = type });

        definition.Properties.Add(new PropertyDefinition { Name = "CreatedOn", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "ModifiedOn", Type = "DateTime" });

        return definition;
    }

    /// <summary>A foreign key as <c>ForeignKeys.TargetsOf</c> defines one: a
    /// <c>&lt;Target&gt;Id</c> int AND a navigation property typed as the target.</summary>
    internal static (string Name, string Type)[] ForeignKeyTo(string target)
        => [(target + "Id", "int"), (target, target)];

    [TestMethod]
    public void An_entity_with_no_foreign_keys_is_a_master()
    {
        var map = EntityRoles.Classify([Entity("Customer", ("Name", "string"))]);

        Assert.AreEqual(EntityRole.Master, map.RoleOf("Customer"));
    }

    /// <summary>
    /// The audit-field trap, and the single most important test in this file.
    /// </summary>
    /// <remarks>
    /// DomainModelMapper gives every class a CreatedOn and a ModifiedOn, so "has a DateTime" means
    /// nothing. Without the exclusion this entity classifies as a transaction — and so does every
    /// other entity in every model, invisibly, with nothing in the emitted diff to show it.
    /// </remarks>
    [TestMethod]
    public void A_foreign_key_plus_only_audit_dates_is_not_a_transaction()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("Address", [.. ForeignKeyTo("Customer"), ("Street", "string")]),
        };

        Assert.AreEqual(EntityRole.Master, EntityRoles.Classify(classes).RoleOf("Address"));
    }

    [TestMethod]
    public void A_foreign_key_plus_a_real_date_is_a_transaction()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("Booking", [.. ForeignKeyTo("Customer"), ("BookedOn", "DateTime")]),
        };

        Assert.AreEqual(EntityRole.Transaction, EntityRoles.Classify(classes).RoleOf("Booking"));
    }

    /// <summary>The gazetteer is the other half: a domain whose vocabulary the lexicon never
    /// learned may produce an Order with no date field at all.</summary>
    [TestMethod]
    public void A_foreign_key_plus_a_transactional_name_is_a_transaction()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("Order", [.. ForeignKeyTo("Customer"), ("Amount", "decimal")]),
        };

        Assert.AreEqual(EntityRole.Transaction, EntityRoles.Classify(classes).RoleOf("Order"));
    }

    /// <summary>Both halves are required. A dated entity that references nothing is reference data
    /// that happens to record a date — an Employee with a HireDate is not a transaction.</summary>
    [TestMethod]
    public void A_date_without_a_foreign_key_is_not_a_transaction()
        => Assert.AreEqual(
            EntityRole.Master,
            EntityRoles.Classify([Entity("Employee", ("HireDate", "DateTime"))]).RoleOf("Employee"));

    /// <summary>Likewise a transactional NAME with nothing to reference.</summary>
    [TestMethod]
    public void A_transactional_name_without_a_foreign_key_is_not_a_transaction()
        => Assert.AreEqual(
            EntityRole.Master,
            EntityRoles.Classify([Entity("Order", ("Amount", "decimal"))]).RoleOf("Order"));

    /// <summary>The gazetteer matches a word inside a name, or SalesOrder and PurchaseInvoice are
    /// both missed.</summary>
    [TestMethod]
    public void A_compound_name_matches_the_word_inside_it()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("SalesOrder", [.. ForeignKeyTo("Customer"), ("Amount", "decimal")]),
        };

        Assert.AreEqual(EntityRole.Transaction, EntityRoles.Classify(classes).RoleOf("SalesOrder"));
    }

    [TestMethod]
    public void An_empty_model_classifies_to_an_empty_map()
        => Assert.IsEmpty(EntityRoles.Classify([]).Names(EntityRole.Master));

    /// <summary>The audit field names are stated once and shared, so the heuristic and any future
    /// caller cannot disagree about which dates do not count.</summary>
    [TestMethod]
    public void The_audit_fields_are_the_two_DomainModelMapper_adds()
        => CollectionAssert.AreEquivalent(
            new[] { "CreatedOn", "ModifiedOn" }, EntityRoles.AuditFields.ToList());

    /// <summary>Every word in the gazetteer is lower case, because the match is done against a
    /// lower-cased name; a capitalised entry would silently never match.</summary>
    [TestMethod]
    public void Every_gazetteer_word_is_lower_case()
        => Assert.IsEmpty(
            TransactionWords.All.Where(w => w != w.ToLowerInvariant()).ToList());
}

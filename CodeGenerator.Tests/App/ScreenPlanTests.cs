using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The screen split as the build workflow needs it: from a DISCOVERED model rather than from
/// class definitions, so the review panel and the scaffolder cannot answer the question
/// differently.
/// </summary>
/// <remarks>
/// <see cref="EntityRolesTests"/> pins the heuristic itself. What is asserted here is the wiring
/// either side of it — the mapping discovery's entity names go through, and the auth rename that
/// only happens on a build with a login.
/// </remarks>
[TestClass]
public class ScreenPlanTests
{
    [TestMethod]
    public void A_dated_child_of_a_master_is_a_transaction()
    {
        var plan = ScreenPlan.For(ShopModel(), AppCustomization.Default, new WpfTarget(), "Acme");

        Assert.AreEqual(EntityRole.Master, plan.RoleOfEntity("Customer"));
        Assert.AreEqual(EntityRole.Transaction, plan.RoleOfEntity("Order"));
    }

    [TestMethod]
    public void A_named_master_overrules_the_heuristic()
    {
        var customization = AppCustomization.Default with { Masters = "Order" };

        var plan = ScreenPlan.For(ShopModel(), customization, new WpfTarget(), "Acme");

        Assert.AreEqual(EntityRole.Master, plan.RoleOfEntity("Order"));
    }

    /// <summary>
    /// The reason the plan takes a customization and a target rather than just a model: the auth
    /// core occupies "User", so a login build renames a discovered User entity — and a panel
    /// showing the pre-rename name would be naming a class the app will not contain.
    /// </summary>
    [TestMethod]
    public void A_login_build_renames_a_discovered_User_and_still_answers_for_it()
    {
        var customization = AppCustomization.Default with { Login = true };

        var plan = ScreenPlan.For(UserModel(), customization, new WpfTarget(), "Acme");

        Assert.AreNotEqual("User", plan.ClassNameOf("User"));
        Assert.AreEqual(EntityRole.Master, plan.RoleOfEntity("User"));
    }

    /// <summary>
    /// A target with no screens has no auth core either, so the name is not reserved and the
    /// entity keeps it. <c>AppScaffolder</c> decides this the same way; the plan is where that
    /// decision now lives.
    /// </summary>
    [TestMethod]
    public void A_target_without_screens_leaves_a_discovered_User_alone()
    {
        var customization = AppCustomization.Default with { Login = true };

        var plan = ScreenPlan.For(UserModel(), customization, new ConsoleTarget(), "Acme");

        Assert.AreEqual("User", plan.ClassNameOf("User"));
    }

    /// <summary>A line entity gets no screen; the transaction it is edited inside names it.</summary>
    [TestMethod]
    public void Lines_belong_to_the_transaction_they_are_edited_inside()
    {
        var plan = ScreenPlan.For(ShopModel(), AppCustomization.Default, new WpfTarget(), "Acme");

        Assert.AreEqual(EntityRole.DocumentLine, plan.RoleOfEntity("OrderLine"));
        CollectionAssert.Contains(plan.Roles.LinesOf("Order").ToList(), "OrderLine");
    }

    /// <summary>
    /// The other direction from <c>EntityRoleMap.LinesOf</c>: a panel drawing one class at a time
    /// has the line in its hand and needs the transaction, not the other way round.
    /// </summary>
    [TestMethod]
    public void Names_the_transaction_a_line_is_edited_inside()
    {
        var plan = ScreenPlan.For(ShopModel(), AppCustomization.Default, new WpfTarget(), "Acme");

        Assert.AreEqual("Order", plan.TransactionHolding("OrderLine"));
        Assert.IsNull(plan.TransactionHolding("Customer"));
    }

    /// <summary>
    /// An entity named for the app itself is renamed, the same way a discovered "User" is
    /// renamed around the auth core.
    /// </summary>
    /// <remarks>
    /// Issue #132: every generated file declares <c>namespace Shop.X;</c>, so a class ALSO named
    /// "Shop" makes the simple name "Shop" ambiguous between the type and the enclosing
    /// namespace at the global scope (CS0118) — reproduced building a real app from a CSV whose
    /// sole inferred entity shared the app's own name. No login and no screens are needed to
    /// trigger it, unlike the User collision above: the root namespace exists on every target.
    /// </remarks>
    [TestMethod]
    public void An_entity_named_for_the_app_itself_is_renamed()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(Entity("Shop", ("Name", "string")));

        var plan = ScreenPlan.For(model, AppCustomization.Default, new ConsoleTarget(), "Shop");

        Assert.AreNotEqual("Shop", plan.ClassNameOf("Shop"));
    }

    /// <summary>
    /// EVERY segment of the root namespace is reserved, not only the outermost. Lookup inside
    /// <c>Shop.Api.Data</c> walks <c>Shop.Api</c> and <c>Shop</c> before any using directive, so a
    /// root table <c>Api</c> from api.json in a web API (<c>Shop.Api</c>) found the namespace and
    /// the app did not compile (CS0118). The test project's own <c>.Tests</c> segment likewise.
    /// </summary>
    [TestMethod]
    [DataRow("Acme.Shop", "Shop")]
    [DataRow("Acme.Shop", "Acme")]
    [DataRow("Shop.Api", "Api")]
    [DataRow("Shop.Worker", "Worker")]
    [DataRow("Shop", "Tests")]
    public void Every_segment_of_the_namespace_is_reserved(string rootNamespace, string entity)
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(Entity(entity, ("Name", "string")));

        var plan = ScreenPlan.For(model, AppCustomization.Default, new ConsoleTarget(), rootNamespace);

        Assert.AreEqual(entity + "Item", plan.ClassNameOf(entity));
    }

    /// <summary>
    /// Names a target's own files or its SDK's implicit usings already bind: System.Console,
    /// Hosting's Host, Routing's Route, the top-level Program, the worker's Worker class, xUnit's
    /// Assert and Fact in the test project. A same-named entity did not compile (CS0104, CS0118,
    /// CS0310, CS9338).
    /// </summary>
    [TestMethod]
    [DataRow("Console")]
    [DataRow("Host")]
    [DataRow("Route")]
    [DataRow("Program")]
    [DataRow("Worker")]
    [DataRow("Results")]
    [DataRow("WebApplication")]
    [DataRow("Assert")]
    [DataRow("Fact")]
    [DataRow("Record")]
    [DataRow("Lock")]
    [DataRow("Monitor")]
    [DataRow("Lookup")]
    public void A_name_a_target_already_binds_is_renamed(string entity)
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(Entity(entity, ("Name", "string")));

        var plan = ScreenPlan.For(model, AppCustomization.Default, new WebApiTarget(), "Shop.Api");

        Assert.AreEqual(entity + "Item", plan.ClassNameOf(entity));
    }

    /// <summary>Customer, Order (dated, references Customer) and OrderLine (references Order).</summary>
    private static CandidateDomainModel ShopModel()
    {
        var model = new CandidateDomainModel();

        model.Entities.Add(Entity("Customer", ("Name", "string")));

        var order = Entity("Order", ("OrderDate", "DateTime"), ("CustomerId", "int"));
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "belongs" });
        model.Entities.Add(order);

        var line = Entity("OrderLine", ("Quantity", "int"), ("OrderId", "int"));
        line.Relationships.Add(new CandidateRelationship { Target = "Order", Role = "belongs" });
        model.Entities.Add(line);

        return model;
    }

    private static CandidateDomainModel UserModel()
    {
        var model = new CandidateDomainModel();

        model.Entities.Add(Entity("User", ("Name", "string")));

        return model;
    }

    private static CandidateEntity Entity(string name, params (string Name, string Type)[] fields)
    {
        var entity = new CandidateEntity { Name = name, Provenance = Provenance.Wiki };

        foreach (var (fieldName, type) in fields)
            entity.Fields.Add(new CandidateField
            {
                Name = fieldName, Type = type, Provenance = Provenance.Wiki
            });

        return entity;
    }
}

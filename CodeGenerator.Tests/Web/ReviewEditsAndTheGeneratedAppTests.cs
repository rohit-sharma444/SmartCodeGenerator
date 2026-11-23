using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// What a Review edit does to the app already generated, and to a second tab.
/// </summary>
/// <remarks>
/// A refused edit leaves the model as it was, so it must not throw away the generated app: it
/// did, because <c>ModelEdited</c> dropped Generate and Run whether the editor said yes or no.
/// And a real edit must repaint a second tab: <c>ModelEdited</c> raised <c>Changed</c> only when
/// an app was generated, so the other tab went on listing a field that was gone.
/// </remarks>
[TestClass]
public class ReviewEditsAndTheGeneratedAppTests
{
    [TestMethod]
    public void A_refused_class_rename_keeps_the_generated_app()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, generated: true);

        rendered.Find("[data-entity=Customer] input.entity-name").Change("1bad");

        Assert.AreEqual("Customer", session.Model!.Entities[0].Name, "the rename was not refused");
        Assert.IsNotNull(session.Generated, "a refused edit threw away the generated app");
    }

    [TestMethod]
    public void A_refused_field_removal_keeps_the_generated_app()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, generated: true);

        // A class's last field stays.
        rendered.Find("[data-entity=Customer] .fields button.unfield").Click();

        Assert.HasCount(1, session.Model!.Entities[0].Fields, "the removal was not refused");
        Assert.IsNotNull(session.Generated, "a refused edit threw away the generated app");
    }

    [TestMethod]
    public void A_refused_new_class_keeps_the_generated_app()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, generated: true);

        rendered.Find("input.new-class").Change("Customer");
        rendered.Find("button.add-class").Click();

        Assert.HasCount(3, session.Model!.Entities, "the new class was not refused");
        Assert.IsNotNull(session.Generated, "a refused edit threw away the generated app");
    }

    [TestMethod]
    public void A_real_rename_still_drops_the_generated_app()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, generated: true);

        rendered.Find("[data-entity=Customer] input.entity-name").Change("Client");

        Assert.AreEqual("Client", session.Model!.Entities[0].Name);
        Assert.IsNull(session.Generated, "the old app still stands for a changed model");
    }

    [TestMethod]
    public void A_real_new_class_still_drops_the_generated_app()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, generated: true);

        rendered.Find("input.new-class").Change("Supplier");
        rendered.Find("button.add-class").Click();

        Assert.HasCount(4, session.Model!.Entities);
        Assert.IsNull(session.Generated, "the old app still stands for a changed model");
    }

    [TestMethod]
    public void A_refused_edit_still_saves()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, generated: true);
        var saves = 0;
        session.Autosave = _ => saves++;

        rendered.Find("[data-entity=Customer] input.entity-name").Change("1bad");

        Assert.AreEqual(1, saves);
    }

    /// <summary>The other tab is repainted by <c>Changed</c>, and nothing else.</summary>
    [TestMethod]
    public void A_removed_field_tells_the_other_tabs_without_a_generated_app()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, generated: false);
        var changes = 0;
        session.Changed += () => changes++;

        rendered.Find("[data-entity=Order] .fields button.unfield").Click();

        Assert.HasCount(1, session.Model!.Entities[1].Fields, "the field was not removed");
        Assert.IsGreaterThan(0, changes, "the other tab was not told");
    }

    [TestMethod]
    public void A_refused_edit_does_not_tell_the_other_tabs()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, generated: false);
        var changes = 0;
        session.Changed += () => changes++;

        rendered.Find("[data-entity=Customer] input.entity-name").Change("1bad");

        Assert.AreEqual(0, changes);
    }

    private static IRenderedComponent<ReviewStage> Render(
        BunitContext context, out BuildSession session, bool generated)
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(Entity("Customer", "Name"));
        model.Entities.Add(Entity("Order", "OrderDate", "Notes"));
        model.Entities.Add(Entity("Product", "Name"));

        session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model);
        session.Advance();

        if (generated)
            session.SetGenerated(new GeneratedApp(
                new ScaffoldResult(true, @"C:\ws\Shop", [], string.Empty),
                BuildLog: "Build succeeded.",
                BuildSucceeded: true));

        var captured = session;

        return context.Render<ReviewStage>(p => p
            .Add(c => c.Session, captured)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));
    }

    private static CandidateEntity Entity(string name, params string[] fields)
    {
        var entity = new CandidateEntity { Name = name, Provenance = Provenance.Wiki };

        foreach (var field in fields)
            entity.Fields.Add(new CandidateField
            {
                Name = field, Type = "string", Provenance = Provenance.Wiki
            });

        return entity;
    }
}

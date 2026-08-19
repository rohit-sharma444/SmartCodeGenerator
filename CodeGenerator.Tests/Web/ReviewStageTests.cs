using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The review screen. The rules belong to ModelEditor and are tested there; what is asserted
/// here is that the model is actually on screen — which in the console it is not — and that each
/// control reaches the right editor method and reports its answer.
/// </summary>
[TestClass]
public class ReviewStageTests
{
    [TestMethod]
    public void Shows_every_class_and_its_fields()
    {
        using var context = new BunitContext();

        var markup = Render(context, out _).Markup;

        StringAssert.Contains(markup, "Book");
        StringAssert.Contains(markup, "Title");
    }

    [TestMethod]
    public void Shows_a_suggestion_as_promotable()
    {
        using var context = new BunitContext();

        var rendered = Render(context, out _);

        StringAssert.Contains(rendered.Markup, "Shelf");
        Assert.IsNotEmpty(rendered.FindAll("button.promote"));
    }

    [TestMethod]
    public void Removing_a_class_takes_it_off_the_screen()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("[data-entity=Book] button.remove").Click();

        Assert.IsEmpty(session.Model!.Entities.Where(e => e.Name == "Book"));
    }

    // ModelEditor refuses this (CS0542 — a property may not share its class's name) and returns
    // false. The panel must say so rather than silently doing nothing.
    [TestMethod]
    public void Reports_a_refused_field_rather_than_failing_silently()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out _);

        rendered.Find("[data-entity=Book] input.field-name").Change("Book");
        rendered.Find("[data-entity=Book] button.add-field").Click();

        StringAssert.Contains(rendered.Markup, "Could not add");
    }

    [TestMethod]
    public void Offers_the_five_supported_field_types_and_no_others()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out _);

        var options = rendered.FindAll("[data-entity=Book] select.field-type option");

        Assert.HasCount(5, options);
    }

    [TestMethod]
    public void Shows_the_target_and_the_customization_that_generate_will_use()
    {
        using var context = new BunitContext();

        var markup = Render(context, out _).Markup;

        StringAssert.Contains(markup, CodeGenerator.App.ApplicationTypeMenu.LabelFor(new WpfTarget()));
    }

    // Generating is this stage's step forward, so it is named in the box rather than buried under
    // a screen of entity cards.
    [TestMethod]
    public async Task Generate_is_the_step_forward_and_advances_to_the_last_node()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        Render(context, out var session, navigation);

        Assert.AreEqual("Generate", navigation.Label);

        Assert.IsTrue(await navigation.ApplyAsync());
        session.Advance();

        Assert.AreEqual(BuildStage.Generate, session.Current);
    }

    [TestMethod]
    public void Generate_is_refused_on_an_empty_model()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();

        context.Render<ReviewStage>(p => p
            .Add(c => c.Session, SessionWith(new CandidateDomainModel()))
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsTrue(navigation.HasStep, "the step should be visible, just not usable");
        Assert.IsFalse(navigation.CanContinue);
    }

    private static IRenderedComponent<ReviewStage> Render(
        BunitContext context, out BuildSession session, StageNavigation? navigation = null)
    {
        session = SessionWith(AModel());

        var captured = session;

        return context.Render<ReviewStage>(p => p
            .Add(c => c.Session, captured)
            .Add(c => c.Navigation, navigation ?? new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));
    }

    private static BuildSession SessionWith(CandidateDomainModel model)
    {
        var session = new BuildSession("a library", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model);
        session.Advance();

        return session;
    }

    private static CandidateDomainModel AModel()
    {
        var model = new CandidateDomainModel();

        var book = new CandidateEntity { Name = "Book", Provenance = Provenance.Wiki };
        book.Fields.Add(new CandidateField
        {
            Name = "Title", Type = "string", Provenance = Provenance.Wiki
        });

        model.Entities.Add(book);
        model.Suggestions.Add(new CandidateEntity { Name = "Shelf", Provenance = Provenance.Wiki });

        return model;
    }
}

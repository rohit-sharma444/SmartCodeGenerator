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

    // The row exists and is wired to the same AddLine the console review loop uses; nothing
    // covered it, so nothing said which of the two names it must report.
    [TestMethod]
    public void Adding_a_class_puts_it_in_the_model_and_on_the_screen()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("input.new-class").Change("Member");
        rendered.Find("button.add-class").Click();

        Assert.IsNotEmpty(session.Model!.Entities.Where(e => e.Name == "Member"));
        StringAssert.Contains(rendered.Markup, "Member");
    }

    /// <summary>
    /// The editor keeps the name shape a person typed, and promoting a suggestion keeps the
    /// suggestion's own spelling — so the report has to name what the model received rather than
    /// echoing the box, which is the one thing a caller cannot work out for itself.
    /// </summary>
    [TestMethod]
    public void The_report_names_the_class_the_model_kept_not_the_text_typed()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("input.new-class").Change("  member  ");
        rendered.Find("button.add-class").Click();

        var kept = session.Model!.Entities.Last().Name;

        Assert.AreNotEqual("  member  ", kept);

        // The report, specifically: the entity card carries that name too, so asserting on the
        // whole markup would pass however the message was worded.
        StringAssert.Contains(rendered.Find(".edit-report").TextContent, kept);
    }

    [TestMethod]
    public void A_class_the_model_already_has_is_refused_with_a_reason()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("input.new-class").Change("Book");
        rendered.Find("button.add-class").Click();

        Assert.HasCount(1, session.Model!.Entities.Where(e => e.Name == "Book"));
        StringAssert.Contains(rendered.Find(".edit-report").TextContent.ToLowerInvariant(), "could not");
    }

    /// <summary>
    /// Typing the name of something discovery only suggested promotes it, keeping the fields and
    /// provenance discovery had already worked out — the alternative is a second, emptier class
    /// beside the suggestion it duplicates.
    /// </summary>
    [TestMethod]
    public void Naming_a_suggestion_promotes_it_rather_than_inventing_a_second_class()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("input.new-class").Change("Shelf");
        rendered.Find("button.add-class").Click();

        Assert.HasCount(1, session.Model!.Entities.Where(e => e.Name == "Shelf"));
        Assert.IsEmpty(session.Model.Suggestions.Where(e => e.Name == "Shelf"));
    }

    /// <summary>
    /// A class with no fields generates a class with nothing but its Id. The editor gives every
    /// added class a fallback field for that reason, and the panel must keep going through the
    /// editor rather than building an entity of its own.
    /// </summary>
    [TestMethod]
    public void An_added_class_is_never_left_without_a_field()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("input.new-class").Change("Member");
        rendered.Find("button.add-class").Click();

        Assert.IsNotEmpty(session.Model!.Entities.Single(e => e.Name == "Member").Fields);
    }

    /// <summary>Where someone deciding to do it differently is standing, with every answer so far
    /// as one line they can copy.</summary>
    [TestMethod]
    public void Shows_the_command_that_would_do_the_same_run()
    {
        using var context = new BunitContext();

        var echo = Render(context, out _).Find(".command-echo").TextContent;

        StringAssert.Contains(echo, "build app: a library");
        StringAssert.Contains(echo, "approve ");
    }

    /// <summary>
    /// The three columns the card's grid draws are positional: name, then type, then the control.
    /// Reordering the markup would leave the CSS quietly putting a type under a name — which a
    /// screenshot shows and no other test would.
    /// </summary>
    [TestMethod]
    public void A_field_row_is_a_name_then_a_type_then_its_remove()
    {
        using var context = new BunitContext();

        var row = Render(context, out _).Find("[data-entity=Book] .fields li");

        var cells = row.Children.Select(c => c.ClassName).ToList();

        Assert.HasCount(3, cells);
        StringAssert.Contains(cells[0], "field-name");
        StringAssert.Contains(cells[1], "field-type");
        StringAssert.Contains(cells[2], "unfield");
    }

    /// <summary>The row that adds a field uses the same tracks, so it carries the same three in the
    /// same order — a name, its type, and the control.</summary>
    [TestMethod]
    public void The_add_row_carries_the_same_three_in_the_same_order()
    {
        using var context = new BunitContext();

        var row = Render(context, out _).Find("[data-entity=Book] .add-field-row");

        var cells = row.Children.Select(c => c.ClassName).ToList();

        Assert.HasCount(3, cells);
        StringAssert.Contains(cells[0], "field-name");
        StringAssert.Contains(cells[1], "field-type");
        StringAssert.Contains(cells[2], "add-field");
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

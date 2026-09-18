using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerationEngine.Models;
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

        rendered.Find("[data-entity=Book] .add-field-row input.field-name").Change("Book");
        rendered.Find("[data-entity=Book] button.add-field").Click();

        StringAssert.Contains(rendered.Markup, "Could not add");
    }

    [TestMethod]
    public void Offers_the_five_supported_field_types_and_no_others()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out _);

        var options = rendered.FindAll("[data-entity=Book] .add-field-row select.field-type option");

        Assert.HasCount(5, options);
    }

    /// <summary>
    /// What this panel is NOT, since that is the whole point of the split: the diagram, the screen
    /// choice and the build summary all belong to the node after it now. A page carrying them
    /// again would put the confirmation back below a screen of entity cards.
    /// </summary>
    [TestMethod]
    public void Carries_no_diagram_no_screen_choice_and_no_build_summary()
    {
        using var context = new BunitContext();

        var rendered = Render(context, out _);

        Assert.IsEmpty(rendered.FindAll(".command-echo"));
        Assert.IsEmpty(rendered.FindAll(".screen-summary"));
        Assert.IsEmpty(rendered.FindAll("select.role"));

        // The classes themselves are still very much here.
        Assert.IsNotEmpty(rendered.FindAll(".entity"));
    }

    // Reviewing the classes settles what they ARE; the node after it settles what comes out.
    [TestMethod]
    public async Task Output_is_the_step_forward_and_advances_to_it()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        Render(context, out var session, navigation);

        Assert.AreEqual("Output", navigation.Label);

        Assert.IsTrue(await navigation.ApplyAsync());
        session.Advance();

        Assert.AreEqual(BuildStage.Output, session.Current);
    }

    [TestMethod]
    public void The_step_forward_is_refused_on_an_empty_model()
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

    /// <summary>
    /// The edits are what a closed tab loses, and they do not go through Change() - the panel
    /// calls ModelEditor directly and mutates the model in place. Asserted at the PANEL rather
    /// than on the session, because a test on the session alone passes happily while the panel
    /// bypasses it, which is the defect this exists to prevent.
    /// </summary>
    [TestMethod]
    public void Every_edit_asks_the_session_to_save()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        var saves = 0;
        session.Autosave = _ => saves++;

        rendered.Find("[data-entity=Book] button.remove").Click();
        Assert.IsGreaterThan(0, saves, "remove did not ask to be saved");

        saves = 0;
        rendered.Find("input.new-class").Change("Member");
        rendered.Find("button.add-class").Click();
        Assert.IsGreaterThan(0, saves, "add class did not ask to be saved");

        saves = 0;
        rendered.Find("button.promote").Click();
        Assert.IsGreaterThan(0, saves, "promoting a suggestion did not ask to be saved");
    }

    [TestMethod]
    public void Renaming_and_field_edits_ask_the_session_to_save()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        var saves = 0;
        session.Autosave = _ => saves++;

        rendered.Find("[data-entity=Book] .add-field-row input.field-name").Change("Isbn");
        rendered.Find("[data-entity=Book] button.add-field").Click();
        Assert.IsGreaterThan(0, saves, "add field did not ask to be saved");

        saves = 0;
        rendered.Find("[data-entity=Book] input.entity-name").Change("Volume");
        Assert.IsGreaterThan(0, saves, "rename did not ask to be saved");
    }

    /// <summary>
    /// A property's name is editable in place, the way its class's name already was. Until now
    /// the only way to correct one was to delete it and add it back, which loses its type.
    /// </summary>
    [TestMethod]
    public void Renaming_a_property_renames_it_in_the_model()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("[data-entity=Book] .fields input.field-name").Change("Heading");

        var field = session.Model!.Entities.Single(e => e.Name == "Book").Fields.Single();

        Assert.AreEqual("Heading", field.Name);
        Assert.AreEqual("string", field.Type);
    }

    /// <summary>
    /// The rules are ModelEditor's and are tested there; what matters here is that a refusal is
    /// reported rather than swallowed, which is what makes a control that did nothing legible.
    /// </summary>
    [TestMethod]
    public void A_refused_property_rename_is_reported()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        // CS0542: a property may not share its class's name.
        rendered.Find("[data-entity=Book] .fields input.field-name").Change("Book");

        Assert.AreEqual("Title", session.Model!.Entities.Single().Fields.Single().Name);
        StringAssert.Contains(rendered.Find(".edit-report").TextContent.ToLowerInvariant(), "could not");
    }

    /// <summary>
    /// The type a property was inferred to have is a guess, and this is the last screen before
    /// Roslyn emits it. "Servings" infers string and means int.
    /// </summary>
    [TestMethod]
    public void Changing_a_propertys_type_changes_it_in_the_model()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("[data-entity=Book] .fields select.field-type").Change("int");

        Assert.AreEqual("int", session.Model!.Entities.Single().Fields.Single().Type);
    }

    /// <summary>The control has to show the type the property HAS, or it reads as a choice
    /// nobody has made yet and the first thing it says about every property is wrong.</summary>
    [TestMethod]
    public void The_type_control_shows_the_type_the_property_already_has()
    {
        using var context = new BunitContext();

        var rendered = RenderShop(context, out _);

        Assert.AreEqual(
            "DateTime",
            rendered.Find("[data-entity=Order] .fields select.field-type").GetAttribute("value"));
    }

    /// <summary>
    /// Every type control is built from the list the editor enforces. Three copies of five
    /// strings is a copy waiting to drift into offering a sixth that compiles here and fails in
    /// the generated project.
    /// </summary>
    [TestMethod]
    public void Every_type_control_offers_exactly_the_types_the_editor_accepts()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out _);

        foreach (var select in rendered.FindAll("[data-entity=Book] select.field-type"))
            Assert.AreEqual(
                string.Join(",", DomainDiscovery.Review.ModelEditor.SupportedTypes),
                string.Join(",", select.QuerySelectorAll("option").Select(o => o.TextContent)));
    }

    /// <summary>
    /// Both new edits mutate the model in place rather than going through Change(), so — like
    /// every other control on this panel — they have to ask for the save themselves or a closed
    /// tab loses them.
    /// </summary>
    [TestMethod]
    public void Editing_a_property_asks_the_session_to_save()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        var saves = 0;
        session.Autosave = _ => saves++;

        rendered.Find("[data-entity=Book] .fields select.field-type").Change("int");
        Assert.IsGreaterThan(0, saves, "changing a type did not ask to be saved");

        saves = 0;
        rendered.Find("[data-entity=Book] .fields input.field-name").Change("Heading");
        Assert.IsGreaterThan(0, saves, "renaming a property did not ask to be saved");
    }

    /// <summary>
    /// Choosing the type a property already has is not a failure. ModelEditor answers "no change"
    /// to it, correctly, and the panel reports what the editor answered - so without a guard the
    /// screen said "could not change that type" to someone who picked the right one.
    /// </summary>
    [TestMethod]
    public void Choosing_the_type_a_property_already_has_reports_nothing()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out _);

        rendered.Find("[data-entity=Book] .fields select.field-type").Change("string");

        Assert.IsEmpty(rendered.FindAll(".edit-report"));
    }

    /// <summary>
    /// Retyping a property the same way it is already spelt is not a failure either, for the
    /// same reason.
    /// </summary>
    [TestMethod]
    public void Retyping_a_propertys_own_name_reports_nothing()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out _);

        rendered.Find("[data-entity=Book] .fields input.field-name").Change("Title");

        Assert.IsEmpty(rendered.FindAll(".edit-report"));
    }

    private static CandidateDomainModel AModelWithAnEntityProposal()
    {
        var model = AModel();
        var booking = new CandidateEntity { Name = "Booking" };
        booking.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        booking.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });
        booking.Proposals.Add(new EntityRuleProposal
        {
            Kind = EntityRuleProposalKind.CrossField,
            Properties = ["StartDate", "EndDate"],
            Rule = new CodeGenerationEngine.Core.Validation.Rules.ComparisonRule(
                "StartDate", "EndDate", "> 0", "StartDate must not be after EndDate"),
            Evidence = "StartDate is always on or before EndDate, in all 40 rows"
        });
        model.Entities.Add(booking);
        return model;
    }

    [TestMethod]
    public void Shows_an_entity_scoped_proposal_as_an_unchecked_box()
    {
        using var context = new BunitContext();
        var session = SessionWith(AModelWithAnEntityProposal());
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        StringAssert.Contains(rendered.Markup, "StartDate is always on or before EndDate");
        var checkbox = rendered.Find("[data-entity=Booking] .entity-proposals input.proposal");
        Assert.IsFalse(checkbox.HasAttribute("checked"));
    }

    [TestMethod]
    public void Checking_an_entity_scoped_proposal_accepts_it()
    {
        using var context = new BunitContext();
        var model = AModelWithAnEntityProposal();
        var session = SessionWith(model);
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("[data-entity=Booking] .entity-proposals input.proposal").Change(true);

        Assert.IsTrue(model.Entities.Single(e => e.Name == "Booking").Proposals[0].Accepted);
    }

    private static CandidateDomainModel AModelWithAnAcceptedStatusProposal()
    {
        var model = AModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved", "Shipped"] },
            Evidence = "Status holds only Draft, Approved, Shipped",
            Accepted = true
        });
        order.Fields.Add(status);
        model.Entities.Add(order);
        return model;
    }

    [TestMethod]
    public void Shows_the_transition_editor_once_a_status_is_accepted()
    {
        using var context = new BunitContext();
        var session = SessionWith(AModelWithAnAcceptedStatusProposal());
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        var items = rendered.FindAll("[data-entity=Order] .workflow-order li");
        Assert.HasCount(3, items);
        StringAssert.Contains(rendered.Markup, "Draft");
    }

    [TestMethod]
    public void Moving_a_value_down_changes_its_stated_order()
    {
        using var context = new BunitContext();
        var model = AModelWithAnAcceptedStatusProposal();
        var session = SessionWith(model);
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("[data-entity=Order] .workflow-order li:first-child button.move-down").Click();

        var proposal = model.Entities.Single(e => e.Name == "Order").Fields.Single().Proposals.Single();
        Assert.AreEqual("Approved", proposal.Order?[0]);
        Assert.AreEqual("Draft", proposal.Order?[1]);
    }

    [TestMethod]
    public void Checking_final_marks_that_value()
    {
        using var context = new BunitContext();
        var model = AModelWithAnAcceptedStatusProposal();
        var session = SessionWith(model);
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("[data-entity=Order] .workflow-order li:last-child input.final").Change(true);

        var proposal = model.Entities.Single(e => e.Name == "Order").Fields.Single().Proposals.Single();
        Assert.Contains("Shipped", proposal.FinalValues);
    }

    /// <summary>Ticking `final` before anyone has explicitly stated an order must arm the
    /// displayed (discovered) order, so the checkbox's implicit promise -- "this order is in
    /// effect" -- becomes true instead of enforcing nothing.</summary>
    [TestMethod]
    public void Checking_final_without_a_stated_order_arms_the_discovered_order()
    {
        using var context = new BunitContext();
        var model = AModelWithAnAcceptedStatusProposal();
        var session = SessionWith(model);
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        var proposal = model.Entities.Single(e => e.Name == "Order").Fields.Single().Proposals.Single();
        Assert.IsNull(proposal.Order);

        rendered.Find("[data-entity=Order] .workflow-order li:last-child input.final").Change(true);

        Assert.IsNotNull(proposal.Order);
        CollectionAssert.AreEqual(new[] { "Draft", "Approved", "Shipped" }, proposal.Order?.ToArray());
    }

    /// <summary>When an order was already stated explicitly (via the up/down controls), ticking
    /// `final` must not clobber it -- `proposal.Order ??= ...` is a no-op once Order is set.
    /// </summary>
    [TestMethod]
    public void Checking_final_after_the_order_was_already_stated_does_not_override_it()
    {
        using var context = new BunitContext();
        var model = AModelWithAnAcceptedStatusProposal();
        var session = SessionWith(model);
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("[data-entity=Order] .workflow-order li:first-child button.move-down").Click();
        rendered.Find("[data-entity=Order] .workflow-order li:last-child input.final").Change(true);

        var proposal = model.Entities.Single(e => e.Name == "Order").Fields.Single().Proposals.Single();
        CollectionAssert.AreEqual(new[] { "Approved", "Draft", "Shipped" }, proposal.Order?.ToArray());
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

    private static IRenderedComponent<ReviewStage> RenderShop(
        BunitContext context, out BuildSession session, ITargetScaffolder? target = null)
    {
        session = SessionWith(AShopModel(), target);

        var captured = session;

        return context.Render<ReviewStage>(p => p
            .Add(c => c.Session, captured)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));
    }

    private static BuildSession SessionWith(
        CandidateDomainModel model, ITargetScaffolder? target = null)
    {
        var session = new BuildSession("a library", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(target ?? new WpfTarget());
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

    /// <summary>Customer, a dated Order that references it, and the Order's lines — one class of
    /// each kind the split recognises.</summary>
    private static CandidateDomainModel AShopModel()
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

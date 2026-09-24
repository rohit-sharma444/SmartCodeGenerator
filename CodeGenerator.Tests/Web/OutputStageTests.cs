using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Node 7: how the classes connect, which of them get a screen, and what will be written.
/// </summary>
/// <remarks>
/// Split out of <c>ReviewStage</c>, which carried all of this under a full screen of entity
/// cards — a confirmation the user reached by scrolling past the thing being confirmed, and a
/// screen choice repeated as a drop-down on every card rather than written once as a table you
/// can read down. Most of these tests moved here with the markup they cover.
/// </remarks>
[TestClass]
public class OutputStageTests
{
    [TestMethod]
    public void Draws_the_relationship_diagram()
    {
        using var context = new BunitContext();

        var rendered = RenderShop(context, out _);

        Assert.IsNotEmpty(rendered.FindAll(".output"));

        // Named in it, which is what makes it a picture of THIS model rather than a decoration.
        StringAssert.Contains(rendered.Markup, "Order");
        StringAssert.Contains(rendered.Markup, "Customer");
    }

    /// <summary>
    /// The split decides which classes get a screen and which shape it takes, and until this
    /// panel existed it was visible only in the build output — after the code had been written.
    /// A class that got it wrong could only be corrected by retyping the whole run in the console.
    /// </summary>
    [TestMethod]
    public void Shows_the_screen_each_class_will_get()
    {
        using var context = new BunitContext();

        var rendered = RenderShop(context, out _);

        Assert.AreEqual(
            "Master", rendered.Find("[data-entity=Customer] select.role").GetAttribute("value"));
    }

    /// <summary>
    /// One row per class, which is the reason for the move: "which classes get their own screen"
    /// is a question about the model as a whole, and answering it used to mean opening every card
    /// in turn to see what the others had been set to.
    /// </summary>
    [TestMethod]
    public void Pairs_every_class_with_its_own_screen_control_in_one_table()
    {
        using var context = new BunitContext();

        var rendered = RenderShop(context, out _);

        Assert.HasCount(3, rendered.FindAll(".screens tbody tr"));

        foreach (var name in new[] { "Customer", "Order", "OrderLine" })
        {
            var row = rendered.Find($"[data-entity={name}]");

            StringAssert.Contains(row.QuerySelector(".screen-class")!.TextContent, name);
        }
    }

    [TestMethod]
    public void Choosing_a_different_screen_records_it_for_generate()
    {
        using var context = new BunitContext();
        var rendered = RenderShop(context, out var session);

        rendered.Find("[data-entity=Customer] select.role").Change("Transaction");

        StringAssert.Contains(session.Customization.Transactions, "Customer");
    }

    /// <summary>
    /// Choosing back records nothing rather than pinning the class — the rule
    /// <c>ScreenChoice</c> exists for, asserted here because this is the only place a person
    /// can exercise it.
    /// </summary>
    [TestMethod]
    public void Choosing_the_screen_it_already_had_leaves_no_override_behind()
    {
        using var context = new BunitContext();
        var rendered = RenderShop(context, out var session);

        rendered.Find("[data-entity=Customer] select.role").Change("Transaction");
        rendered.Find("[data-entity=Customer] select.role").Change("Master");

        Assert.AreEqual(string.Empty, session.Customization.Masters);
        Assert.AreEqual(string.Empty, session.Customization.Transactions);
    }

    /// <summary>
    /// A line gets no screen of its own, so it is not offered the two-way choice: asking for
    /// "transaction" on a line is an answer <c>EntityRoles</c> would silently drop in its second
    /// pass. The one correction that does work is offered instead.
    /// </summary>
    [TestMethod]
    public void A_line_is_told_which_transaction_holds_it_rather_than_offered_a_role()
    {
        using var context = new BunitContext();

        var rendered = RenderShop(context, out _);

        Assert.IsEmpty(rendered.FindAll("[data-entity=OrderLine] select.role"));
        StringAssert.Contains(
            rendered.Find("[data-entity=OrderLine] .role-line").TextContent, "Order");
    }

    [TestMethod]
    public void A_line_can_be_pulled_out_to_a_screen_of_its_own()
    {
        using var context = new BunitContext();
        var rendered = RenderShop(context, out var session);

        rendered.Find("[data-entity=OrderLine] button.own-screen").Click();

        StringAssert.Contains(session.Customization.Masters, "OrderLine");
        Assert.IsNotEmpty(rendered.FindAll("[data-entity=OrderLine] select.role"));
    }

    /// <summary>A console app has no screens to split, so the question is not put at all.</summary>
    [TestMethod]
    public void A_target_without_screens_is_not_asked_about_the_split()
    {
        using var context = new BunitContext();

        var rendered = RenderShop(context, out _, new ConsoleTarget());

        // Something first. Three absences on a panel that rendered nothing at all would pass just
        // as well, and prove nothing.
        Assert.IsNotEmpty(rendered.FindAll(".command-echo"));

        Assert.IsEmpty(rendered.FindAll("select.role"));
        Assert.IsEmpty(rendered.FindAll("button.own-screen"));
        Assert.IsEmpty(rendered.FindAll(".screen-summary"));
        Assert.IsEmpty(rendered.FindAll(".screens"));
    }

    /// <summary>The same sentence the build output has always ended with, kept where it can still
    /// be acted on.</summary>
    [TestMethod]
    public void Summarises_the_split_the_way_the_build_output_does()
    {
        using var context = new BunitContext();

        var summary = RenderShop(context, out _).Find(".screen-summary").TextContent;

        StringAssert.Contains(summary, "1 master");
        StringAssert.Contains(summary, "1 transaction");

        // The transaction holding lines, which is how ScreenSummary words it — the LINE's own
        // name is on its row, not in this sentence.
        StringAssert.Contains(summary, "Order has 1 line");
    }

    /// <summary>Where someone deciding to do it differently is standing, with every answer so far
    /// as one line they can copy.</summary>
    [TestMethod]
    public void Shows_the_command_that_would_do_the_same_run()
    {
        using var context = new BunitContext();

        var echo = RenderShop(context, out _).Find(".command-echo").TextContent;

        StringAssert.Contains(echo, "build app: a library");
        StringAssert.Contains(echo, "approve ");
    }

    [TestMethod]
    public void Shows_the_target_and_the_customization_that_generate_will_use()
    {
        using var context = new BunitContext();

        var markup = RenderShop(context, out _).Markup;

        StringAssert.Contains(
            markup, CodeGenerator.App.ApplicationTypeMenu.LabelFor(new WpfTarget()));
    }

    [TestMethod]
    public async Task Generate_is_the_step_forward_and_advances_to_it()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();

        RenderShop(context, out var session, navigation: navigation);

        Assert.AreEqual("Generate", navigation.Label);

        Assert.IsTrue(await navigation.ApplyAsync());
        session.Advance();                                    // output -> entry template
        session.Advance();                                    // entry template -> generate, nothing to answer

        Assert.AreEqual(BuildStage.Generate, session.Current);
    }

    /// <summary>The screen split is remembered for the next run, as the Look stage's answers are.
    /// The only web save used to be on Look, which comes first, so a split chosen here was lost
    /// with the session.</summary>
    [TestMethod]
    public async Task The_screen_split_is_remembered_for_the_next_run()
    {
        var root = Path.Combine(Path.GetTempPath(), "output-save-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            using var context = new BunitContext();
            var navigation = new StageNavigation();

            var session = new BuildSession(
                "a library", root, AppCustomization.Default with { Masters = "Order" });
            session.SetTarget(new WpfTarget());
            session.Advance();
            session.SetNoData();
            session.Advance();
            session.SetCustomization(session.Customization);
            session.Advance();
            session.SetModel(AShopModel());
            session.Advance();
            session.Advance();

            context.Render<OutputStage>(p => p
                .Add(c => c.Session, session)
                .Add(c => c.Navigation, navigation)
                .Add(c => c.OnAdvance, () => { }));

            Assert.IsTrue(await navigation.ApplyAsync());

            Assert.AreEqual("Order", CodeGenerator.App.CustomizationDefaults.Load(root).Masters);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void The_step_forward_is_refused_on_an_empty_model()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();

        context.Render<OutputStage>(p => p
            .Add(c => c.Session, SessionWith(new CandidateDomainModel()))
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsTrue(navigation.HasStep, "the step should be visible, just not usable");
        Assert.IsFalse(navigation.CanContinue);
    }

    private static IRenderedComponent<OutputStage> RenderShop(
        BunitContext context, out BuildSession session,
        ITargetScaffolder? target = null, StageNavigation? navigation = null)
    {
        session = SessionWith(AShopModel(), target);

        var captured = session;

        return context.Render<OutputStage>(p => p
            .Add(c => c.Session, captured)
            .Add(c => c.Navigation, navigation ?? new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));
    }

    /// <summary>A session walked as far as the relationships node, the way the page walks it.</summary>
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
        session.Advance();   // discover -> review
        session.Advance();   // review -> relationships

        return session;
    }

    /// <summary>A master, a transaction and a line of that transaction — the three roles the
    /// screen table has to tell apart.</summary>
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

using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The page, as a page: the question it puts on screen before a model is lost, and what two tabs
/// looking at one workflow see.
/// </summary>
/// <remarks>
/// Everything the session decides is asserted in <c>DiscardConfirmationTests</c> without a
/// renderer. What is here is only what needs one — that the question is drawn, that its two
/// answers are wired to the right calls, and that a second circuit repaints when the first one
/// moves.
/// </remarks>
[TestClass]
public class WorkflowPageTests
{
    [TestMethod]
    public void Retyping_the_description_asks_before_the_model_is_lost()
    {
        using var context = PageFor(Discovered(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find("textarea").Change("a hotel");
        page.Find(".stage-continue").Click();

        Assert.IsNotNull(page.Find(".discard-question"));
        Assert.IsNotNull(session.Model);
        Assert.AreEqual("a shop", session.Description);
        Assert.AreEqual(BuildStage.Describe, session.Current);
    }

    [TestMethod]
    public void Confirming_the_question_applies_the_change_and_moves_on()
    {
        using var context = PageFor(Discovered(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find("textarea").Change("a hotel");
        page.Find(".stage-continue").Click();
        page.Find(".discard-confirm").Click();

        Assert.AreEqual("a hotel", session.Description);
        Assert.IsNull(session.Model);
        Assert.AreEqual(BuildStage.AppType, session.Current);
        Assert.IsEmpty(page.FindAll(".discard-question"));
    }

    [TestMethod]
    public void Keeping_the_model_leaves_the_workflow_exactly_where_it_was()
    {
        using var context = PageFor(Discovered(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find("textarea").Change("a hotel");
        page.Find(".stage-continue").Click();
        page.Find(".discard-keep").Click();

        Assert.AreEqual("a shop", session.Description);
        Assert.IsNotNull(session.Model);
        Assert.AreEqual(BuildStage.Describe, session.Current);
        Assert.IsEmpty(page.FindAll(".discard-question"));
    }

    /// <summary>
    /// The other way into the question: leaving a node by clicking a box commits what the panel
    /// holds, and that commit can cost the model too. The click must not be honoured until the
    /// question is answered, or the user would be reading a different node while the thing they
    /// were asked about happened behind them.
    /// </summary>
    [TestMethod]
    public void Clicking_another_box_with_a_question_pending_waits_for_the_answer()
    {
        using var context = PageFor(Discovered(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find("textarea").Change("a hotel");
        AppTypeBox(page).Click();

        Assert.IsNotNull(page.Find(".discard-question"));
        Assert.AreEqual(BuildStage.Describe, session.Current);
        Assert.IsNotNull(session.Model);
    }

    [TestMethod]
    public void Confirming_then_goes_where_the_click_was_headed()
    {
        using var context = PageFor(Discovered(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find("textarea").Change("a hotel");
        AppTypeBox(page).Click();
        page.Find(".discard-confirm").Click();

        Assert.AreEqual("a hotel", session.Description);
        Assert.AreEqual(BuildStage.AppType, session.Current);
    }

    /// <summary>
    /// Two tabs, one workflow. The session is a singleton so both already drive the same thing;
    /// what was missing is that the tab nobody touched went on drawing the node the workflow had
    /// left, with a live Continue button on it.
    /// </summary>
    [TestMethod]
    public void A_step_taken_in_one_tab_shows_in_the_other()
    {
        using var context = PageFor(New("a shop"), out var session);

        var first = context.Render<BuildWorkflow>();
        var second = context.Render<BuildWorkflow>();

        Assert.AreEqual(BuildStage.AppType, session.Current);

        first.Find(".stage-continue").Click();

        Assert.AreEqual(BuildStage.Data, session.Current);
        second.WaitForAssertion(
            () => StringAssert.Contains(second.Find(".panel h2").TextContent, "Data"));
    }

    [TestMethod]
    public void Cancel_asks_before_it_ends_the_run()
    {
        using var context = PageFor(Discovered(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find(".cancel").Click();

        Assert.IsNotNull(page.Find(".cancel-question"));
        Assert.AreEqual(SessionPhase.Running, session.Phase);
    }

    [TestMethod]
    public void Confirming_the_cancel_ends_the_run()
    {
        using var context = PageFor(Discovered(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find(".cancel").Click();
        page.Find(".cancel-confirm").Click();

        Assert.AreEqual(SessionPhase.Cancelled, session.Phase);
    }

    [TestMethod]
    public void Keeping_the_build_takes_the_question_away_and_changes_nothing_else()
    {
        using var context = PageFor(Discovered(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find(".cancel").Click();
        page.Find(".cancel-keep").Click();

        Assert.IsEmpty(page.FindAll(".cancel-question"));
        Assert.AreEqual(SessionPhase.Running, session.Phase);
        Assert.AreEqual(BuildStage.Describe, session.Current);
        Assert.IsNotNull(session.Model);
    }

    // Eight panels and no way through them without a mouse: nothing in the page handled a key,
    // and after each step the focus fell back to the document.
    [TestMethod]
    public void Enter_takes_the_step_the_panel_offers()
    {
        using var context = PageFor(New("a shop"), out var session);

        var page = context.Render<BuildWorkflow>();

        Assert.AreEqual(BuildStage.AppType, session.Current);

        page.Find(".panel").KeyDown(Key.Enter);

        Assert.AreEqual(BuildStage.Data, session.Current);
    }

    /// <summary>
    /// The generate panel offers nothing until a project has been written — nothing leads out of
    /// there — and Enter must respect that rather than stepping into a run stage with nothing to
    /// run.
    /// </summary>
    [TestMethod]
    public void Enter_does_nothing_where_no_step_is_offered()
    {
        var session = OnReview();
        session.Advance();

        using var context = PageFor(session, out _);

        var page = context.Render<BuildWorkflow>();

        Assert.AreEqual(BuildStage.Generate, session.Current);

        page.Find(".panel").KeyDown(Key.Enter);

        Assert.AreEqual(BuildStage.Generate, session.Current);
    }

    /// <summary>
    /// A named step is an action rather than movement — Generate writes a project — and the rule
    /// that keeps a click on another box from taking it is the rule that keeps a stray Enter from
    /// taking it too.
    /// </summary>
    [TestMethod]
    public void Enter_never_takes_a_named_step()
    {
        using var context = PageFor(OnReview(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find(".panel").KeyDown(Key.Enter);

        Assert.AreEqual(BuildStage.Review, session.Current);
        Assert.IsNull(session.Generated);
    }

    /// <summary>
    /// The panel that opens takes the keyboard, so the next answer can be typed. Asserted through
    /// the interop call because that is what Blazor's FocusAsync is: there is no rendered focus to
    /// look at in a test renderer.
    /// </summary>
    [TestMethod]
    public void The_panel_that_opens_takes_the_keyboard()
    {
        using var context = PageFor(New("a shop"), out _);

        context.Render<BuildWorkflow>();

        Assert.IsNotEmpty(
            context.JSInterop.Invocations.Where(i => i.Identifier.Contains("focus")));
    }

    /// <summary>
    /// A description is prose: Enter there is a new line, not a decision.
    /// </summary>
    /// <remarks>
    /// The propagation is real and so is this test: dropping the stopPropagation directive from
    /// the textarea reddens it on <c>Actual:&lt;AppType&gt;</c>, which is the page's Enter rule
    /// reaching an event that was never meant for it. The empty handler beside the directive is
    /// what gives Blazor a handled event to apply it to.
    /// </remarks>
    [TestMethod]
    public void The_description_keeps_Enter_for_itself()
    {
        using var context = PageFor(New(string.Empty), out var session);

        var page = context.Render<BuildWorkflow>();

        Assert.AreEqual(BuildStage.Describe, session.Current);

        page.Find("textarea").KeyDown(Key.Enter);

        Assert.AreEqual(BuildStage.Describe, session.Current);
    }

    /// <summary>
    /// A boundary nobody wired is a boundary that never runs, and the workflow would go on dying
    /// silently exactly as it did before — with every unit test still green.
    /// </summary>
    [TestMethod]
    public void The_open_panel_sits_inside_an_error_boundary_the_page_handles()
    {
        using var context = PageFor(New("a shop"), out _);

        var page = context.Render<BuildWorkflow>();

        var boundary = page.FindComponent<CodeGenerator.Web.Components.PanelErrorBoundary>();

        Assert.IsTrue(
            boundary.Instance.OnError.HasDelegate,
            "the page must handle what a panel throws");
    }

    /// <summary>The page has to actually set it, not merely be able to.</summary>
    [TestMethod]
    public void The_page_puts_the_stage_in_the_browser_tab()
    {
        using var context = PageFor(New("a shop"), out _);

        var page = context.Render<BuildWorkflow>();

        var title = page.FindComponent<Microsoft.AspNetCore.Components.Web.PageTitle>();
        var content = title.Instance.ChildContent;

        Assert.IsNotNull(content, "the page set a title with nothing in it");
        StringAssert.Contains(context.Render(content).Markup, "Application Type");
    }

    /// <summary>A session parked on the review node, where the step forward is named.</summary>
    private static BuildSession OnReview()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book" });

        var session = New("a shop");

        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model);
        session.Advance();

        return session;
    }

    /// <summary>The Application Type box, which is green and clickable because the fixture answered
    /// it.</summary>
    private static AngleSharp.Dom.IElement AppTypeBox(IRenderedComponent<BuildWorkflow> page)
        => page.FindAll(".node-open").First(b => b.TextContent.Contains("Application Type"));

    private static BunitContext PageFor(BuildSession session, out BuildSession registered)
    {
        var context = new BunitContext();

        // The page focuses the open panel, which is an interop call the strict default refuses.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        // The error boundary around the panel resolves an ILoggerFactory of its own.
        context.Services.AddLogging();
        context.Services.AddSingleton(session);
        registered = session;

        return context;
    }

    private static BuildSession New(string description)
        => new(description, @"C:\ws", AppCustomization.Default);

    /// <summary>A session with a model, sent back to the description it was discovered from —
    /// the one place a retype costs something.</summary>
    private static BuildSession Discovered()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book" });

        var session = New("a shop");

        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model);
        session.Advance();

        session.GoTo(BuildStage.Describe);

        return session;
    }
}

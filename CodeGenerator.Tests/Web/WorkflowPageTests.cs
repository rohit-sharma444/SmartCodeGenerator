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

    /// <summary>The App type box, which is green and clickable because the fixture answered
    /// it.</summary>
    private static AngleSharp.Dom.IElement AppTypeBox(IRenderedComponent<BuildWorkflow> page)
        => page.FindAll(".node-open").First(b => b.TextContent.Contains("App type"));

    private static BunitContext PageFor(BuildSession session, out BuildSession registered)
    {
        var context = new BunitContext();

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

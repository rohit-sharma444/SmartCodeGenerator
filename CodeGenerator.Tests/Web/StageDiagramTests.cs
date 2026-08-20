using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The diagram, which is also the navigation. Which node is active and what a click costs are
/// <see cref="BuildSession"/>'s decisions and are asserted in BuildSessionTests; what is asserted
/// here is that the four states look different, that a box says what it holds, and that the step
/// forward is drawn in the box the user is answering.
/// </summary>
[TestClass]
public class StageDiagramTests
{
    [TestMethod]
    public void Draws_every_node()
    {
        using var context = new BunitContext();

        var markup = Render(context, New()).Markup;

        foreach (var node in StageGraph.Nodes)
            StringAssert.Contains(markup, node.Title);
    }

    [TestMethod]
    public void Marks_the_active_node_apart_from_the_pending_ones()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New());

        Assert.HasCount(1, rendered.FindAll(".stage-node.active"));
        Assert.HasCount(7, rendered.FindAll(".stage-node.pending"));
    }

    [TestMethod]
    public void Marks_an_answered_node_done()
    {
        using var context = new BunitContext();
        var session = New("a shop");

        var rendered = Render(context, session);

        Assert.HasCount(1, rendered.FindAll(".stage-node.done"));
    }

    [TestMethod]
    public void Marks_a_failed_node_failed()
    {
        using var context = new BunitContext();
        var session = New();
        session.Fail("No file there.");

        var rendered = Render(context, session);

        Assert.HasCount(1, rendered.FindAll(".stage-node.failed"));
    }

    // The diagram is the summary of every decision as well as the way around it: a box that says
    // only "Describe" makes the user open it to remember what they typed.
    [TestMethod]
    public void An_answered_node_shows_the_answer_it_holds()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New("a library that lends books"));

        StringAssert.Contains(
            rendered.Find(".stage-node.done").TextContent, "a library that lends books");
    }

    [TestMethod]
    public void A_node_with_no_answer_shows_what_it_will_ask()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New());

        StringAssert.Contains(
            rendered.Find(".stage-node.pending").TextContent,
            StageGraph.Nodes[1].Blurb);
    }

    // Clicking a green node is how the workflow is revisitable, which is the whole point of
    // drawing it as a graph rather than a progress bar.
    [TestMethod]
    public void Clicking_a_done_node_raises_revisit_with_that_stage()
    {
        using var context = new BunitContext();
        var session = New("a shop");
        BuildStage? revisited = null;

        var rendered = context.Render<StageDiagram>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.Navigation, new StageNavigation())
            .Add(p => p.OnRevisit, (BuildStage stage) => revisited = stage));

        rendered.Find(".stage-node.done .node-open").Click();

        Assert.AreEqual(BuildStage.Describe, revisited);
    }

    [TestMethod]
    public void A_pending_node_is_not_clickable()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New());

        Assert.IsEmpty(rendered.FindAll(".stage-node.pending .node-open"));
    }

    // The step forward lives in the box being answered — there is no Next button under the panel.
    [TestMethod]
    public void The_open_box_carries_the_step_the_panel_offered()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true), label: "Generate");

        var rendered = Render(context, New(), navigation);

        var control = rendered.Find(".stage-node.active .stage-continue");

        StringAssert.Contains(control.TextContent, "Generate");
        Assert.IsFalse(control.HasAttribute("disabled"));
    }

    [TestMethod]
    public void A_box_whose_panel_offers_nothing_draws_no_step()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New());

        Assert.IsEmpty(rendered.FindAll(".stage-continue"));
    }

    // Disabled rather than missing: what the user is working towards should stay visible.
    [TestMethod]
    public void An_unready_step_is_drawn_and_refused()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true), ready: () => false);

        var rendered = Render(context, New(), navigation);

        Assert.IsTrue(rendered.Find(".stage-continue").HasAttribute("disabled"));
    }

    // The control raises; the page acts. Applying an answer and advancing the session are two
    // different things and only the page owns the second.
    [TestMethod]
    public void Taking_the_step_asks_the_page_to_act()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true));
        var asked = 0;

        var rendered = context.Render<StageDiagram>(parameters => parameters
            .Add(p => p.Session, New())
            .Add(p => p.Navigation, navigation)
            .Add(p => p.OnRevisit, (BuildStage _) => { })
            .Add(p => p.OnContinue, () => asked++));

        rendered.Find(".stage-continue").Click();

        Assert.AreEqual(1, asked);
    }

    // The next box and the arrow in the open box are one decision drawn twice: one is where the
    // eye is, the other is where the workflow is going.
    [TestMethod]
    public void The_next_box_lights_up_and_takes_the_same_step()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true));
        var asked = 0;

        var rendered = context.Render<StageDiagram>(parameters => parameters
            .Add(p => p.Session, New())
            .Add(p => p.Navigation, navigation)
            .Add(p => p.OnRevisit, (BuildStage _) => { })
            .Add(p => p.OnContinue, () => asked++));

        var next = rendered.Find(".stage-node.next");

        StringAssert.Contains(next.TextContent, StageGraph.Nodes[1].Title);

        next.QuerySelector(".node-open")!.Click();

        Assert.AreEqual(1, asked);
    }

    [TestMethod]
    public void No_box_lights_up_while_the_step_is_unusable()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true), ready: () => false);

        var rendered = Render(context, New(), navigation);

        Assert.IsEmpty(rendered.FindAll(".stage-node.next"));
    }

    // Until the project has been written there is nothing in the Generate box to go to, so it is
    // not offered as a click — the named button in the Review box is the only way to write it.
    [TestMethod]
    public void The_stage_after_a_named_step_is_not_offered_as_a_click()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true), label: "Generate");

        var rendered = Render(context, New(), navigation);

        Assert.IsEmpty(rendered.FindAll(".stage-node.next"));
        Assert.IsNotEmpty(rendered.FindAll(".stage-continue"));
    }

    // One step back, always in the same place. Clicking a green box goes anywhere; this goes to the
    // stage before this one, which is the move people actually want.
    [TestMethod]
    public void The_open_box_offers_one_step_back()
    {
        using var context = new BunitContext();
        var session = New("a shop");
        BuildStage? revisited = null;

        var rendered = context.Render<StageDiagram>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.Navigation, new StageNavigation())
            .Add(p => p.OnRevisit, (BuildStage stage) => revisited = stage)
            .Add(p => p.OnContinue, () => { }));

        rendered.Find(".stage-node.active .stage-back").Click();

        Assert.AreEqual(BuildStage.Describe, revisited);
    }

    [TestMethod]
    public void There_is_no_way_back_from_the_first_stage()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New());

        Assert.AreEqual(BuildStage.Describe, New().Current);
        Assert.IsEmpty(rendered.FindAll(".stage-back"));
    }

    // The arrow alone is not a name, and a screen reader that reads "button" has told the user
    // nothing.
    [TestMethod]
    public void The_way_back_says_where_it_goes()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New("a shop"));

        var back = rendered.Find(".stage-back");

        StringAssert.Contains(back.GetAttribute("title")!, "Describe");
        StringAssert.Contains(back.TextContent, "Describe");
    }

    /// <summary>
    /// The marks were written for the person who cannot see the colour — the comment above them
    /// says so — and were then hidden from assistive technology, which is that person. The state
    /// has to exist as words somewhere.
    /// </summary>
    [TestMethod]
    public void Every_node_says_its_state_in_words_and_not_only_in_colour()
    {
        using var context = new BunitContext();
        var session = New("a shop");
        session.SetTarget(new ConsoleTarget());

        var markup = Render(context, session).Markup;

        StringAssert.Contains(markup, "Answered");
        StringAssert.Contains(markup, "Current step");
        StringAssert.Contains(markup, "Not reached yet");
    }

    /// <summary>The glyph stays decorative, or a reader announces the state twice — once as a
    /// tick and once as a word.</summary>
    [TestMethod]
    public void The_glyph_itself_stays_decorative()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New("a shop"));

        foreach (var mark in rendered.FindAll(".stage-mark"))
            Assert.AreEqual("true", mark.GetAttribute("aria-hidden"));
    }

    [TestMethod]
    public void The_open_node_is_the_current_step_and_the_only_one()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New("a shop"));

        var current = rendered.FindAll("[aria-current=step]");

        Assert.HasCount(1, current);
        StringAssert.Contains(current[0].TextContent, "App type");
    }

    private static IRenderedComponent<StageDiagram> Render(
        BunitContext context, BuildSession session, StageNavigation? navigation = null)
        => context.Render<StageDiagram>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.Navigation, navigation ?? new StageNavigation())
            .Add(p => p.OnRevisit, (BuildStage _) => { }));

    private static BuildSession New(string description = "")
        => new(description, @"C:\ws", AppCustomization.Default);
}

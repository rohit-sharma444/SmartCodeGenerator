using AppScaffolding;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The diagram's rendering only. Which node is active and what a click costs are
/// <see cref="BuildSession"/>'s decisions and are asserted in BuildSessionTests; what is asserted
/// here is that the four states look different and that a green node is clickable.
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
            .Add(p => p.OnRevisit, (BuildStage stage) => revisited = stage));

        rendered.Find(".stage-node.done button").Click();

        Assert.AreEqual(BuildStage.Describe, revisited);
    }

    [TestMethod]
    public void A_pending_node_is_not_clickable()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New());

        Assert.IsEmpty(rendered.FindAll(".stage-node.pending button"));
    }

    private static IRenderedComponent<StageDiagram> Render(
        BunitContext context, BuildSession session)
        => context.Render<StageDiagram>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.OnRevisit, (BuildStage _) => { }));

    private static BuildSession New(string description = "")
        => new(description, @"C:\ws", AppCustomization.Default);
}

using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>The workflow's shape: nine nodes in order, and what changing one costs.</summary>
[TestClass]
public class StageGraphTests
{
    [TestMethod]
    public void Has_nine_nodes_in_workflow_order()
    {
        Assert.HasCount(9, StageGraph.Nodes);
        Assert.AreEqual(BuildStage.Describe, StageGraph.Nodes[0].Stage);
        Assert.AreEqual(BuildStage.Generate, StageGraph.Nodes[7].Stage);

        // Running it is its own stage, not a button on the generate panel: it is the step that
        // tells the user whether what was written actually works.
        Assert.AreEqual(BuildStage.Run, StageGraph.Nodes[8].Stage);
    }

    [TestMethod]
    public void Output_follows_review_and_precedes_generate()
    {
        // The split: Review settles what the classes ARE, Relationships settles how they connect
        // and what gets written.
        Assert.AreEqual(BuildStage.Output, StageGraph.Next(BuildStage.Review));
        Assert.AreEqual(BuildStage.Generate, StageGraph.Next(BuildStage.Output));
        Assert.AreEqual(BuildStage.Review, StageGraph.Previous(BuildStage.Output));
    }

    [TestMethod]
    public void Every_stage_in_the_enum_has_a_node()
    {
        // IndexOf throws on a stage with no node, so a member added to the enum and forgotten in
        // Nodes is a wiring bug that only shows when someone reaches that stage.
        foreach (var stage in Enum.GetValues<BuildStage>())
            Assert.IsTrue(
                StageGraph.Nodes.Any(n => n.Stage == stage), $"no node for {stage}");
    }

    [TestMethod]
    public void Anything_that_discards_the_model_discards_the_output_node_too()
    {
        // Relationships is derived from the model, so whatever throws the model away throws this
        // away with it. Missing one of these would leave a green tick on a node describing a
        // model that no longer exists.
        foreach (var changed in new[] { BuildStage.Describe, BuildStage.Data, BuildStage.Discover })
            Assert.Contains(
                BuildStage.Output,
                StageGraph.Invalidates(changed),
                $"{changed} should invalidate Output");
    }

    [TestMethod]
    public void Answering_the_output_node_invalidates_nothing()
        => Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Output));

    [TestMethod]
    public void Every_node_has_a_title_and_a_blurb()
    {
        foreach (var node in StageGraph.Nodes)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(node.Title), $"{node.Stage} has no title");
            Assert.IsFalse(string.IsNullOrWhiteSpace(node.Blurb), $"{node.Stage} has no blurb");
        }
    }

    [TestMethod]
    public void Next_walks_forward_and_stops_at_the_end()
    {
        Assert.AreEqual(BuildStage.AppType, StageGraph.Next(BuildStage.Describe));
        Assert.AreEqual(BuildStage.Review, StageGraph.Next(BuildStage.Discover));
        Assert.AreEqual(BuildStage.Run, StageGraph.Next(BuildStage.Generate));
        Assert.IsNull(StageGraph.Next(BuildStage.Run));
    }

    [TestMethod]
    public void Previous_walks_back_and_stops_at_the_start()
    {
        Assert.AreEqual(BuildStage.Describe, StageGraph.Previous(BuildStage.AppType));
        Assert.IsNull(StageGraph.Previous(BuildStage.Describe));
    }

    // One test per row of the spec's invalidation table. A description or a data source
    // changes what discovery would produce; an app type and a customization are not read
    // until Generate, so changing them costs nothing.
    [TestMethod]
    public void Changing_the_description_invalidates_discovery_onward()
    {
        var invalidated = StageGraph.Invalidates(BuildStage.Describe);

        Assert.HasCount(5, invalidated);
        Assert.Contains(BuildStage.Discover, invalidated);
        Assert.Contains(BuildStage.Review, invalidated);
        Assert.Contains(BuildStage.Output, invalidated);
        Assert.Contains(BuildStage.Generate, invalidated);

        // What was generated and run came from the old answer, so neither survives it either.
        Assert.Contains(BuildStage.Run, invalidated);
    }

    [TestMethod]
    public void Changing_the_data_source_invalidates_discovery_onward()
        => Assert.HasCount(5, StageGraph.Invalidates(BuildStage.Data));

    [TestMethod]
    public void Changing_discovery_invalidates_the_review_onward()
    {
        var invalidated = StageGraph.Invalidates(BuildStage.Discover);

        Assert.HasCount(5, invalidated);
        Assert.Contains(BuildStage.Review, invalidated);
        Assert.Contains(BuildStage.Output, invalidated);
        Assert.Contains(BuildStage.Generate, invalidated);
        Assert.Contains(BuildStage.Run, invalidated);
    }

    // The self-inclusion is the fix for the zero-classes defect: a SECOND completed discovery is
    // exactly as worthless to a model already discovered as a changed description is, and without
    // Discover listing itself here, BuildSession.WouldDiscardModel could never be true for a
    // second SetModel call — so it silently overwrote the model instead of asking first.
    [TestMethod]
    public void Discovery_invalidates_itself_so_a_second_completion_is_not_silently_applied()
        => Assert.Contains(BuildStage.Discover, StageGraph.Invalidates(BuildStage.Discover));

    [TestMethod]
    public void Changing_an_answer_read_only_at_generate_invalidates_nothing()
    {
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.AppType));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Look));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Review));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Generate));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Run));
    }

    /// <summary>
    /// The tab said "Build an application" and nothing else, for all eight stages and for both
    /// tabs at once — which is exactly when a title has work to do.
    /// </summary>
    [TestMethod]
    public void The_tab_title_says_which_stage_of_how_many()
    {
        var session = new BuildSession("a shop", @"C:\ws", AppScaffolding.AppCustomization.Default);

        var opening = StageGraph.TabTitle(session);

        StringAssert.Contains(opening, $"of {StageGraph.Nodes.Count}");
        StringAssert.Contains(opening, "Application Type");
        StringAssert.Contains(opening, "Build an application");
        StringAssert.Contains(opening, "2 of");

        session.SetTarget(new AppScaffolding.Targets.ConsoleTarget());
        session.Advance();

        StringAssert.Contains(StageGraph.TabTitle(session), "3 of");
        StringAssert.Contains(StageGraph.TabTitle(session), "Data");
    }

    [TestMethod]
    public void The_first_stage_is_one_not_zero()
    {
        var session = new BuildSession(
            string.Empty, @"C:\ws", AppScaffolding.AppCustomization.Default);

        StringAssert.Contains(StageGraph.TabTitle(session), "1 of");
    }
}

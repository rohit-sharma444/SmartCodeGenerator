using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>The workflow's shape: seven nodes in order, and what changing one costs.</summary>
[TestClass]
public class StageGraphTests
{
    [TestMethod]
    public void Has_seven_nodes_in_workflow_order()
    {
        Assert.HasCount(7, StageGraph.Nodes);
        Assert.AreEqual(BuildStage.Describe, StageGraph.Nodes[0].Stage);
        Assert.AreEqual(BuildStage.Generate, StageGraph.Nodes[6].Stage);
    }

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
        Assert.IsNull(StageGraph.Next(BuildStage.Generate));
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

        Assert.HasCount(3, invalidated);
        Assert.Contains(BuildStage.Discover, invalidated);
        Assert.Contains(BuildStage.Review, invalidated);
        Assert.Contains(BuildStage.Generate, invalidated);
    }

    [TestMethod]
    public void Changing_the_data_source_invalidates_discovery_onward()
        => Assert.HasCount(3, StageGraph.Invalidates(BuildStage.Data));

    [TestMethod]
    public void Changing_discovery_invalidates_the_review_onward()
    {
        var invalidated = StageGraph.Invalidates(BuildStage.Discover);

        Assert.HasCount(2, invalidated);
        Assert.Contains(BuildStage.Review, invalidated);
        Assert.Contains(BuildStage.Generate, invalidated);
    }

    [TestMethod]
    public void Changing_an_answer_read_only_at_generate_invalidates_nothing()
    {
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.AppType));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Look));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Review));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Generate));
    }
}

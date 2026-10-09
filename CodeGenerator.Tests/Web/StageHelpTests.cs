using AppScaffolding;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The help page for each stage, and the icon on the diagram that opens it.
/// </summary>
[TestClass]
public class StageHelpTests
{
    [TestMethod]
    public void Every_stage_has_help_text()
    {
        foreach (var stage in Enum.GetValues<BuildStage>())
        {
            var help = StageHelp.For(stage);

            Assert.IsFalse(string.IsNullOrWhiteSpace(help.Summary), $"{stage} has no summary");
            Assert.IsGreaterThanOrEqualTo(3, help.Sections.Count, $"{stage} has too few sections");
            Assert.IsTrue(help.Sections.Any(x => x.Numbered), $"{stage} has no numbered steps");
            foreach (var section in help.Sections)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(section.Heading), $"{stage} has a section with no heading");
                Assert.IsNotEmpty(section.Items, $"{stage}: {section.Heading} is empty");
            }
            Assert.IsFalse(string.IsNullOrWhiteSpace(help.Next), $"{stage} has no next");
        }
    }

    [TestMethod]
    public void The_address_of_a_stage_round_trips()
    {
        foreach (var stage in Enum.GetValues<BuildStage>())
            Assert.AreEqual(stage, StageHelp.Parse(StageHelp.AddressOf(stage).Split('/').Last()));
    }

    [TestMethod]
    public void An_unknown_name_is_not_a_stage()
    {
        Assert.IsNull(StageHelp.Parse("nonsense"));
        Assert.IsNull(StageHelp.Parse("42"));
    }

    [TestMethod]
    public void The_diagram_draws_one_help_link_for_each_stage()
    {
        using var context = new BunitContext();

        var links = RenderDiagram(context).FindAll("a.stage-help");

        Assert.HasCount(StageGraph.Nodes.Count, links);
        foreach (var node in StageGraph.Nodes)
        {
            var link = links.Single(l => l.GetAttribute("href") == StageHelp.AddressOf(node.Stage));
            Assert.AreEqual("_blank", link.GetAttribute("target"));
            Assert.AreEqual($"Help for {node.Title}", link.GetAttribute("aria-label"));
        }
    }

    [TestMethod]
    public void The_help_page_shows_the_text_for_its_stage()
    {
        using var context = new BunitContext();

        var rendered = context.Render<StageHelpPage>(p => p.Add(x => x.Stage, "data"));

        StringAssert.Contains(rendered.Markup, StageGraph.Nodes.First(n => n.Stage == BuildStage.Data).Title);
        StringAssert.Contains(rendered.Markup, StageHelp.For(BuildStage.Data).Summary);

        // Every section reaches the page, not only the first.
        foreach (var section in StageHelp.For(BuildStage.Data).Sections)
            Assert.IsTrue(
                rendered.FindAll("h2").Any(h => h.TextContent == section.Heading),
                $"the page lacks the section {section.Heading}");
    }

    [TestMethod]
    public void The_help_page_says_so_for_an_unknown_stage()
    {
        using var context = new BunitContext();

        var rendered = context.Render<StageHelpPage>(p => p.Add(x => x.Stage, "nonsense"));

        Assert.HasCount(1, rendered.FindAll(".stage-help-missing"));
    }

    private static IRenderedComponent<StageDiagram> RenderDiagram(BunitContext context)
    {
        var session = new BuildSession("", @"C:\ws", AppCustomization.Default);
        return context.Render<StageDiagram>(p => p
            .Add(x => x.Session, session)
            .Add(x => x.Navigation, new StageNavigation()));
    }
}

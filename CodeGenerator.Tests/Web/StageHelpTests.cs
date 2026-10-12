using AppScaffolding;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;
using Microsoft.Extensions.DependencyInjection;

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
    public void The_help_switch_has_no_default_arm_so_a_missing_stage_cannot_compile()
    {
        // Only a switch with no "_ =>" arm throws this for a value it has no arm for; with no
        // default, CS8509 (under CI's -warnaserror) refuses a named stage that has no help.
        Assert.ThrowsExactly<System.Runtime.CompilerServices.SwitchExpressionException>(
            () => StageHelp.For((BuildStage)999));
    }

    [TestMethod]
    public void The_describe_help_says_cancel_asks_first_only_when_there_are_answers()
    {
        var items = StageHelp.For(BuildStage.Describe).Sections.SelectMany(s => s.Items).ToList();
        var cancel = items.Single(i => i.StartsWith("A Cancel button.", StringComparison.Ordinal));

        // BuildSession.RequestCancel ends an empty build at once; it asks only when an answer would be lost.
        var session = new BuildSession("", @"C:\ws", AppCustomization.Default);
        Assert.IsTrue(session.RequestCancel(), "with no answers the build ends without a question");

        Assert.AreEqual(
            "A Cancel button. It ends the build. If you have already given answers, it asks you to confirm first.",
            cancel);
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
    public void The_help_page_is_drawn_in_the_theme_the_workflow_saved()
    {
        var root = Path.Combine(Path.GetTempPath(), "cg-help-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            WorkflowThemeStore.Save(root, "dark");
            using var context = new BunitContext();
            context.Services.AddSingleton(new BuildSession("", root, AppCustomization.Default));

            var found = context.Render<StageHelpPage>(p => p.Add(x => x.Stage, "data"));
            var missing = context.Render<StageHelpPage>(p => p.Add(x => x.Stage, "nonsense"));

            Assert.AreEqual("dark", found.Find("main.stage-help-page").GetAttribute("data-theme"));
            Assert.AreEqual("dark", missing.Find("main.stage-help-page").GetAttribute("data-theme"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void The_help_page_is_light_with_no_session()
    {
        using var context = new BunitContext();

        var rendered = context.Render<StageHelpPage>(p => p.Add(x => x.Stage, "data"));

        Assert.AreEqual(WorkflowThemes.Default, rendered.Find("main.stage-help-page").GetAttribute("data-theme"));
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

using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Node 8: how master and transaction screens arrange their fields, distinct from Output's
/// choice of which classes get a screen at all.
/// </summary>
/// <remarks>
/// The final whole-branch review found the "not applicable" message named one specific outcome —
/// "this run will use the standard grouped entry screens" — that is true only for WinForms on the
/// business layout. It is false for Blazor (a flat form per the spec) and false for WPF on a
/// non-business layout (also flat, per <c>WpfBusinessLayout.cs</c>'s own comment). These tests
/// cover both of the false cases and prove the message no longer makes that claim.
/// </remarks>
[TestClass]
public class EntryTemplateStageTests
{
    [TestMethod]
    public void The_not_applicable_message_makes_no_claim_for_blazor()
    {
        using var context = new BunitContext();

        var rendered = RenderStage(context, new BlazorTarget());

        Assert.IsNotEmpty(rendered.FindAll("p.not-applicable"));
        StringAssert.Contains(
            rendered.Markup, "Only available for a WPF application using the business layout");
        Assert.DoesNotContain("grouped", rendered.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void The_not_applicable_message_makes_no_claim_for_wpf_on_a_non_business_layout()
    {
        using var context = new BunitContext();

        var rendered = RenderStage(context, new WpfTarget(), AppLayout.Tabs);

        Assert.IsNotEmpty(rendered.FindAll("p.not-applicable"));
        Assert.DoesNotContain("grouped", rendered.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void The_template_choice_is_offered_for_wpf_on_the_business_layout()
    {
        using var context = new BunitContext();

        var rendered = RenderStage(context, new WpfTarget());

        Assert.HasCount(EntryTemplateCatalog.Keys.Count, rendered.FindAll("input.entry-template"));
    }

    /// <summary>Each option is a card, not a bare radio: a screenshot of the template it names,
    /// so the choice can be made by eye rather than by parsing a key. The image path is the
    /// catalogue key itself, so a template added to the catalogue without its own screenshot fails
    /// loudly (a broken image) rather than silently reusing another template's picture.</summary>
    [TestMethod]
    public void Every_option_shows_a_screenshot_named_after_its_catalogue_key()
    {
        using var context = new BunitContext();

        var rendered = RenderStage(context, new WpfTarget());

        var images = rendered.FindAll("img.entry-template-preview");

        Assert.HasCount(EntryTemplateCatalog.Keys.Count, images);

        for (var i = 0; i < EntryTemplateCatalog.Keys.Count; i++)
        {
            var key = EntryTemplateCatalog.Keys[i];

            Assert.AreEqual($"entry-templates/{key}.png", images[i].GetAttribute("src"));
        }
    }

    [TestMethod]
    public void Choosing_a_template_and_advancing_records_it_on_the_customization()
    {
        using var context = new BunitContext();
        var (rendered, session, navigation) = RenderStageWithSession(context, new WpfTarget());

        rendered.Find("input.entry-template[value=tabbed]").Change(true);
        navigation.ApplyAsync().GetAwaiter().GetResult();

        Assert.AreEqual(EntryTemplate.Tabbed, session.Customization.EntryTemplate);
    }

    /// <summary>The zoom is an inspection aid, not a way to answer the question — clicking a
    /// thumbnail must not also check that card's radio, or a user previewing a template they do
    /// not intend to pick would silently change their answer.</summary>
    [TestMethod]
    public void Clicking_a_preview_opens_it_enlarged_without_changing_the_selection()
    {
        using var context = new BunitContext();
        var rendered = RenderStage(context, new WpfTarget());

        Assert.IsEmpty(rendered.FindAll(".entry-template-zoom"));

        rendered.Find("img.entry-template-preview[data-key=split]").Click();

        var zoom = rendered.Find(".entry-template-zoom");
        Assert.AreEqual("entry-templates/split.png", zoom.QuerySelector("img")!.GetAttribute("src"));

        // Grouped is the default and was never touched.
        Assert.IsTrue(rendered.Find("input.entry-template[value=grouped]").HasAttribute("checked"));
        Assert.IsFalse(rendered.Find("input.entry-template[value=split]").HasAttribute("checked"));
    }

    [TestMethod]
    public void Closing_the_zoom_with_its_button_removes_the_overlay()
    {
        using var context = new BunitContext();
        var rendered = RenderStage(context, new WpfTarget());

        rendered.Find("img.entry-template-preview[data-key=ribbon]").Click();
        Assert.IsNotEmpty(rendered.FindAll(".entry-template-zoom"));

        rendered.Find(".entry-template-zoom-close").Click();

        Assert.IsEmpty(rendered.FindAll(".entry-template-zoom"));
    }

    [TestMethod]
    public void Pressing_escape_closes_the_zoom_too()
    {
        using var context = new BunitContext();
        var rendered = RenderStage(context, new WpfTarget());

        rendered.Find("img.entry-template-preview[data-key=tabbed]").Click();
        Assert.IsNotEmpty(rendered.FindAll(".entry-template-zoom"));

        rendered.Find(".entry-template-zoom-card").KeyDown(Key.Escape);

        Assert.IsEmpty(rendered.FindAll(".entry-template-zoom"));
    }

    private static IRenderedComponent<EntryTemplateStage> RenderStage(
        BunitContext context, ITargetScaffolder target, AppLayout? layout = null)
        => RenderStageWithSession(context, target, layout).Rendered;

    private static (IRenderedComponent<EntryTemplateStage> Rendered, BuildSession Session,
        StageNavigation Navigation) RenderStageWithSession(
            BunitContext context, ITargetScaffolder target, AppLayout? layout = null)
    {
        var session = new BuildSession(
            "a shop", Path.GetTempPath(), AppCustomization.Default with { Layout = layout });

        session.SetTarget(target);

        var navigation = new StageNavigation();

        var rendered = context.Render<EntryTemplateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        return (rendered, session, navigation);
    }
}

using AppScaffolding;
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

        Assert.IsNotEmpty(rendered.FindAll("select.entry-template"));
    }

    private static IRenderedComponent<EntryTemplateStage> RenderStage(
        BunitContext context, ITargetScaffolder target, AppLayout? layout = null)
    {
        var session = new BuildSession(
            "a shop", Path.GetTempPath(), AppCustomization.Default with { Layout = layout });

        session.SetTarget(target);

        return context.Render<EntryTemplateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));
    }
}

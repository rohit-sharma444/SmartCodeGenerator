using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class CustomizationSummaryTests
{
    [TestMethod]
    public void An_explicit_entry_template_is_named_for_wpf_business()
    {
        var customization = AppCustomization.Default with { EntryTemplate = EntryTemplate.Tabbed };

        var lines = CustomizationSummary.Lines(customization, new WpfTarget());

        CollectionAssert.Contains(lines.ToList(), "  Entry template: tabbed");
    }

    [TestMethod]
    public void No_entry_template_produces_no_line()
    {
        var lines = CustomizationSummary.Lines(AppCustomization.Default, new WpfTarget());

        Assert.IsFalse(lines.Any(l => l.Contains("Entry template")));
    }

    /// <summary>The Global Constraint gating the prompt/menu/inline option — WPF only, business
    /// layout only — is NOT enforced by the inline CLI parser (issue found in the final
    /// whole-branch review): `build app: a shop entry-template ribbon winforms` is silently
    /// accepted and stored on <c>AppCustomization.EntryTemplate</c>. The summary must not echo a
    /// setting that took nothing downstream reads.</summary>
    [TestMethod]
    public void An_entry_template_set_on_a_non_wpf_target_is_not_named()
    {
        var customization = AppCustomization.Default with { EntryTemplate = EntryTemplate.Ribbon };

        var lines = CustomizationSummary.Lines(customization, new WinFormsTarget());

        Assert.IsFalse(lines.Any(l => l.Contains("Entry template")));
    }

    /// <summary>Same constraint, the other gate: WPF but not the business layout.</summary>
    [TestMethod]
    public void An_entry_template_set_on_a_non_business_layout_is_not_named()
    {
        var customization = AppCustomization.Default with
        {
            EntryTemplate = EntryTemplate.Ribbon,
            Layout = AppLayout.Tabs,
        };

        var lines = CustomizationSummary.Lines(customization, new WpfTarget());

        Assert.IsFalse(lines.Any(l => l.Contains("Entry template")));
    }

    [TestMethod]
    public void An_entry_template_with_no_target_chosen_yet_is_not_named()
    {
        var customization = AppCustomization.Default with { EntryTemplate = EntryTemplate.Ribbon };

        var lines = CustomizationSummary.Lines(customization, target: null);

        Assert.IsFalse(lines.Any(l => l.Contains("Entry template")));
    }
}

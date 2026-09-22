using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class CustomizationSummaryTests
{
    [TestMethod]
    public void An_explicit_entry_template_is_named()
    {
        var customization = AppCustomization.Default with { EntryTemplate = EntryTemplate.Tabbed };

        var lines = CustomizationSummary.Lines(customization);

        CollectionAssert.Contains(lines.ToList(), "  Entry template: tabbed");
    }

    [TestMethod]
    public void No_entry_template_produces_no_line()
    {
        var lines = CustomizationSummary.Lines(AppCustomization.Default);

        Assert.IsFalse(lines.Any(l => l.Contains("Entry template")));
    }
}

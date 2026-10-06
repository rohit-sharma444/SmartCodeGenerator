using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A WPF grid showed dates as en-US (<c>8/5/2026 12:00:00 AM</c>) beside DatePickers in the
/// machine's format (<c>05-Aug-26</c>): bindings format in the element's Language, which is en-US
/// unless overridden. Every WPF app now carries the override and a date-cell converter, and every
/// layout's grid handler uses it.
/// </summary>
[TestClass]
public class WpfDisplayCultureTests
{
    [TestMethod]
    public void Every_layout_emits_the_display_culture_and_its_grid_handler_uses_it()
    {
        foreach (var layout in new[] { AppLayout.Tabs, AppLayout.Sidebar, AppLayout.MasterDetail, AppLayout.Dashboard, AppLayout.Business })
        {
            var files = new WpfTarget().FrontEndFiles(
                "Shop", ["Customer"], AppCustomization.Default with { Layout = layout });

            var culture = files.SingleOrDefault(f => f.Name == WpfDisplayCultureTemplate.FileName);
            Assert.IsNotNull(culture, $"{layout} has no {WpfDisplayCultureTemplate.FileName}");
            StringAssert.Contains(culture.Source, "FrameworkElement.LanguageProperty.OverrideMetadata");

            var codeBehind = files.Single(f => f.Name == "MainWindow.xaml.cs").Source;
            StringAssert.Contains(codeBehind, "DisplayCulture.DateCell", $"{layout}");
        }
    }

    [TestMethod]
    public void The_display_culture_compiles()
        => EmittedDesktopSource.AssertCompiles(WpfDisplayCultureTemplate.Source("Shop"));
}

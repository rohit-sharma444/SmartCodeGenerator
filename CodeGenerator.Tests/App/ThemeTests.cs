using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>Theme resources are emitted only when something was themed, so an unthemed run is
/// byte-identical to the output before customization existed.</summary>
[TestClass]
public class ThemeTests
{
    private static readonly AppCustomization Dark =
        AppCustomization.Default with { Theme = AppTheme.Dark, Accent = "#0B6E4F" };

    [TestMethod]
    public void An_unthemed_wpf_app_gets_no_theme_file()
        => Assert.IsFalse(
            new WpfTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Any(f => f.Name == "Theme.xaml"));

    [TestMethod]
    public void A_themed_wpf_app_gets_a_theme_file()
    {
        var theme = new WpfTarget().FrontEndFiles("Shop", ["Order"], Dark)
            .Single(f => f.Name == "Theme.xaml");

        Assert.AreEqual("Views", theme.RelativeFolder);
        StringAssert.Contains(theme.Source, "#0B6E4F");
        StringAssert.Contains(theme.Source, "ResourceDictionary");
    }

    /// <summary>The window has to consume the dictionary or the palette is dead markup.</summary>
    [TestMethod]
    public void A_themed_window_binds_to_the_theme_brushes()
    {
        var xaml = new WpfTarget().FrontEndFiles("Shop", ["Order"], Dark)
            .Single(f => f.Name == "MainWindow.xaml").Source;

        StringAssert.Contains(xaml, "{DynamicResource AppBackgroundBrush}");
    }

    [TestMethod]
    public void An_unthemed_window_is_unchanged()
    {
        var xaml = new WpfTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
            .Single(f => f.Name == "MainWindow.xaml").Source;

        Assert.IsFalse(xaml.Contains("DynamicResource", StringComparison.Ordinal));
    }

    [TestMethod]
    public void A_themed_winforms_app_sets_its_colours()
    {
        var source = new WinFormsTarget().FrontEndFiles("Shop", ["Order"], Dark)
            .Single(f => f.Name == "MainForm.cs").Source;

        StringAssert.Contains(source, "BackColor = ColorTranslator.FromHtml(");
    }

    [TestMethod]
    public void An_unthemed_winforms_app_sets_no_colours()
        => Assert.IsFalse(
            new WinFormsTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Single(f => f.Name == "MainForm.cs").Source
                .Contains("ColorTranslator", StringComparison.Ordinal));
}

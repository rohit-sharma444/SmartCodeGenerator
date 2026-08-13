using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.Wpf;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfLayoutNeutralityTests
{
    private static LayoutContext Context(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return new LayoutContext(
            RootNamespace: "Payroll",
            ProjectName: "Payroll",
            Customization: settings,
            Palette: ThemePalette.Resolve(settings.Theme, settings.Accent));
    }

    [TestMethod]
    public void TabsLayout_EmitsExactlyWhatTheTemplateEmitsToday()
    {
        var files = WpfLayouts.Resolve(AppLayout.Tabs).ViewFiles(Context());

        var xaml = files.Single(f => f.Name == "MainWindow.xaml");
        var codeBehind = files.Single(f => f.Name == "MainWindow.xaml.cs");

        // Equality, not Contains: this is the byte-neutrality contract. The tabs layout
        // delegates, it does not reimplement.
        Assert.AreEqual(
            MainWindowTemplate.Xaml("Payroll", "Payroll", themed: false, iconFileName: null),
            xaml.Source);
        Assert.AreEqual(MainWindowTemplate.CodeBehind("Payroll"), codeBehind.Source);
        Assert.AreEqual("Views", xaml.RelativeFolder);
    }

    [TestMethod]
    public void TabsLayout_PassesTheTitleThemeAndIconThrough()
    {
        var customization = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = AppCustomization.CompanyTitlePattern,
            Theme = AppTheme.Dark,
            IconPath = @"D:\brand\logo.ico",
        };

        var files = WpfLayouts.Resolve(AppLayout.Tabs).ViewFiles(Context(customization));
        var xaml = files.Single(f => f.Name == "MainWindow.xaml").Source;

        Assert.AreEqual(
            MainWindowTemplate.Xaml("Payroll", "Codis Ltd - Payroll", themed: true, iconFileName: "app.ico"),
            xaml);
    }

    [TestMethod]
    public void WpfTarget_DefaultsToTheTabsLayoutOutput()
    {
        var files = new WpfTarget().FrontEndFiles("Payroll", ["Employee"], AppCustomization.Default);

        Assert.AreEqual(
            MainWindowTemplate.Xaml("Payroll", "Payroll", themed: false, iconFileName: null),
            files.Single(f => f.Name == "MainWindow.xaml").Source);
    }

    [TestMethod]
    public void Registry_DefaultsToTabs()
    {
        Assert.AreEqual("tabs", WpfLayouts.Default.Key);
    }
}

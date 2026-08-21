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
        // `nologin` is part of the promise, not an incidental default. The guarantee is
        // that a NAMED layout with NO login reproduces the pre-auth output byte for byte;
        // with a login those layouts legitimately gain a login window, a Users pane and a
        // sign-out control, which SignOutReachableTests covers.
        var settings = (customization ?? AppCustomization.Default) with { Login = false };

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
    public void WpfTarget_WithTabsNamed_EmitsTheOriginalOutput()
    {
        // The promise, exactly: a build that NAMES tabs and asks for no login reproduces the
        // pre-auth output byte for byte. Naming nothing now means the business shell, which is a
        // different shape on purpose.
        var files = new WpfTarget().FrontEndFiles(
            "Payroll", ["Employee"],
            AppCustomization.Default with { Login = false, Layout = AppLayout.Tabs });

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

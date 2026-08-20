using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsLayoutNeutralityTests
{
    private static LayoutContext Context(AppCustomization? customization = null)
    {
        // `nologin` is part of the promise, not an incidental default. The guarantee is
        // that a NAMED layout with NO login reproduces the pre-auth output byte for byte;
        // with a login those layouts legitimately gain a login window, a Users pane and a
        // sign-out control, which SignOutReachableTests covers.
        var settings = (customization ?? AppCustomization.Default) with { Login = false };

        return new LayoutContext(
            "Payroll.WinForms", "Payroll.WinForms", settings,
            ThemePalette.Resolve(settings.Theme, settings.Accent));
    }

    [TestMethod]
    public void TabsLayout_EmitsExactlyWhatTheTemplateEmitsToday()
    {
        var files = WinFormsLayouts.Resolve(AppLayout.Tabs).ViewFiles(Context());
        var form = files.Single(f => f.Name == "MainForm.cs");

        Assert.AreEqual(
            WinFormsMainFormTemplate.Source("Payroll.WinForms", "Payroll.WinForms", palette: null, iconFileName: null),
            form.Source);
        Assert.AreEqual("Views", form.RelativeFolder);
    }

    [TestMethod]
    public void TabsLayout_EscapesTheTitleForASourceLiteralNotForMarkup()
    {
        // WinForms emits its caption into C# source (Text = "..."), WPF into a XAML attribute, so
        // the two want different escaping. Passing the XML-escaped title here would emit
        // "Smith &amp; Sons" into a string literal -- wrong, and invisible to any fixture whose
        // company name has no markup character in it.
        var customization = AppCustomization.Default with
        {
            Company = """Smith & Sons "Ltd\""",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        };

        var form = WinFormsLayouts.Resolve(AppLayout.Tabs)
            .ViewFiles(Context(customization))
            .Single(f => f.Name == "MainForm.cs").Source;

        Assert.AreEqual(
            WinFormsMainFormTemplate.Source(
                "Payroll.WinForms",
                customization.TitleLiteralFor("Payroll.WinForms"),
                palette: null,
                iconFileName: null),
            form);

        Assert.DoesNotContain("&amp;", form);
    }

    [TestMethod]
    public void WinFormsTarget_DefaultsToTheTabsLayoutOutput()
    {
        // `nologin`, because that is the promise: a build that names no layout and asks for
        // no login is the one that must reproduce the pre-auth output byte for byte.
        var files = new WinFormsTarget().FrontEndFiles(
            "Payroll.WinForms", ["Employee"], AppCustomization.Default with { Login = false });

        Assert.AreEqual(
            WinFormsMainFormTemplate.Source("Payroll.WinForms", "Payroll.WinForms", palette: null, iconFileName: null),
            files.Single(f => f.Name == "MainForm.cs").Source);
    }

    [TestMethod]
    public void Supports_IsTrueForTheTwoDesktopTargetsOnly()
    {
        Assert.IsTrue(AppLayouts.Supports("wpf"));
        Assert.IsTrue(AppLayouts.Supports("winforms"));
        Assert.IsTrue(AppLayouts.Supports("WPF"));

        Assert.IsFalse(AppLayouts.Supports("console"));
        Assert.IsFalse(AppLayouts.Supports("webapi"));
        Assert.IsFalse(AppLayouts.Supports("worker"));
        Assert.IsFalse(AppLayouts.Supports("classlib"));
    }
}

using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsSidebarLayoutTests
{
    private static string Source(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return WinFormsLayouts.Resolve(AppLayout.Sidebar).ViewFiles(
                new LayoutContext("Payroll.WinForms", "Payroll.WinForms", settings,
                    ThemePalette.Resolve(settings.Theme, settings.Accent)))
            .Single(f => f.Name == "MainForm.cs").Source;
    }

    [TestMethod]
    public void ListsTheEntitiesBesideAPaneHost()
    {
        var source = Source();

        StringAssert.Contains(source, "new ListBox");
        StringAssert.Contains(source, "DisplayMember = \"Title\"");
        StringAssert.Contains(source, "DockStyle.Left");
        StringAssert.Contains(source, "SelectedIndexChanged");
        StringAssert.Contains(source, "BuildPane(");
    }

    [TestMethod]
    public void SelectsTheFirstEntityOnStartup()
    {
        StringAssert.Contains(Source(), "SelectedIndex = 0;");
    }

    [TestMethod]
    public void KeepsTheGridBindingAndColumnRules()
    {
        var source = Source();

        // The BindingSource republish is what makes Add/Delete visible in a DataGridView, which
        // does not listen to INotifyCollectionChanged.
        StringAssert.Contains(source, "new BindingSource");
        StringAssert.Contains(source, "binding.ResetBindings(false)");
        StringAssert.Contains(source, "ApplyColumnRules");
        StringAssert.Contains(source, "DataBindingComplete");
    }

    [TestMethod]
    public void PaneCarriesNoPropertyGridUnlessOneWasAskedFor()
    {
        // The sidebar shares BuildPane with master-detail, which is the layout that wants the
        // editor. A default-on flag would put one in every layout.
        Assert.DoesNotContain("PropertyGrid", Source());
        StringAssert.Contains(Source(), "RowCount = 3,");
    }

    [TestMethod]
    public void Compiles()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source()),
            ("ViewModels/MainViewModel.cs", ViewModelStubs.WinFormsHost),
            ("Auth/Session.cs", ViewModelStubs.WinFormsSession));
    }

    [TestMethod]
    public void CompilesWhenThemedAndIconed()
    {
        // The themed and iconed fragments are spliced into the constructor, so they are the lines
        // most likely to land at the wrong depth or with a broken literal.
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source(AppCustomization.Default with
            {
                Theme = AppTheme.Dark,
                IconPath = @"D:\brand\logo.ico",
            })),
            ("ViewModels/MainViewModel.cs", ViewModelStubs.WinFormsHost),
            ("Auth/Session.cs", ViewModelStubs.WinFormsSession));
    }

    [TestMethod]
    public void EscapesTheTitleForASourceLiteralNotForMarkup()
    {
        // Same trap as the tabs layout: this caption is emitted into C# source, so XML escaping
        // would put "Smith &amp; Sons" into a string literal.
        var source = Source(AppCustomization.Default with
        {
            Company = """Smith & Sons "Ltd\""",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        });

        Assert.DoesNotContain("&amp;", source);
        StringAssert.Contains(source, "Smith & Sons \\\"Ltd\\\\");
    }

    [TestMethod]
    public void PaintsWithTheThemeColoursOnlyWhenThemed()
    {
        StringAssert.Contains(
            Source(AppCustomization.Default with { Theme = AppTheme.Dark }),
            "ColorTranslator.FromHtml");

        Assert.DoesNotContain("ColorTranslator", Source());
    }
}

using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsMasterDetailLayoutTests
{
    private static string Source(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return WinFormsLayouts.Resolve(AppLayout.MasterDetail).ViewFiles(
                new LayoutContext("Payroll.WinForms", "Payroll.WinForms", settings,
                    ThemePalette.Resolve(settings.Theme, settings.Accent)))
            .Single(f => f.Name == "MainForm.cs").Source;
    }

    [TestMethod]
    public void KeepsTheTabsAndAddsAPropertyGridUnderTheRecords()
    {
        var source = Source();

        StringAssert.Contains(source, "new TabControl");
        StringAssert.Contains(source, "new PropertyGrid");
        StringAssert.Contains(source, "detail.SelectedObject = grid.CurrentRow?.DataBoundItem");
    }

    [TestMethod]
    public void GivesTheDetailRowItsOwnRowInThePaneLayout()
    {
        // A PropertyGrid built but never placed would satisfy the assertions above and show
        // nothing. Four rows, and the button and status rows shifted down to make room.
        var source = Source();

        StringAssert.Contains(source, "RowCount = 4,");
        StringAssert.Contains(source, "layout.Controls.Add(detail, 0, 1);");
        StringAssert.Contains(source, "layout.Controls.Add(buttons, 0, 2);");
        StringAssert.Contains(source, "layout.Controls.Add(status, 0, 3);");
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
        Assert.DoesNotContain("&amp;", Source(AppCustomization.Default with
        {
            Company = "Smith & Sons",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        }));
    }

    [TestMethod]
    public void LeaksNoGeneratorImplementationDetails()
    {
        var source = Source();

        Assert.DoesNotContain("WinFormsPaneCode", source);
        Assert.DoesNotContain("WinFormsMainFormTemplate", source);
    }
}

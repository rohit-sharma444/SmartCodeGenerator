using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsDashboardLayoutTests
{
    private static string Source(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return WinFormsLayouts.Resolve(AppLayout.Dashboard).ViewFiles(
                new LayoutContext("Payroll.WinForms", "Payroll.WinForms", settings,
                    ThemePalette.Resolve(settings.Theme, settings.Accent)))
            .Single(f => f.Name == "MainForm.cs").Source;
    }

    [TestMethod]
    public void ShowsACardPerEntityWithALiveRowCount()
    {
        var source = Source();

        StringAssert.Contains(source, "new FlowLayoutPanel");
        StringAssert.Contains(source, "entity.Items.Cast<object>().Count()");

        // WinForms has no binding path to Items.Count, so the count is refreshed from the
        // collection's own change notification.
        StringAssert.Contains(source, "INotifyCollectionChanged");
    }

    [TestMethod]
    public void NavigatesToAnEntityAndBack()
    {
        var source = Source();

        StringAssert.Contains(source, "BuildPane(");
        StringAssert.Contains(source, "\"Back\"");
    }

    [TestMethod]
    public void ShowingAPaneHidesTheCardsAndRevealsTheWayBack()
    {
        // A Back button that is built but never shown, or cards left visible behind the pane, would
        // satisfy every assertion above and leave the app with no way home.
        var source = Source();

        StringAssert.Contains(source, "_cards.Visible = false;");
        StringAssert.Contains(source, "_toolbar.Visible = true;");
        StringAssert.Contains(source, "_cards.Visible = true;");
        StringAssert.Contains(source, "_toolbar.Visible = false;");
    }

    [TestMethod]
    public void DisposesTheOpenPaneWithoutEnumeratingAControlsCollectionItMutates()
    {
        // Disposing a control removes it from its parent's Controls, so disposing inside a foreach
        // over that collection skips siblings. Snapshot, clear, then dispose.
        var source = Source();

        StringAssert.Contains(source, "_host.Controls.Cast<Control>().ToArray()");
        Assert.DoesNotContain("foreach (Control existing in _host.Controls)", source);
    }

    [TestMethod]
    public void Compiles()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source()),
            ("ViewModels/MainViewModel.cs", ViewModelStubs.WinFormsHost));
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
            ("ViewModels/MainViewModel.cs", ViewModelStubs.WinFormsHost));
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
    public void EveryLayoutIsRegisteredForBothTargets()
    {
        // The registries fall back to Default for an unimplemented layout, which would make a
        // missing layout look like a working tabs app rather than a failure.
        foreach (var layout in Enum.GetValues<AppLayout>())
        {
            Assert.AreEqual(AppLayoutCatalog.KeyFor(layout), WinFormsLayouts.Resolve(layout).Key);
            Assert.AreEqual(
                AppLayoutCatalog.KeyFor(layout),
                AppScaffolding.Layouts.Wpf.WpfLayouts.Resolve(layout).Key);
        }
    }
}

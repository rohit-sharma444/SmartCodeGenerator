using System.Xml.Linq;
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.Wpf;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfSidebarLayoutTests
{
    private static string Xaml(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        var files = WpfLayouts.Resolve(AppLayout.Sidebar).ViewFiles(
            new LayoutContext("Payroll", "Payroll", settings,
                ThemePalette.Resolve(settings.Theme, settings.Accent)));

        return files.Single(f => f.Name == "MainWindow.xaml").Source;
    }

    [TestMethod]
    public void ListsTheEntitiesBesideTheSelectedOnesPane()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "x:Class=\"Payroll.Views.MainWindow\"");
        StringAssert.Contains(xaml, "<ListBox x:Name=\"EntityList\"");
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Entities}\"");
        StringAssert.Contains(xaml, "DisplayMemberPath=\"Title\"");
        StringAssert.Contains(xaml, "Content=\"{Binding SelectedItem, ElementName=EntityList}\"");
        StringAssert.Contains(xaml, "ContentTemplate=\"{StaticResource EntityPane}\"");
    }

    [TestMethod]
    public void SelectsTheFirstEntityOnStartup()
    {
        // Same reason the TabControl needs it: a Selector bound to a plain ObservableCollection
        // rather than a CollectionView opens with nothing selected.
        StringAssert.Contains(Xaml(), "SelectedIndex=\"0\"");
    }

    [TestMethod]
    public void CarriesTheWholePaneTheTabbedLayoutHas()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "<DataTemplate x:Key=\"EntityPane\">");
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Items}\"");
        StringAssert.Contains(xaml, "SelectedItem=\"{Binding Selected, Mode=TwoWay}\"");
        StringAssert.Contains(xaml, "AutoGenerateColumns=\"True\"");
        StringAssert.Contains(xaml, "CanUserAddRows=\"False\"");
        StringAssert.Contains(xaml, "AutoGeneratingColumn=\"OnAutoGeneratingColumn\"");
        StringAssert.Contains(xaml, "Command=\"{Binding AddCommand}\"");
        StringAssert.Contains(xaml, "Command=\"{Binding DeleteCommand}\"");
        StringAssert.Contains(xaml, "Command=\"{Binding SaveCommand}\"");
        StringAssert.Contains(xaml, "Text=\"{Binding Status}\"");
    }

    [TestMethod]
    public void ComposesIntoWellFormedMarkupWithThePaneInsideWindowResources()
    {
        // The only mechanical check markup allows: Roslyn cannot read XAML, and every other
        // assertion here is a substring, which a fragment spliced in at the wrong nesting depth
        // would satisfy just as happily. Both variants are parsed because the themed one splices
        // brush attributes into three separate elements.
        foreach (var xaml in new[] { Xaml(), Xaml(AppCustomization.Default with { Theme = AppTheme.Dark }) })
        {
            var root = XDocument.Parse(xaml).Root!;

            Assert.AreEqual("Window", root.Name.LocalName);

            var resources = root.Elements().Single(e => e.Name.LocalName == "Window.Resources");
            var pane = resources.Elements().Single(e => e.Name.LocalName == "DataTemplate");

            Assert.AreEqual("EntityPane", pane.Attributes().Single(a => a.Name.LocalName == "Key").Value);
            Assert.IsNotNull(root.Elements().Single(e => e.Name.LocalName == "Grid"));
        }
    }

    [TestMethod]
    public void ReusesTodaysCodeBehindUnchanged()
    {
        // The pane's DataGrid still raises AutoGeneratingColumn, so the handler — and the FK
        // combo logic with it — is needed verbatim. No second copy to keep in sync.
        var files = WpfLayouts.Resolve(AppLayout.Sidebar).ViewFiles(
            new LayoutContext("Payroll", "Payroll", AppCustomization.Default, null));

        Assert.AreEqual(
            // login: true to match the fixture. The claim is that the layout REUSES the
            // shared code-behind, not that the shared one never changes.
            MainWindowTemplate.CodeBehind("Payroll", login: true),
            files.Single(f => f.Name == "MainWindow.xaml.cs").Source);
    }

    [TestMethod]
    public void PaintsWithTheThemeBrushesOnlyWhenThemed()
    {
        var themed = Xaml(AppCustomization.Default with { Theme = AppTheme.Dark });
        var plain = Xaml();

        StringAssert.Contains(themed, "{DynamicResource AppBackgroundBrush}");
        StringAssert.Contains(themed, "{DynamicResource AppSurfaceBrush}");
        Assert.DoesNotContain("DynamicResource", plain);
    }

    [TestMethod]
    public void ShowsTheWindowIconAndTitleItWasGiven()
    {
        var xaml = Xaml(AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = AppCustomization.CompanyTitlePattern,
            IconPath = @"D:\brand\logo.ico",
        });

        StringAssert.Contains(xaml, "Title=\"Codis Ltd - Payroll\"");
        StringAssert.Contains(xaml, "Icon=\"Assets/app.ico\"");
    }
}

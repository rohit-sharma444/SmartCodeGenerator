using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.Wpf;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfDashboardLayoutTests
{
    /// <summary>
    /// The half of the partial class the XAML compiler generates: InitializeComponent and a field
    /// per x:Name. Without it the code-behind cannot compile on its own — which is a fact about
    /// WPF, not a defect in the emitted source.
    /// </summary>
    private const string XamlPartialStub = """
        namespace Payroll.Views;

        public partial class MainWindow
        {
            internal System.Windows.Controls.ListBox EntityCards = new();
            internal System.Windows.Controls.ContentControl PaneHost = new();
            internal System.Windows.Controls.Button HomeButton = new();

            private void InitializeComponent() { }
        }
        """;

    /// <summary>The view-model files DesktopViewModelFiles emits in the real app.</summary>
    private const string ViewModelStub = """
        using System.Collections.ObjectModel;

        namespace Payroll.ViewModels;

        public sealed class MainViewModel
        {
            public ObservableCollection<IEntityViewModel> Entities { get; } = new();
        }

        public interface IEntityViewModel
        {
            string Title { get; }
            System.Collections.IEnumerable Items { get; }
        }
        """;

    private static IReadOnlyList<GeneratedFile> Files(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return WpfLayouts.Resolve(AppLayout.Dashboard).ViewFiles(
            new LayoutContext("Payroll", "Payroll", settings,
                ThemePalette.Resolve(settings.Theme, settings.Accent)));
    }

    private static string Xaml(AppCustomization? customization = null)
        => Files(customization).Single(f => f.Name == "MainWindow.xaml").Source;

    private static string CodeBehind()
        => Files().Single(f => f.Name == "MainWindow.xaml.cs").Source;

    [TestMethod]
    public void ShowsOneCardPerEntityWithItsRowCount()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "<ListBox x:Name=\"EntityCards\"");
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Entities}\"");
        StringAssert.Contains(xaml, "<WrapPanel />");
        StringAssert.Contains(xaml, "Text=\"{Binding Title}\"");

        // The count comes free: Items returns an ObservableCollection<T>, which raises
        // PropertyChanged("Count") on every mutation, and WPF binds against the runtime type.
        StringAssert.Contains(xaml, "Text=\"{Binding Items.Count}\"");
    }

    [TestMethod]
    public void OpensOnTheCardsRatherThanAnEntity()
    {
        StringAssert.Contains(Xaml(), "SelectedIndex=\"-1\"");
    }

    [TestMethod]
    public void ShowsTheSelectedEntitysPaneThroughTheSharedTemplate()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "<ContentControl x:Name=\"PaneHost\"");
        StringAssert.Contains(xaml, "Content=\"{Binding SelectedItem, ElementName=EntityCards}\"");
        StringAssert.Contains(xaml, "ContentTemplate=\"{StaticResource EntityPane}\"");
    }

    [TestMethod]
    public void ComposesIntoWellFormedMarkup()
    {
        foreach (var xaml in new[] { Xaml(), Xaml(AppCustomization.Default with { Theme = AppTheme.Dark }) })
        {
            var root = System.Xml.Linq.XDocument.Parse(xaml).Root!;

            Assert.AreEqual("Window", root.Name.LocalName);
            Assert.IsNotNull(root.Elements().Single(e => e.Name.LocalName == "Window.Resources"));
            Assert.IsNotNull(root.Elements().Single(e => e.Name.LocalName == "DockPanel"));
        }
    }

    [TestMethod]
    public void CodeBehindNavigatesBothWaysAndStillHandlesColumns()
    {
        var source = CodeBehind();

        StringAssert.Contains(source, "private void OnCardSelected(");
        StringAssert.Contains(source, "private void OnHome(");
        StringAssert.Contains(source, "EntityCards.SelectedIndex = -1;");

        // Today's handler is still there: the pane's DataGrid raises AutoGeneratingColumn.
        StringAssert.Contains(source, "OnAutoGeneratingColumn");
        StringAssert.Contains(source, "DataGridComboBoxColumn");
    }

    [TestMethod]
    public void CodeBehindKeepsTheHandlersIndentedAsClassMembers()
    {
        // The handlers are spliced into the template's text, so nothing but this checks they land
        // at member depth: at column 0 they would still compile, and read as a formatting bug in
        // every generated app.
        StringAssert.Contains(CodeBehind(), "\n    private void OnCardSelected(");
        StringAssert.Contains(CodeBehind(), "\n    private void OnHome(");
    }

    [TestMethod]
    public void CodeBehindCompiles()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainWindow.xaml.cs", CodeBehind()),
            ("Views/MainWindow.g.cs", XamlPartialStub),
            ("ViewModels/MainViewModel.cs", ViewModelStub));
    }

    [TestMethod]
    public void CodeBehindLeaksNoGeneratorImplementationDetails()
    {
        var source = CodeBehind();

        Assert.DoesNotContain("MainWindowTemplate", source);
        Assert.DoesNotContain("WpfDashboardLayout", source);
    }
}

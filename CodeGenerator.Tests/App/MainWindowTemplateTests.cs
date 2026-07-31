using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class MainWindowTemplateTests
{
    [TestMethod]
    public void Xaml_DeclaresTheViewsNamespacedPartialClass()
    {
        var xaml = MainWindowTemplate.Xaml("Payroll");

        StringAssert.Contains(xaml, "x:Class=\"Payroll.Views.MainWindow\"");
    }

    [TestMethod]
    public void Xaml_BindsATabPerEntityOverAGrid()
    {
        var xaml = MainWindowTemplate.Xaml("Payroll");

        StringAssert.Contains(xaml, "<TabControl ItemsSource=\"{Binding Entities}\"");
        StringAssert.Contains(xaml, "DisplayMemberPath=\"Title\"");
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Items}\"");
        StringAssert.Contains(xaml, "SelectedItem=\"{Binding Selected, Mode=TwoWay}\"");
        StringAssert.Contains(xaml, "AutoGenerateColumns=\"True\"");
    }

    [TestMethod]
    public void Xaml_SelectsTheFirstTabOnStartup()
    {
        var xaml = MainWindowTemplate.Xaml("Payroll");

        // A Selector only syncs to the collection's current item when its ItemsSource is a
        // CollectionView. Bound to a plain ObservableCollection it selects nothing, so the
        // app opens on an empty content area until the user clicks a tab.
        StringAssert.Contains(xaml, "SelectedIndex=\"0\"");
    }

    [TestMethod]
    public void Xaml_ForbidsTheGridsOwnNewRow()
    {
        var xaml = MainWindowTemplate.Xaml("Payroll");

        // The placeholder row would construct T with Id = 0, bypassing NextId().
        StringAssert.Contains(xaml, "CanUserAddRows=\"False\"");
    }

    [TestMethod]
    public void Xaml_ExposesTheThreeCommandsAndStatus()
    {
        var xaml = MainWindowTemplate.Xaml("Payroll");

        StringAssert.Contains(xaml, "Command=\"{Binding AddCommand}\"");
        StringAssert.Contains(xaml, "Command=\"{Binding DeleteCommand}\"");
        StringAssert.Contains(xaml, "Command=\"{Binding SaveCommand}\"");
        StringAssert.Contains(xaml, "Text=\"{Binding Status}\"");
    }

    [TestMethod]
    public void CodeBehind_SetsTheDataContextAndLocksTheIdColumn()
    {
        var source = MainWindowTemplate.CodeBehind("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Views");
        StringAssert.Contains(source, "using Payroll.ViewModels;");
        StringAssert.Contains(source, "DataContext = new MainViewModel();");
        StringAssert.Contains(source, "e.Column.IsReadOnly = true;");
    }

    [TestMethod]
    public void CodeBehind_BuildsComboColumnsForForeignKeys()
    {
        var source = MainWindowTemplate.CodeBehind("Payroll");

        StringAssert.Contains(source, "TryBuildRelationshipColumn");
        StringAssert.Contains(source, "DataGridComboBoxColumn");
        StringAssert.Contains(source, "SelectedValuePath = \"Id\"");
        StringAssert.Contains(source, "SelectedValueBinding = new System.Windows.Data.Binding(e.PropertyName)");

        // The combo branch must run and return before the nav-property cancel, or every
        // combo column would be hidden behind e.Cancel = true.
        Assert.IsLessThan(
            source.IndexOf("e.Cancel = true", StringComparison.Ordinal),
            source.IndexOf("TryBuildRelationshipColumn(sender, e)", StringComparison.Ordinal),
            "the FK combo branch must run and return before the nav-property cancel");
    }

    [TestMethod]
    public void CodeBehind_LeaksNoGeneratorImplementationDetails()
    {
        var source = MainWindowTemplate.CodeBehind("Payroll");

        // The cross-reference note about the deliberate duplication with WinFormsMainFormTemplate
        // is for a generator maintainer, not for a user reading their generated app's code-behind.
        Assert.DoesNotContain(
            "WinFormsMainFormTemplate", source,
            "generator implementation details must not leak into the generated app's source");
        Assert.DoesNotContain(
            "MainWindowTemplate", source,
            "generator implementation details must not leak into the generated app's source");
    }
}

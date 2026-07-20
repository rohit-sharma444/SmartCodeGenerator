using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ViewModelTemplatesTests
{
    [TestMethod]
    public void AllTemplates_LiveInTheAppsViewModelsNamespace()
    {
        foreach (var source in new[]
        {
            ViewModelTemplates.ObservableObject("Payroll"),
            ViewModelTemplates.RelayCommand("Payroll"),
            ViewModelTemplates.EntityViewModelInterface("Payroll"),
            ViewModelTemplates.EntityViewModel("Payroll"),
        })
        {
            StringAssert.Contains(source, "namespace Payroll.ViewModels");
        }
    }

    [TestMethod]
    public void Interface_DeclaresEverythingTheXamlBindsTo()
    {
        var source = ViewModelTemplates.EntityViewModelInterface("Payroll");

        StringAssert.Contains(source, "interface IEntityViewModel");
        StringAssert.Contains(source, "string Title { get; }");
        StringAssert.Contains(source, "IEnumerable Items { get; }");
        StringAssert.Contains(source, "object? Selected { get; set; }");
        StringAssert.Contains(source, "ICommand AddCommand { get; }");
        StringAssert.Contains(source, "ICommand DeleteCommand { get; }");
        StringAssert.Contains(source, "ICommand SaveCommand { get; }");
        StringAssert.Contains(source, "string Status { get; }");
    }

    [TestMethod]
    public void EntityViewModel_ExposesRecordsAsTypedCollection()
    {
        var source = ViewModelTemplates.EntityViewModel("Payroll");

        // DataGrid reads its columns from IEnumerable<T>'s type argument, so the
        // backing collection must stay generic or empty tabs lose their columns.
        StringAssert.Contains(source, "public ObservableCollection<T> Records { get; }");
        StringAssert.Contains(source, "public IEnumerable Items => Records;");
    }

    [TestMethod]
    public void EntityViewModel_UsesTheStoreForIdsAndPersistence()
    {
        var source = ViewModelTemplates.EntityViewModel("Payroll");

        StringAssert.Contains(source, "using Payroll.Data;");
        StringAssert.Contains(source, "_store.NewItem(Records)");
        StringAssert.Contains(source, "_store.SaveAll(Records.ToList())");
    }

    [TestMethod]
    public void EntityViewModel_DisablesDeleteWithoutASelection()
    {
        var source = ViewModelTemplates.EntityViewModel("Payroll");

        StringAssert.Contains(source, "new RelayCommand(Delete, () => _selected is T)");
    }

    [TestMethod]
    public void RelayCommand_RequeriesOnUiActivity()
    {
        var source = ViewModelTemplates.RelayCommand("Payroll");

        // Without CommandManager the Delete button never re-enables on selection change.
        StringAssert.Contains(source, "CommandManager.RequerySuggested");
    }

    [TestMethod]
    public void EntityViewModel_ValidatesBeforeSaving()
    {
        var src = ViewModelTemplates.EntityViewModel("Payroll");

        StringAssert.Contains(src, "using System.ComponentModel.DataAnnotations;");
        StringAssert.Contains(src, "Validator.TryValidateObject");
        StringAssert.Contains(src, "Cannot save:");
    }
}

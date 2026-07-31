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

        // This path writes through _store, not Repository, so repository-level enforcement never
        // reaches it: it has to resolve and run the generated validator itself.
        StringAssert.Contains(src, "using Payroll.Validation;");
        StringAssert.Contains(src, "ValidatorFactory.Create<T>()");
        StringAssert.Contains(src, "Cannot save:");
    }

    [TestMethod]
    public void EntityViewModel_constrains_T_to_a_reference_type()
    {
        // ValidatorFactory.Create<T>() is declared `where T : class, new()`; without the class
        // constraint here the generated WPF app does not compile (CS0452).
        StringAssert.Contains(
            ViewModelTemplates.EntityViewModel("Payroll"),
            "EntityViewModel<T> : ObservableObject, IEntityViewModel where T : class, new()");
    }

    [TestMethod]
    public void PortableRelayCommand_DoesNotDependOnWpf()
    {
        var source = ViewModelTemplates.PortableRelayCommand("Payroll");

        // CommandManager lives in PresentationCore. A WinForms project has no reference to it,
        // so its presence here is a compile error in the generated app, not a style question.
        Assert.IsFalse(
            source.Contains("CommandManager", StringComparison.Ordinal),
            "the portable RelayCommand must not reference CommandManager");
        Assert.IsFalse(
            source.Contains("using System.Windows.Input;\nusing System.Windows.Controls;",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void PortableRelayCommand_IsTheSameClassUnderTheSameNamespace()
    {
        var source = ViewModelTemplates.PortableRelayCommand("Payroll");

        // EntityViewModel is shared between both desktop targets and says `new RelayCommand(...)`,
        // so the type it binds to must have the same name and namespace in either variant.
        StringAssert.Contains(source, "namespace Payroll.ViewModels;");
        StringAssert.Contains(source, "public sealed class RelayCommand : ICommand");
        StringAssert.Contains(source, "public bool CanExecute(object? parameter)");
        StringAssert.Contains(source, "public void Execute(object? parameter)");
    }

    [TestMethod]
    public void PortableRelayCommand_LetsTheCallerRaiseCanExecuteChanged()
    {
        var source = ViewModelTemplates.PortableRelayCommand("Payroll");

        // With no CommandManager there is no global requery, so the event has to be raisable —
        // otherwise it is a dead member and nothing can ever re-evaluate CanExecute.
        StringAssert.Contains(source, "public void RaiseCanExecuteChanged()");
        StringAssert.Contains(source, "CanExecuteChanged?.Invoke(this, EventArgs.Empty)");
    }
}

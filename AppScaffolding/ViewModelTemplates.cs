namespace AppScaffolding;

/// <summary>
/// The MVVM plumbing every scaffolded app gets, identical apart from its namespace.
/// Hand-rolled so a generated app needs no MVVM package.
/// </summary>
public static class ViewModelTemplates
{
    /// <summary>Source of <c>ViewModels/ObservableObject.cs</c>.</summary>
    public static string ObservableObject(string appName) => $$"""
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace {{appName}}.ViewModels;

/// <summary>Minimal INotifyPropertyChanged base.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<TValue>(
        ref TValue field, TValue value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<TValue>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
""";

    /// <summary>Source of <c>ViewModels/RelayCommand.cs</c>.</summary>
    public static string RelayCommand(string appName) => $$"""
using System;
using System.Windows.Input;

namespace {{appName}}.ViewModels;

/// <summary>An ICommand over a delegate pair.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    // Routing through CommandManager is what re-evaluates CanExecute when the
    // grid selection changes, so Delete enables and disables on its own.
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}
""";

    /// <summary>
    /// Source of <c>ViewModels/RelayCommand.cs</c> for a target with no WPF reference.
    /// </summary>
    /// <remarks>
    /// Same class, same namespace, same members as <see cref="RelayCommand"/> — deliberately, so
    /// the shared <c>EntityViewModel</c> compiles against either. The difference is
    /// <c>CanExecuteChanged</c>: the WPF variant routes it through
    /// <c>CommandManager.RequerySuggested</c>, which lives in PresentationCore and does not exist
    /// in a WinForms project. Here the generated view re-evaluates <c>CanExecute</c> directly when
    /// the grid selection changes (see <c>WinFormsMainFormTemplate</c>), so
    /// <c>RaiseCanExecuteChanged</c> is never called by generated code; it exists as a seam for
    /// hand-written code that wants to signal a change itself.
    /// </remarks>
    public static string PortableRelayCommand(string appName) => $$"""
using System;
using System.Windows.Input;

namespace {{appName}}.ViewModels;

/// <summary>An ICommand over a delegate pair.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    // The generated view re-evaluates CanExecute directly instead of raising this (see
    // MainForm's grid.SelectionChanged handler). This method is a seam for hand-written code
    // that wants to signal a change itself.
    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}
""";

    /// <summary>Source of <c>ViewModels/IEntityViewModel.cs</c>.</summary>
    public static string EntityViewModelInterface(string appName) => $$"""
using System.Collections;
using System.Windows.Input;

namespace {{appName}}.ViewModels;

/// <summary>The non-generic face of one entity's tab. MainWindow.xaml binds to this.</summary>
public interface IEntityViewModel
{
    string Title { get; }
    IEnumerable Items { get; }
    object? Selected { get; set; }
    ICommand AddCommand { get; }
    ICommand DeleteCommand { get; }
    ICommand SaveCommand { get; }
    string Status { get; }
}
""";

    /// <summary>Source of <c>ViewModels/EntityViewModel.cs</c>.</summary>
    public static string EntityViewModel(string appName) => EntityViewModel(appName, validation: true);

    /// <summary>
    /// Source of <c>ViewModels/EntityViewModel.cs</c>. With <paramref name="validation"/> false the
    /// Validation folder is not generated, so this file's own validator resolution has to go — the
    /// second of the two places generated code hard-depends on that folder.
    /// </summary>
    public static string EntityViewModel(string appName, bool validation)
    {
        // Raw string literals rather than "\n" concatenation: these are lines lifted out of the
        // template below, and a raw literal carries the same line endings that template does. See
        // RepositoryTemplate.Base, which lifts its validation out the same way.
        var validationUsing = validation
            ? $"""
            using {appName}.Validation;

            """
            : string.Empty;

        var constraintReason = validation
            ? """
              /// <remarks>T is constrained to a reference type because this view-model resolves the entity's
              /// generated validator, and ValidatorFactory.Create&lt;T&gt;() is declared `where T : class, new()`.
              /// Every entity the app generates is a class, so the constraint costs nothing.</remarks>
              """
            : """
              /// <remarks>T is constrained to a reference type because a record is held in an
              /// ObservableCollection and compared against null on selection. Every entity the app
              /// generates is a class, so the constraint costs nothing.</remarks>
              """;

        // The whole loop goes, not just the Validate call: with nothing to reject, a foreach that
        // saved nothing and skipped nothing would be dead code in every generated app.
        var validateRecords = validation
            ? """
                    // This path writes through _store, not Repository, so it has to run the generated
                    // validator itself -- repository-level enforcement never sees these records.
                    var validator = ValidatorFactory.Create<T>();
                    foreach (var record in Records)
                    {
                        var errors = validator.Validate(record!);
                        if (errors.Count > 0)
                        {
                            Status = $"Cannot save: {errors[0]}";
                            return;
                        }
                    }


            """
            : string.Empty;

        return $$"""
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Input;
using {{appName}}.Data;
{{validationUsing}}
namespace {{appName}}.ViewModels;

/// <summary>One entity's records, edited in memory and persisted on Save.</summary>
{{constraintReason}}
public sealed class EntityViewModel<T> : ObservableObject, IEntityViewModel where T : class, new()
{
    private readonly JsonStore<T> _store = new();
    private object? _selected;
    private string _status = string.Empty;

    public EntityViewModel(string title)
    {
        Title = title;
        Records = new ObservableCollection<T>(_store.GetAll());
        AddCommand = new RelayCommand(Add);
        DeleteCommand = new RelayCommand(Delete, () => _selected is T);
        SaveCommand = new RelayCommand(Save);
    }

    public string Title { get; }

    /// <summary>
    /// Typed on purpose: DataGrid derives its columns from IEnumerable&lt;T&gt;'s type
    /// argument, so the columns appear even while the collection is empty.
    /// </summary>
    public ObservableCollection<T> Records { get; }

    public IEnumerable Items => Records;

    public object? Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public ICommand AddCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand SaveCommand { get; }

    private void Add()
    {
        Records.Add(_store.NewItem(Records));
        Status = $"Added a {Title}. Not saved yet.";
    }

    private void Delete()
    {
        if (_selected is not T item)
            return;

        Records.Remove(item);
        Selected = null;
        Status = $"Removed a {Title}. Not saved yet.";
    }

    private void Save()
    {
{{validateRecords}}        Status = _store.SaveAll(Records.ToList())
            ? $"Saved {Records.Count} {Title} record(s)."
            : $"Could not write {typeof(T).Name}.json.";
    }
}
""";
    }
}

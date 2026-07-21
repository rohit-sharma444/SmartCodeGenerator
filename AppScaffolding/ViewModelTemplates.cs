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
    public static string EntityViewModel(string appName) => $$"""
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows.Input;
using {{appName}}.Data;

namespace {{appName}}.ViewModels;

/// <summary>One entity's records, edited in memory and persisted on Save.</summary>
public sealed class EntityViewModel<T> : ObservableObject, IEntityViewModel where T : new()
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
        foreach (var record in Records)
        {
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(
                    record!, new ValidationContext(record!), errors, validateAllProperties: true))
            {
                Status = $"Cannot save: {errors[0].ErrorMessage}";
                return;
            }
        }

        Status = _store.SaveAll(Records.ToList())
            ? $"Saved {Records.Count} {Title} record(s)."
            : $"Could not write {typeof(T).Name}.json.";
    }
}
""";
}

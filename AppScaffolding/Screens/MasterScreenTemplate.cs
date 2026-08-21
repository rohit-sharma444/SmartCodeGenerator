namespace AppScaffolding.Screens;

/// <summary>Emits <c>ViewModels/MasterScreenViewModel.cs</c>: a read-only list that opens an editor.</summary>
/// <remarks>
/// The list finds a record; the editor changes it. That is the whole application's one interaction,
/// and it is why this screen has no edit path at all — <c>AddCommand</c> and <c>SaveCommand</c>
/// exist only to satisfy <c>IEntityViewModel</c>, which every layout binds to.
/// <para>
/// Not built on <c>EntityViewModel&lt;T&gt;</c>: that one edits its rows in place, which is the
/// behaviour this screen exists to replace.
/// </para>
/// </remarks>
public static class MasterScreenTemplate
{
    /// <summary>Source of <c>ViewModels/MasterScreenViewModel.cs</c>.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using {{rootNamespace}}.Auth;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Screens;

namespace {{rootNamespace}}.ViewModels;

/// <summary>Reference data: a read-only list, and an editor for whichever row you open.</summary>
public class MasterScreenViewModel<T> : ObservableObject, IEntityViewModel where T : class, new()
{
    private readonly IStore<T> _store = StoreFactory.Create<T>();

    private object? _selected;
    private string _search = string.Empty;
    private string _status = string.Empty;

    public MasterScreenViewModel(string title)
    {
        Title = title;
        Records = new ObservableCollection<T>(_store.GetAll());

        NewCommand = new RelayCommand(New);
        OpenCommand = new RelayCommand(Open, () => _selected is T);
        DeleteCommand = new RelayCommand(Delete, () => _selected is T);
        SaveCommand = new RelayCommand(Reload);
    }

    /// <summary>Raised when a record should be opened. The shell shows it; this screen does not
    /// know what a window is.</summary>
    public event EventHandler<RecordEditorViewModel<T>>? EditorRequested;

    public string Title { get; }

    public virtual EntityRole Role => EntityRole.Master;

    public int Count => Records.Count;

    /// <summary>Everything the store holds. <see cref="Items"/> is what the screen shows.</summary>
    protected ObservableCollection<T> Records { get; }

    /// <summary>The rows matching <see cref="Search"/>, or all of them when it is empty.</summary>
    public virtual IEnumerable Items => Matching(Records);

    /// <summary>Narrows the list. Never touches the store.</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (SetField(ref _search, value))
                OnPropertyChanged(nameof(Items));
        }
    }

    public object? Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    public string Status
    {
        get => _status;
        protected set => SetField(ref _status, value);
    }

    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand DeleteCommand { get; }

    /// <summary>Reloads from the store. The list itself never edits, so there is nothing of its
    /// own to save — the name exists because IEntityViewModel names it.</summary>
    public ICommand SaveCommand { get; }

    /// <summary>The same as <see cref="NewCommand"/>, under the name the interface uses.</summary>
    public ICommand AddCommand => NewCommand;

    /// <summary>Re-reads the store. The shell calls this when an editor closes.</summary>
    public void Reload()
    {
        Records.Clear();

        foreach (var record in _store.GetAll())
            Records.Add(record);

        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>Rows whose text matches the search, matched across every string property so a user
    /// does not have to know which column holds what.</summary>
    protected IEnumerable<T> Matching(IEnumerable<T> source)
    {
        if (_search.Length == 0)
            return source.ToList();

        var strings = typeof(T).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.CanRead)
            .ToList();

        return source
            .Where(record => strings.Any(p =>
                (p.GetValue(record) as string)?.Contains(_search, StringComparison.OrdinalIgnoreCase) == true))
            .ToList();
    }

    /// <summary>
    /// The line sets a record's editor gets.
    /// </summary>
    /// <remarks>
    /// Empty here: reference data has no lines. A transaction screen overrides this, which is why
    /// the editor is built through it rather than inline — both paths have to agree.
    /// </remarks>
    protected virtual IReadOnlyList<ILineSet> LineSets() => [];

    private void New()
        => EditorRequested?.Invoke(
            this, new RecordEditorViewModel<T>(null, isNew: true, LineSets()));

    private void Open()
    {
        if (Selected is T record)
        {
            EditorRequested?.Invoke(
                this, new RecordEditorViewModel<T>(record, isNew: false, LineSets()));
        }
    }

    /// <summary>Deletes the selected row. Administrators only, which is one of the role's two
    /// effects.</summary>
    private void Delete()
    {
        if (Selected is not T record)
        {
            Status = "Select a record first.";
            return;
        }

        if (!Session.IsAdmin)
        {
            Status = "Only an administrator can delete a record.";
            return;
        }

        var all = _store.GetAll();
        var index = all.FindIndex(r => IdOf(r) == IdOf(record));

        if (index >= 0)
            all.RemoveAt(index);

        Status = _store.SaveAll(all) ? "Deleted." : "Could not delete.";

        Selected = null;
        Reload();
    }

    private static int IdOf(T record)
        => (int)(typeof(T).GetProperty("Id")?.GetValue(record) ?? 0);
}
""";
}

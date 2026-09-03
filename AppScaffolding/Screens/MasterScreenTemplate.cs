namespace AppScaffolding.Screens;

/// <summary>Emits <c>ViewModels/MasterScreenViewModel.cs</c>: an entry form with its list under it.</summary>
/// <remarks>
/// The form and the list are one surface. Selecting a row loads it into the form above, and
/// nothing navigates — which is why this screen holds a <c>RecordEditorViewModel&lt;T&gt;</c> of
/// its own rather than asking the shell for one.
/// <para>
/// A transaction screen derives from this one and turns that off through <c>EditsInline</c>: its
/// lines need a screen of their own, so its list stays a list and opening a row still asks the
/// shell for an editor. Both behaviours live here, behind one flag, so the two screens cannot
/// drift into disagreeing about which half of it they took.
/// </para>
/// <para>
/// Not built on <c>EntityViewModel&lt;T&gt;</c>: that one edits its rows in place, in the grid,
/// which is the behaviour these screens exist to replace.
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

/// <summary>Reference data: an entry form, and the list of what has been entered under it.</summary>
public class MasterScreenViewModel<T> : ObservableObject, IEntityViewModel, IDirtyScreen where T : class, new()
{
    private readonly IStore<T> _store = StoreFactory.Create<T>();

    private RecordEditorViewModel<T>? _editor;
    private object? _selected;
    private string _search = string.Empty;
    private string _status = string.Empty;

    public MasterScreenViewModel(string title)
    {
        Title = title;
        Records = new ObservableCollection<T>(_store.GetAll());

        NewCommand = new RelayCommand(New);
        EditCommand = new RelayCommand(Edit, () => _selected is T);
        OpenCommand = new RelayCommand(Edit, () => _selected is T);
        ClearCommand = new RelayCommand(Clear);
        CancelCommand = new RelayCommand(Cancel);
        DeleteCommand = new RelayCommand(Delete, () => _selected is T);
        SaveCommand = new RelayCommand(Save);
    }

    /// <summary>Raised when a record should be opened on a screen of its own. Only a screen that
    /// does not edit inline raises it; the shell shows what it asks for, because this screen does
    /// not know what a window is.</summary>
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
        set
        {
            if (!SetField(ref _selected, value))
                return;

            // Selecting a row IS opening it: the form and the list are one screen. Where they are
            // not -- a transaction list -- selecting stays selecting, and the record is opened
            // only when something asks for it.
            if (EditsInline && value is T record)
                Editor = NewEditor(record, isNew: false);
        }
    }

    public string Status
    {
        get => _status;
        protected set => SetField(ref _status, value);
    }

    /// <summary>
    /// The form at the top of the screen. Never null: the form is usable the moment the screen
    /// opens, with nothing selected. Unused by a screen that does not edit inline, whose records
    /// are opened in an editor of the shell's.
    /// </summary>
    /// <remarks>
    /// Built on first use rather than in the constructor, because <see cref="NewEditor"/> calls
    /// the virtual <see cref="LineSets"/> — which a derived screen cannot answer until its own
    /// constructor has run, and the base constructor runs first.
    /// </remarks>
    public RecordEditorViewModel<T> Editor
    {
        get => _editor ??= NewEditor(null, isNew: true);
        private set => SetField(ref _editor, value);
    }

    /// <inheritdoc />
    /// <remarks>A screen with no form on it has nothing unsaved, and asking would build an editor
    /// it never shows.</remarks>
    public bool IsDirty => EditsInline && Editor.IsDirty;

    /// <summary>Whether this record has long-text fields, and so an Additional Information tab.
    /// A view binds it so a model with none does not show an empty tab — the same rule that drops
    /// an empty panel.</summary>
    public bool HasAdditional => FieldGroups.Additional(typeof(T)).Count > 0;

    public ICommand NewCommand { get; }

    /// <summary>Loads the selected row into the form.</summary>
    public ICommand EditCommand { get; }

    /// <summary>The same as <see cref="EditCommand"/> — both mean "open the selected record" —
    /// under the name the list views bind: a grid's double-click, and a transaction list's Open
    /// button, which is the only way to reach a transaction's entry screen.</summary>
    public ICommand OpenCommand { get; }

    /// <summary>Blanks the form and keeps the selection, so the list still shows where you
    /// were.</summary>
    public ICommand ClearCommand { get; }

    /// <summary>Blanks the form and drops the selection: abandon what is on screen.</summary>
    public ICommand CancelCommand { get; }

    public ICommand DeleteCommand { get; }

    /// <summary>Saves the form, then re-reads the list under it. Save on this screen means the
    /// RECORD: the list has nothing of its own to write.</summary>
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
    /// Whether selecting a row loads it into a form on THIS screen.
    /// </summary>
    /// <remarks>
    /// True for a master, whose form and list are one surface. False for a transaction, whose list
    /// is a list and nothing else: a transaction carries lines, which need the room of a screen of
    /// their own, so it asks the shell for an editor instead of holding one.
    /// </remarks>
    protected virtual bool EditsInline => true;

    /// <summary>
    /// The line sets a record's editor gets.
    /// </summary>
    /// <remarks>
    /// Empty here: reference data has no lines. A transaction screen overrides this, which is why
    /// the editor is built through it rather than inline — both paths have to agree.
    /// </remarks>
    protected virtual IReadOnlyList<ILineSet> LineSets() => [];

    /// <summary>A FRESH editor, never a mutated one: the draft is a clone, and reusing the
    /// instance would leave the previous record's snapshot in place and make IsDirty lie.</summary>
    private RecordEditorViewModel<T> NewEditor(T? record, bool isNew)
        => new(record, isNew, LineSets());

    /// <summary>Starts a new record: a blank form here, or a blank editor screen where there is no
    /// form.</summary>
    private void New()
    {
        if (EditsInline)
            Clear();
        else
            EditorRequested?.Invoke(this, NewEditor(null, isNew: true));
    }

    /// <summary>Opens the selected record: in the form here, or on a screen of its own.</summary>
    private void Edit()
    {
        if (Selected is not T record)
            return;

        if (EditsInline)
            Editor = NewEditor(record, isNew: false);
        else
            EditorRequested?.Invoke(this, NewEditor(record, isNew: false));
    }

    /// <summary>Blanks the form. Separate from <see cref="New"/>, which on a screen with no form
    /// means "open a new one": clearing a form there has nothing to clear, and must not
    /// navigate.</summary>
    private void Clear()
    {
        if (EditsInline)
            Editor = NewEditor(null, isNew: true);
    }

    private void Cancel()
    {
        Selected = null;
        Clear();
    }

    /// <summary>
    /// Saves the form and re-reads the list under it.
    /// </summary>
    /// <remarks>
    /// Save means the record now, not the list: the list has nothing of its own to write, and the
    /// form is what the user typed into. A screen with no form keeps the older meaning — re-read
    /// the store — because saving there would write a blank draft nobody ever saw.
    /// </remarks>
    private void Save()
    {
        if (!EditsInline)
        {
            Reload();
            return;
        }

        var editor = Editor;
        var written = false;

        // Closed is raised on a save that went through, and on nothing else here: Cancel is a
        // different command. It is the only honest signal -- a validator can refuse the record,
        // and a rejected save must leave the form exactly as the user typed it.
        void OnWritten(object? sender, EventArgs e) => written = true;

        editor.Closed += OnWritten;

        try
        {
            if (editor.SaveCommand.CanExecute(null))
                editor.SaveCommand.Execute(null);
        }
        finally
        {
            editor.Closed -= OnWritten;
        }

        Status = editor.Status;
        Reload();

        // The store took the draft OBJECT, not a copy of it, so it is now the very row the
        // reloaded list is showing. Left in the form, typing on would change that stored row with
        // nothing saved -- and a second Save would enter the record all over again, because the
        // editor still believes it is new. A fresh editor over what was just saved is a copy
        // again, which is the rule the editor exists to keep.
        if (written)
            Editor = NewEditor(editor.Draft, isNew: false);
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

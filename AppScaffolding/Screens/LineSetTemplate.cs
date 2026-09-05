namespace AppScaffolding.Screens;

/// <summary>
/// Emits <c>Screens/LineSet.cs</c>: the lines of one transaction, edited inside its editor.
/// </summary>
/// <remarks>
/// The third way a transaction screen differs from a master screen, after the filter bar and the
/// column totals. A line has no screen of its own — this is where it is edited.
/// <para>
/// One class for all three front ends, and deliberately free of anything a window needs: no
/// <c>ObservableObject</c>, no <c>RelayCommand</c>, nothing from <c>System.Windows</c>. WPF binds
/// <c>Items</c> and calls <c>Add</c>/<c>Remove</c> from click handlers, Windows Forms binds the same
/// collection to a grid, and the web renders it as a table. A <c>BindingList&lt;T&gt;</c> rather
/// than an <c>ObservableCollection&lt;T&gt;</c> because it is the one collection BOTH desktop
/// stacks refresh from: WPF wraps <c>IBindingList</c> in a collection view, and a WinForms
/// <c>DataGridView</c> needs <c>IBindingList</c> to notice a row being added.
/// </para>
/// <para>
/// The lines are CLONED on load and written back as a set on save, for the reason the record itself
/// is cloned: Cancel has to genuinely cancel, including line edits.
/// </para>
/// </remarks>
public static class LineSetTemplate
{
    /// <summary>Source of <c>Screens/LineSet.cs</c>.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using {{rootNamespace}}.Data;

namespace {{rootNamespace}}.Screens;

/// <summary>
/// What an editor can ask a set of lines without knowing what kind of line it holds.
/// </summary>
/// <remarks>
/// The editor is generic over the RECORD, so it cannot name the line type: every member here is
/// reachable without it. <see cref="LineType"/> is how a view builds columns for something it
/// cannot name either.
/// </remarks>
public interface ILineSet
{
    /// <summary>The heading the editor shows above the grid.</summary>
    string Title { get; }

    /// <summary>The property on a line that points at its parent. Assigned on save, so it is not
    /// a field anyone types into.</summary>
    string ForeignKey { get; }

    /// <summary>The line type, for a view that has to generate columns for it.</summary>
    Type LineType { get; }

    /// <summary>The lines on screen. Editable in place: this IS the grid's source.</summary>
    IEnumerable Items { get; }

    /// <summary>The line a view has selected, for Remove.</summary>
    object? Selected { get; set; }

    /// <summary>Whether any line has been added, removed or changed since the editor opened.</summary>
    bool IsDirty { get; }

    /// <summary>Reads the lines belonging to one parent. Called when the editor opens.</summary>
    void Load(int parentId);

    /// <summary>Adds an empty line.</summary>
    void Add();

    /// <summary>Removes one line. Nothing reaches the store until the editor saves.</summary>
    void Remove(object? line);

    /// <summary>Removes every line. Nothing reaches the store until the editor saves, so this is
    /// undone by Cancel like any other line edit.</summary>
    void Clear();

    /// <summary>
    /// Announces that a value was typed into one of the lines.
    /// </summary>
    /// <remarks>
    /// A line is a plain generated model with no change notification, so a grid writing a typed
    /// value into it reaches the object without the set -- or anything derived from the set, such
    /// as the editor's totals band -- hearing about it. Add, Remove and Clear announce themselves;
    /// this is how a view says what only it saw.
    /// <para>
    /// EVERY front end owes this call. Whatever a view uses to know an edit was committed -- WPF's
    /// CellEditEnding, a WinForms CellEndEdit, a web field losing focus -- must end in this method,
    /// or that front end shows a total that ignores what was just typed into the grid under it.
    /// </para>
    /// </remarks>
    void LineEdited();

    /// <summary>Writes the lines back, stamped with the parent's id. Called AFTER the parent is
    /// saved, because a new parent has no id before that.</summary>
    bool Save(int parentId);
}

/// <summary>One transaction's lines, of one type.</summary>
public sealed class LineSet<TLine> : ILineSet where TLine : class, new()
{
    private readonly IStore<TLine> _store = StoreFactory.Create<TLine>();
    private readonly BindingList<TLine> _lines = new();

    private string _openedAs = "[]";

    /// <param name="title">The heading, which is the line class's name.</param>
    /// <param name="foreignKey">The property pointing at the parent — <c>OrderId</c> for an
    /// OrderLine. Always <c>&lt;Parent&gt;Id</c>: the classifier only calls a class a line when it
    /// has exactly that property and a navigation property beside it.</param>
    public LineSet(string title, string foreignKey)
    {
        Title = title;
        ForeignKey = foreignKey;
    }

    public string Title { get; }

    public string ForeignKey { get; }

    public Type LineType => typeof(TLine);

    public IEnumerable Items => _lines;

    public object? Selected { get; set; }

    public bool IsDirty => !string.Equals(Snapshot(), _openedAs, StringComparison.Ordinal);

    public void Load(int parentId)
    {
        _lines.Clear();

        // Cloned, like the record itself: an edit to a line must not reach the store until the
        // editor saves, or Cancel would leave half of the change behind.
        foreach (var line in Belonging(_store.GetAll(), parentId))
            _lines.Add(Clone(line));

        Selected = null;
        _openedAs = Snapshot();
    }

    public void Add()
    {
        var line = new TLine();

        _lines.Add(line);
        Selected = line;
    }

    public void Remove(object? line)
    {
        if (line is not TLine typed)
            return;

        _lines.Remove(typed);

        if (ReferenceEquals(Selected, typed))
            Selected = null;
    }

    public void Clear()
    {
        _lines.Clear();
        Selected = null;
    }

    /// <summary>
    /// Raises the list's own change event, which is the channel every consumer already listens to.
    /// </summary>
    /// <remarks>
    /// The list's own event rather than a second one of this class's own: it is what Add, Remove
    /// and Clear already reach through the BindingList, so an editor watching the lines needs no
    /// knowledge of where a change came from -- and a bound grid re-reads the row for free.
    /// <para>
    /// ResetItem for the edited row, NOT ResetBindings for the whole list. A reset tells a grid
    /// that everything it is showing may have changed, and a DataGridView answers that by
    /// rebuilding its rows and putting the caret back at the top: measured on a running app,
    /// typing into row 3 and pressing Tab left the cursor on ROW 1, so entering a document's
    /// lines threw the user back to the first line after every single cell. ResetItem says only
    /// what is true -- one row changed -- and the caret stays where the user put it.
    /// </para>
    /// <para>
    /// The row is the selected one because a cell cannot be edited without being current, and
    /// both grids set Selected as the selection moves. If it is somehow not in the list, this
    /// falls back to the whole-list reset: a moved caret is worth less than a stale total.
    /// </para>
    /// </remarks>
    public void LineEdited()
    {
        var index = Selected is TLine typed ? _lines.IndexOf(typed) : -1;

        if (index >= 0)
            _lines.ResetItem(index);
        else
            _lines.ResetBindings();
    }

    /// <summary>
    /// Replaces this parent's lines with what is on screen.
    /// </summary>
    /// <remarks>
    /// A whole-set replacement rather than a per-line diff: a removed line has to disappear from
    /// the store, and matching up ids to work out which ones went is more code and more ways to be
    /// wrong than writing what the editor is showing.
    /// </remarks>
    public bool Save(int parentId)
    {
        var all = _store.GetAll();

        all.RemoveAll(line => IdOf(line, ForeignKey) == parentId);

        foreach (var line in _lines)
        {
            SetId(line, ForeignKey, parentId);

            // A line added in this editor has no id yet. Assigned one at a time, because the store
            // hands out the next id from what it has been given so far.
            if (IdOf(line, "Id") == 0)
                SetId(line, "Id", _store.NextId(all));

            all.Add(line);
        }

        if (!_store.SaveAll(all))
            return false;

        _openedAs = Snapshot();

        return true;
    }

    private IEnumerable<TLine> Belonging(IEnumerable<TLine> lines, int parentId)
        => lines.Where(line => IdOf(line, ForeignKey) == parentId);

    private string Snapshot() => JsonSerializer.Serialize(_lines);

    private static TLine Clone(TLine line)
        => JsonSerializer.Deserialize<TLine>(JsonSerializer.Serialize(line))!;

    private static int IdOf(TLine line, string property)
        => (int)(typeof(TLine).GetProperty(property)?.GetValue(line) ?? 0);

    private static void SetId(TLine line, string property, int value)
        => typeof(TLine).GetProperty(property)?.SetValue(line, value);
}
""";
}

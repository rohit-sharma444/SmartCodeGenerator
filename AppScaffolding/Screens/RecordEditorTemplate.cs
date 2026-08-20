namespace AppScaffolding.Screens;

/// <summary>Emits <c>ViewModels/RecordEditorViewModel.cs</c>: one record, edited as a copy.</summary>
/// <remarks>
/// The copy is the whole point. Editing the record in place would make Cancel a lie — the change
/// would already be in the list behind the editor — and it is what lets the shell ask "discard
/// your changes?" and mean it.
/// <para>
/// The clone is a <c>System.Text.Json</c> round-trip, already a dependency through JsonStore.
/// Navigation properties are <c>[JsonIgnore]</c>, so they drop out of the copy and are re-resolved
/// by RelationshipResolver on save.
/// </para>
/// </remarks>
public static class RecordEditorTemplate
{
    /// <summary>Source of <c>ViewModels/RecordEditorViewModel.cs</c>.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using {{rootNamespace}}.Data;

namespace {{rootNamespace}}.ViewModels;

/// <summary>
/// What the shell can ask an editor without knowing which entity it edits.
/// </summary>
/// <remarks>
/// The shell holds the current screen as an object, so it cannot test for
/// <c>RecordEditorViewModel&lt;T&gt;</c> — and <c>RecordEditorViewModel&lt;object&gt;</c> compiles
/// while being always false. This interface is how the dirty check is reachable at all.
/// </remarks>
public interface IRecordEditor
{
    /// <summary>Whether anything has been typed since the editor opened.</summary>
    bool IsDirty { get; }

    /// <summary>The heading the shell shows while this editor is on screen.</summary>
    string Title { get; }
}

/// <summary>One record, edited in a copy so Cancel can genuinely cancel.</summary>
public sealed class RecordEditorViewModel<T> : ObservableObject, IRecordEditor where T : class, new()
{
    private readonly IStore<T> _store = StoreFactory.Create<T>();
    private string _openedAs;
    private string _status = string.Empty;

    public RecordEditorViewModel(T? record, bool isNew)
    {
        IsNew = isNew;
        Draft = record is null ? new T() : Clone(record);

        _openedAs = Snapshot(Draft);

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>Raised when the editor is finished with, whichever way it ended.</summary>
    public event EventHandler? Closed;

    /// <summary>The copy on screen. Nothing outside this object holds a reference to it.</summary>
    public T Draft { get; }

    public bool IsNew { get; }

    public string Title => IsNew ? $"New {typeof(T).Name}" : typeof(T).Name;

    /// <summary>
    /// Whether anything has been typed since the editor opened.
    /// </summary>
    /// <remarks>
    /// Compared as a serialized snapshot rather than tracked per property: the draft is a plain
    /// generated model with no change notification, so there is nothing to subscribe to.
    /// </remarks>
    public bool IsDirty => !string.Equals(Snapshot(Draft), _openedAs, StringComparison.Ordinal);

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private void Save()
    {
        var all = _store.GetAll();

        if (IsNew)
        {
            SetId(Draft, _store.NextId(all));
            all.Add(Draft);
        }
        else
        {
            var index = all.FindIndex(r => IdOf(r) == IdOf(Draft));

            // Deleted from another screen while this editor was open. Saying so beats writing the
            // record back and silently resurrecting it.
            if (index < 0)
            {
                Status = "That record no longer exists.";
                return;
            }

            all[index] = Draft;
        }

        if (!_store.SaveAll(all))
        {
            Status = "Could not save.";
            return;
        }

        // The snapshot moves to what was just written, so the shell stops calling this dirty and
        // the discard prompt does not fire straight after a successful save.
        _openedAs = Snapshot(Draft);

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);

    private static T Clone(T record)
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(record))!;

    private static string Snapshot(T record) => JsonSerializer.Serialize(record);

    private static int IdOf(T record)
        => (int)(typeof(T).GetProperty("Id")?.GetValue(record) ?? 0);

    private static void SetId(T record, int id)
        => typeof(T).GetProperty("Id")?.SetValue(record, id);
}
""";
}

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
    /// <summary>
    /// Source of <c>ViewModels/RecordEditorViewModel.cs</c>.
    /// </summary>
    /// <param name="validation">
    /// Whether the app has generated validators. With none, the Validation folder this would
    /// resolve one from is not generated either, so naming it would not compile — the same bargain
    /// <c>ViewModelTemplates.EntityViewModel</c> and <c>RepositoryTemplate.Base</c> strike.
    /// </param>
    public static string Source(string rootNamespace, bool validation)
    {
        // Raw string literals rather than "\n" concatenation: these are lines lifted out of the
        // template below, and a raw literal carries the same line endings that template does.
        var validationUsing = validation
            ? $"""
            using {rootNamespace}.Validation;

            """
            : string.Empty;

        var validatorField = validation
            ? """
                private readonly IValidator<T> _validator = ValidatorFactory.Create<T>();

            """
            : string.Empty;

        // Two blank lines before the closing delimiter, so the fragment ends with the blank line
        // that separates the guard from the body.
        var validateCall = validation
            ? """
                    // This path writes through _store, not Repository, so it has to run the generated
                    // validator itself. Without this the business shell would accept records the
                    // layout it replaced rejected, from the same model in the same application.
                    var errors = _validator.Validate(Draft);
                    if (errors.Count > 0)
                    {
                        Status = $"Cannot save: {errors[0]}";
                        return;
                    }


            """
            : string.Empty;

        return $$"""
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Screens;
{{validationUsing}}
namespace {{rootNamespace}}.ViewModels;

/// <summary>
/// A screen that can be left with unsaved changes.
/// </summary>
/// <remarks>
/// Separate from IRecordEditor because a master screen holds its editor INLINE: it is dirty in
/// exactly the same way, and it is deliberately not an IRecordEditor — ScreenTemplateSelector
/// tests that interface before IEntityViewModel, so a master implementing it would draw as the
/// editor template on every screen in the application.
/// </remarks>
public interface IDirtyScreen
{
    /// <summary>Whether anything has been typed since the screen was opened or last saved.</summary>
    bool IsDirty { get; }
}

/// <summary>
/// What the shell can ask an editor without knowing which entity it edits.
/// </summary>
/// <remarks>
/// The shell holds the current screen as an object, so it cannot test for
/// <c>RecordEditorViewModel&lt;T&gt;</c> — and <c>RecordEditorViewModel&lt;object&gt;</c> compiles
/// while being always false. This interface is how the dirty check is reachable at all.
/// </remarks>
public interface IRecordEditor : IDirtyScreen
{
    /// <summary>The heading the shell shows while this editor is on screen.</summary>
    string Title { get; }

    /// <summary>The lines this record holds, or empty. A master has none; a transaction has one
    /// set per kind of line.</summary>
    IReadOnlyList<ILineSet> Lines { get; }

    /// <summary>One row per money column of the lines, summed across them.</summary>
    IReadOnlyList<LineTotal> Totals { get; }
}

/// <summary>One money column of a document's lines, summed over them.</summary>
public sealed record LineTotal(string Column, decimal Value, bool IsGrand);

/// <summary>One record, edited in a copy so Cancel can genuinely cancel.</summary>
public sealed class RecordEditorViewModel<T> : ObservableObject, IRecordEditor where T : class, new()
{
    private readonly IStore<T> _store = StoreFactory.Create<T>();
    private string _openedAs;
    private string _status = string.Empty;
{{validatorField}}
    /// <param name="lines">
    /// The line sets this record's editor shows, supplied by the screen that opened it. Null for a
    /// master, and for a transaction whose lines the classifier did not find.
    /// </param>
    public RecordEditorViewModel(T? record, bool isNew, IReadOnlyList<ILineSet>? lines = null)
    {
        IsNew = isNew;
        Draft = record is null ? new T() : Clone(record);
        Lines = lines ?? [];

        _openedAs = Snapshot(Draft);

        // A new record has no id yet, so it has no lines to read either -- they are written after
        // the parent is saved and has one.
        if (record is not null)
        {
            foreach (var set in Lines)
                set.Load(IdOf(Draft));
        }

        // Totals is computed, and a computed property announces nothing by itself: a band bound to
        // it would paint once and then go stale the moment a line was added, removed, edited or
        // cleared -- the most visible number on a document, silently wrong. The lines are the thing
        // that changes and this editor is the thing bound, so the set publishes the change and the
        // editor translates it into the one property that derives from it.
        //
        // Subscribed AFTER the load above, which would otherwise announce the totals while this
        // constructor is still running. IsDirty is deliberately not announced with it: it also
        // turns on the draft's own fields, which are a plain generated model with nothing to
        // subscribe to, so it is a question the shell asks rather than a value a view binds.
        // The invariant this depends on: every set an application of this shape creates is a
        // LineSet<T>, whose Items IS a BindingList<T> -- chosen because it is the one collection
        // both desktop grids refresh from, and relied on again here. ILineSet.Items is typed
        // IEnumerable, so the cast is how that fact is reached, not a doubt about it. A set that
        // somehow is not one keeps working; only its totals stop repainting.
        foreach (var set in Lines)
        {
            if (set.Items is IBindingList list)
                list.ListChanged += (_, _) => OnPropertyChanged(nameof(Totals));
        }

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>Raised when the editor is finished with, whichever way it ended.</summary>
    public event EventHandler? Closed;

    /// <summary>The copy on screen. Nothing outside this object holds a reference to it.</summary>
    public T Draft { get; }

    /// <inheritdoc />
    public IReadOnlyList<ILineSet> Lines { get; }

    /// <summary>Whether to show a lines area at all. A view binds this so a master's editor is not
    /// a form with an empty grid under it.</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary>Whether this record has long-text fields, and so an Additional Information tab.
    /// A view binds it so a model with none does not show an empty tab.</summary>
    public bool HasAdditional => FieldGroups.Additional(typeof(T)).Count > 0;

    /// <summary>
    /// One row per money column of the lines, summed across them.
    /// </summary>
    /// <remarks>
    /// Every decimal and double that is neither an Id nor a foreign key, rather than guessing
    /// which single column is the document's total — the rule TransactionScreenViewModel already
    /// applies to a list, for the reason written there: a generated application showing one
    /// confidently wrong number is worse than one showing several honest ones.
    /// <para>
    /// IsGrand only when a set has exactly one money column, which is the only case where naming
    /// a grand total is not a guess.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LineTotal> Totals
    {
        get
        {
            var totals = new List<LineTotal>();

            foreach (var set in Lines)
            {
                var columns = set.LineType.GetProperties()
                    .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(double))
                    .Where(p => !string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase)
                                && !p.Name.EndsWith("Id", StringComparison.Ordinal))
                    .ToList();

                foreach (var column in columns)
                {
                    var sum = set.Items.Cast<object>().Sum(line => ToDecimal(column.GetValue(line)));

                    totals.Add(new LineTotal(column.Name, sum, columns.Count == 1));
                }
            }

            return totals;
        }
    }

    private static decimal ToDecimal(object? value)
        => value switch
        {
            decimal d => d,
            double d => (decimal)d,
            _ => 0m,
        };

    public bool IsNew { get; }

    public string Title => IsNew ? $"New {typeof(T).Name}" : typeof(T).Name;

    /// <summary>
    /// Whether anything has been typed since the editor opened.
    /// </summary>
    /// <remarks>
    /// Compared as a serialized snapshot rather than tracked per property: the draft is a plain
    /// generated model with no change notification, so there is nothing to subscribe to.
    /// </remarks>
    public bool IsDirty
        => !string.Equals(Snapshot(Draft), _openedAs, StringComparison.Ordinal)
           || Lines.Any(set => set.IsDirty);

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private void Save()
    {
{{validateCall}}        var all = _store.GetAll();

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

        var write = _store.SaveAll(all);
        if (!write.Success)
        {
            Status = write.Refusal ?? "Could not save.";
            return;
        }

        // The lines go after the parent, because a new parent has no id until it is written --
        // that id is what stamps them. Reported rather than swallowed: the record IS saved, and
        // saying nothing would leave the editor looking as though nothing had happened.
        foreach (var set in Lines)
        {
            if (set.Save(IdOf(Draft)))
                continue;

            Status = $"Saved, but could not save the {set.Title} lines.";
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
}

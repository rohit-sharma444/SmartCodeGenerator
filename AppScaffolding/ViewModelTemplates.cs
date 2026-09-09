namespace AppScaffolding;

/// <summary>
/// The MVVM plumbing every scaffolded app gets, identical apart from its namespace.
/// Hand-rolled so a generated app needs no MVVM package.
/// </summary>
public static class ViewModelTemplates
{
    /// <summary>Source of <c>ViewModels/ObservableObject.cs</c>.</summary>
    public static string ObservableObject(string rootNamespace) => $$"""
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace {{rootNamespace}}.ViewModels;

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
    public static string RelayCommand(string rootNamespace) => $$"""
using System;
using System.Windows.Input;

namespace {{rootNamespace}}.ViewModels;

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
    public static string PortableRelayCommand(string rootNamespace) => $$"""
using System;
using System.Windows.Input;

namespace {{rootNamespace}}.ViewModels;

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
    public static string EntityViewModelInterface(string rootNamespace) => $$"""
using System.Collections;
using System.Windows.Input;
using {{rootNamespace}}.Screens;

namespace {{rootNamespace}}.ViewModels;

/// <summary>The non-generic face of one entity's screen. Every layout binds to this.</summary>
public interface IEntityViewModel
{
    string Title { get; }
    IEnumerable Items { get; }
    object? Selected { get; set; }
    ICommand AddCommand { get; }
    ICommand DeleteCommand { get; }
    ICommand SaveCommand { get; }
    string Status { get; }

    /// <summary>Which kind of screen this entity gets. The four pre-business layouts ignore
    /// it, which is why it could be added without changing any of them.</summary>
    EntityRole Role { get; }

    /// <summary>How many records it holds, for the dashboard's tiles.</summary>
    int Count { get; }
}
""";

    /// <summary>Source of <c>ViewModels/EntityViewModel.cs</c>.</summary>
    public static string EntityViewModel(string rootNamespace) => EntityViewModel(rootNamespace, validation: true);

    /// <summary>
    /// Source of <c>ViewModels/EntityViewModel.cs</c>. With <paramref name="validation"/> false the
    /// validator resolution has to go — the second of the two places generated code hard-depends
    /// on the pipeline. The Validation folder itself stays: permissions sit outside the flag, and
    /// this screen's Delete asks them.
    /// </summary>
    /// <param name="login">
    /// Whether the app has a desktop <c>Session</c> to read the acting user from. Emitted beside
    /// these view models under the same flag, so naming one without it would not compile.
    /// </param>
    public static string EntityViewModel(string rootNamespace, bool validation, bool login = false)
    {
        // Raw string literals rather than "\n" concatenation: these are lines lifted out of the
        // template below, and a raw literal carries the same line endings that template does. See
        // RepositoryTemplate.Base, which lifts its validation out the same way.
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
        //
        // Built with Update: this loop cannot tell an insert from an update any more than SaveAll
        // can -- a newly-added row and an edited one look identical by the time they reach here --
        // which is the same reason the coarse store guard is built with Update. ValidationContext
        // .For supplies no Stored version and no acting user, same as before the pipeline existed:
        // this path predates CurrentRole()/CurrentUserId() and stays that way here.
        var validateRecords = validation
            ? """
                    // This path writes through _store, not Repository, so it has to run the generated
                    // validator itself -- repository-level enforcement never sees these records.
                    var validator = ValidatorFactory.Create<T>();
                    foreach (var record in Records)
                    {
                        var outcome = validator.Validate(
                            ValidationContext<T>.For(record!, ValidationOperation.Update, new StoreEntityLookup()));

                        if (!outcome.IsValid)
                        {
                            Status = $"Cannot save: {outcome.Messages[0]}";
                            return;
                        }
                    }


            """
            : string.Empty;

        // The same helper the two store templates and Repository<T> emit, for the same reason:
        // each names Session from a different folder and none of them may depend on another. This
        // file imports no Auth namespace, so it is named in full, exactly as they name it.
        var currentRole = login
            ? $$"""

                private static string? CurrentRole() => {{rootNamespace}}.Auth.Session.Current?.Role.ToString();

            """
            : """

                // No desktop session to read a user from, so there is nobody to attribute the
                // delete to. Not a stub to fill in later: an app generated without a login has
                // nobody to refuse, which is exactly what the matrix reads a null role as.
                private static string? CurrentRole() => null;

            """;

        return $$"""
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Input;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Screens;
using {{rootNamespace}}.Validation;

namespace {{rootNamespace}}.ViewModels;

/// <summary>One entity's records, edited in memory and persisted on Save.</summary>
{{constraintReason}}
public sealed class EntityViewModel<T> : ObservableObject, IEntityViewModel where T : class, new()
{
    private readonly JsonStore<T> _store = new();
    private object? _selected;
    private string _status = string.Empty;

    /// <summary>The role defaults to Master so the four pre-business layouts, which never
    /// pass one, behave exactly as they did.</summary>
    public EntityViewModel(string title, EntityRole role = EntityRole.Master)
    {
        Title = title;
        Role = role;
        Records = new ObservableCollection<T>(_store.GetAll());
        AddCommand = new RelayCommand(Add);
        DeleteCommand = new RelayCommand(Delete, () => _selected is T);
        SaveCommand = new RelayCommand(Save);
    }

    public string Title { get; }

    public EntityRole Role { get; }

    public int Count => Records.Count;

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

        // Checked HERE, not at Save: this screen defers every change to one SaveAll, and a
        // whole-list write cannot tell that a record was removed. There is nowhere later to ask.
        if (!Permissions.Allows(ValidationOperation.Delete, typeof(T).Name, CurrentRole()))
        {
            Status = Permissions.Refusal(ValidationOperation.Delete, typeof(T).Name);
            return;
        }

        Records.Remove(item);
        Selected = null;
        Status = $"Removed a {Title}. Not saved yet.";
    }

    private void Save()
    {
{{validateRecords}}        var write = _store.SaveAll(Records.ToList());

        Status = write.Success
            ? $"Saved {Records.Count} {Title} record(s)."
            : write.Refusal ?? $"Could not write {typeof(T).Name}.json.";
    }
{{currentRole}}}
""";
    }
}

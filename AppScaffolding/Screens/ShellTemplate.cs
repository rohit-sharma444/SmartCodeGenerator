namespace AppScaffolding.Screens;

/// <summary>Emits the dashboard and the shell that holds every screen.</summary>
/// <remarks>
/// The shell owns the content area and a one-deep history: opening a record sets the current
/// screen, and Back sets it back. Not a stack, because nothing in this design opens an editor from
/// an editor.
/// </remarks>
public static class ShellTemplate
{
    /// <summary>Source of <c>ViewModels/DashboardViewModel.cs</c>.</summary>
    public static string Dashboard(string rootNamespace) => $$"""
using System.Collections.Generic;
using System.Linq;
using {{rootNamespace}}.Screens;

namespace {{rootNamespace}}.ViewModels;

/// <summary>One heading and the entities under it.</summary>
public sealed record DashboardGroup(string Title, IReadOnlyList<IEntityViewModel> Entities);

/// <summary>The home screen: per-entity counts, grouped the way the rail is grouped.</summary>
public sealed class DashboardViewModel
{
    public DashboardViewModel(IReadOnlyList<IEntityViewModel> entities)
    {
        // Transactions first: on a business screen they are the work, and the masters are what the
        // work refers to.
        Groups =
        [
            .. Group("Transactions", entities.Where(e => e.Role == EntityRole.Transaction)),
            .. Group("Masters", entities.Where(e => e.Role == EntityRole.Master)),
        ];
    }

    public string Title => "Home";

    public IReadOnlyList<DashboardGroup> Groups { get; }

    /// <summary>A heading with nothing under it is noise, so an empty group is not emitted at
    /// all.</summary>
    private static IEnumerable<DashboardGroup> Group(
        string title, IEnumerable<IEntityViewModel> entities)
    {
        var list = entities.ToList();

        return list.Count == 0 ? [] : [new DashboardGroup(title, list)];
    }
}
""";

    /// <summary>Source of <c>ViewModels/ShellViewModel.cs</c>.</summary>
    public static string Shell(string rootNamespace) => $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using {{rootNamespace}}.Auth;
using {{rootNamespace}}.Screens;

namespace {{rootNamespace}}.ViewModels;

/// <summary>One heading in the navigation rail, and what sits under it.</summary>
public sealed record NavGroup(string Title, IReadOnlyList<IEntityViewModel> Entities);

/// <summary>The application shell: the rail, the header, and whatever screen is showing.</summary>
public sealed class ShellViewModel : ObservableObject
{
    private object? _current;
    private object? _previous;

    public ShellViewModel(
        IReadOnlyList<IEntityViewModel> entities, IReadOnlyList<IEntityViewModel> adminEntities)
    {
        Dashboard = new DashboardViewModel(entities);

        Groups =
        [
            .. Group("Masters", entities.Where(e => e.Role == EntityRole.Master)),
            .. Group("Transactions", entities.Where(e => e.Role == EntityRole.Transaction)),

            // Only for an administrator, and filtered rather than greyed: a screen a role cannot
            // reach is simply not listed. The screen itself refuses too, which is the check that
            // matters.
            .. Session.IsAdmin ? Group("Admin", adminEntities) : [],
        ];

        _current = Dashboard;

        SignOutCommand = new RelayCommand(SignOut);
        BackCommand = new RelayCommand(Back, () => _previous is not null);
        HomeCommand = new RelayCommand(() => TryShow(Dashboard));
    }

    /// <summary>
    /// Asked before leaving an editor with unsaved changes.
    /// </summary>
    /// <remarks>
    /// Supplied by the view, because the answer is a message box on the desktop and a
    /// NavigationLock on the web. This is what a modal was buying: an editor that fills the
    /// content area can be navigated away from, so the shell has to ask.
    /// </remarks>
    public Func<bool> ConfirmDiscard { get; set; } = () => true;

    public DashboardViewModel Dashboard { get; }

    public IReadOnlyList<NavGroup> Groups { get; }

    public string SignedInUser => Session.DisplayName;

    public bool IsAdmin => Session.IsAdmin;

    /// <summary>Whatever is in the content area.</summary>
    public object? Current
    {
        get => _current;
        private set
        {
            if (SetField(ref _current, value))
                OnPropertyChanged(nameof(CanGoBack));
        }
    }

    public bool CanGoBack => _previous is not null;

    public ICommand SignOutCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand HomeCommand { get; }

    /// <summary>
    /// Shows <paramref name="screen"/>, or refuses when the user declines to discard an edit.
    /// </summary>
    /// <remarks>
    /// Returns false rather than throwing, so a rail click that the user cancelled simply leaves
    /// the editor where it was.
    /// </remarks>
    public bool TryShow(object screen)
    {
        if (_current is IRecordEditor { IsDirty: true } && !ConfirmDiscard())
            return false;

        // Leaving an editor drops the history: Back means "back to the list", never "back to the
        // editor you just abandoned".
        _previous = _current is IRecordEditor ? null : _current;

        Current = screen;
        return true;
    }

    /// <summary>Opens a record over the screen that asked for it, and returns there when it
    /// closes.</summary>
    public void ShowEditor(object editor, Action? onClosed = null)
    {
        var from = _current;

        if (editor is IRecordEditor && _current is IRecordEditor { IsDirty: true } && !ConfirmDiscard())
            return;

        _previous = from is IRecordEditor ? _previous : from;

        Current = editor;

        if (onClosed is not null)
            Closed += onClosed;
    }

    /// <summary>Raised by the shell when an editor finishes, so the screen underneath can
    /// reload.</summary>
    public event Action? Closed;

    /// <summary>Returns to the screen the editor was opened over.</summary>
    public void Back()
    {
        if (_previous is null)
            return;

        var target = _previous;
        _previous = null;

        Closed?.Invoke();
        Current = target;
    }

    private void SignOut()
    {
        if (_current is IRecordEditor { IsDirty: true } && !ConfirmDiscard())
            return;

        Session.RequestSignOut();
    }

    private static IEnumerable<NavGroup> Group(
        string title, IEnumerable<IEntityViewModel> entities)
    {
        var list = entities.ToList();

        return list.Count == 0 ? [] : [new NavGroup(title, list)];
    }
}
""";
}

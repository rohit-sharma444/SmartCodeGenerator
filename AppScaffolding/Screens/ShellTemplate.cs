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
using System.Reflection;
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

        // Every screen's editor request lands in the content area. Wired here rather than in each
        // view: a screen raises the request and does not know what a window is, and doing it once
        // means both desktop targets behave the same. The WPF window bound a screen whose request
        // nothing handled, so New did nothing at all.
        foreach (var entity in entities.Concat(adminEntities))
            Route(entity);

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
        if (_current is IDirtyScreen { IsDirty: true } && !ConfirmDiscard())
            return false;

        // Leaving an editor drops the history: Back means "back to the list", never "back to the
        // editor you just abandoned".
        _previous = _current is IRecordEditor ? null : _current;

        Current = screen;
        return true;
    }

    /// <summary>Returns to the screen the editor was opened over.</summary>
    public void Back()
    {
        if (_previous is null)
            return;

        var target = _previous;
        _previous = null;

        Current = target;
    }

    private void SignOut()
    {
        if (_current is IDirtyScreen { IsDirty: true } && !ConfirmDiscard())
            return;

        Session.RequestSignOut();
    }

    /// <summary>
    /// Subscribes to one screen's editor request.
    /// </summary>
    /// <remarks>
    /// By reflection because the event is <c>EventHandler&lt;RecordEditorViewModel&lt;T&gt;&gt;</c>
    /// and this shell holds <c>IEntityViewModel</c>: it cannot name T. A pane with no such event —
    /// the Users pane, or any of the four older layouts' — is simply left alone.
    /// </remarks>
    private void Route(IEntityViewModel entity)
    {
        var request = entity.GetType().GetEvent("EditorRequested");

        if (request?.EventHandlerType is null)
            return;

        var handler = GetType()
            .GetMethod(nameof(OnEditorRequested), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(request.EventHandlerType.GetGenericArguments()[0]);

        request.AddEventHandler(
            entity, Delegate.CreateDelegate(request.EventHandlerType, this, handler));
    }

    /// <summary>Shows the editor over the screen that asked for it, and reloads that screen when
    /// it closes.</summary>
    private void OnEditorRequested<TEditor>(object? sender, TEditor editor)
    {
        if (editor is not IRecordEditor || !TryShow(editor))
            return;

        var opened = sender;

        editor.GetType().GetEvent("Closed")?.AddEventHandler(
            editor,
            new EventHandler((_, _) =>
            {
                // The list underneath re-reads the store, then the shell returns to it.
                opened?.GetType().GetMethod("Reload")?.Invoke(opened, null);

                Back();
            }));
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

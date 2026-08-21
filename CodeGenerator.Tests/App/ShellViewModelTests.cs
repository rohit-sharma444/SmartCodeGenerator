using System.Collections;
using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>The shell groups the rail, holds the content area, and guards a dirty editor. The
/// guard is what a modal was buying, and it is the only interaction this design adds.</summary>
[TestClass]
public class ShellViewModelTests
{
    /// <summary>Enough of a session and a pane to build a shell without the whole auth core.</summary>
    private const string Stub = """
using System.Collections;
using System.Collections.Generic;
using System.Windows.Input;

public static class Session
{
    public static bool IsAdmin { get; set; } = true;
    public static string DisplayName { get; set; } = "rohit";
    public static bool SignOutRequested { get; private set; }
    public static void RequestSignOut() => SignOutRequested = true;
    public static void Reset() { SignOutRequested = false; IsAdmin = true; }
}

public sealed class FakePane : IEntityViewModel
{
    public FakePane(string title, EntityRole role) { Title = title; Role = role; }

    public string Title { get; }
    public EntityRole Role { get; }
    public int Count => 0;
    public IEnumerable Items => System.Array.Empty<object>();
    public object? Selected { get; set; }
    public ICommand AddCommand => null!;
    public ICommand DeleteCommand => null!;
    public ICommand SaveCommand => null!;
    public string Status => string.Empty;
}

/// <summary>Stands in for a RecordEditorViewModel without needing a store.</summary>
public sealed class FakeEditor : IRecordEditor
{
    public bool IsDirty { get; set; }
    public string Title => "Editing";
    public IReadOnlyList<ILineSet> Lines => System.Array.Empty<ILineSet>();
}
""";

    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            ViewModelTemplates.ObservableObject("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            ViewModelTemplates.PortableRelayCommand("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            ScreenTemplates.RoleEnum("Acme").Replace("namespace Acme.Screens;", string.Empty),
            ViewModelTemplates.EntityViewModelInterface("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            // The editor names IStore/StoreFactory even though this shell never opens a real one,
            // so the store stub travels with it.
            RecordEditorTests.StoreAndModelStub,
            RecordEditorTests.Editor(validation: false),
            Stub,
            ShellTemplate.Dashboard("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            ShellTemplate.Shell("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);

        assembly.GetType("Session")!.GetMethod("Reset")!.Invoke(null, null);

        return assembly;
    }

    private static object Pane(Assembly assembly, string title, string role)
        => Activator.CreateInstance(
            assembly.GetType("FakePane")!,
            [title, Enum.Parse(assembly.GetType("EntityRole")!, role)])!;

    private static object Shell(Assembly assembly, bool withAdmin = true)
    {
        var paneType = assembly.GetType("IEntityViewModel")!;
        var listType = typeof(List<>).MakeGenericType(paneType);

        var entities = (IList)Activator.CreateInstance(listType)!;
        entities.Add(Pane(assembly, "Customer", "Master"));
        entities.Add(Pane(assembly, "Product", "Master"));
        entities.Add(Pane(assembly, "Order", "Transaction"));

        var admin = (IList)Activator.CreateInstance(listType)!;
        if (withAdmin)
            admin.Add(Pane(assembly, "Users", "Master"));

        return Activator.CreateInstance(assembly.GetType("ShellViewModel")!, [entities, admin])!;
    }

    private static List<string> GroupTitles(object shell)
        => ((IEnumerable)shell.GetType().GetProperty("Groups")!.GetValue(shell)!)
            .Cast<object>()
            .Select(g => (string)g.GetType().GetProperty("Title")!.GetValue(g)!)
            .ToList();

    private static object? Current(object shell)
        => shell.GetType().GetProperty("Current")!.GetValue(shell);

    private static bool TryShow(object shell, object screen)
        => (bool)shell.GetType().GetMethod("TryShow")!.Invoke(shell, [screen])!;

    /// <summary>
    /// The same shell over a REAL master screen, which is the only way to exercise the editor
    /// request.
    /// </summary>
    /// <remarks>
    /// FakePane has no such event, so every test above passes whether the shell routes one or not
    /// — and it did not: the WPF window showed a list whose New button did nothing at all, because
    /// the request was raised into a void. Found by clicking New in a built application.
    /// </remarks>
    private static Assembly EmitWithRealScreen()
    {
        var source = EmittedSource.Flatten(
            ViewModelTemplates.ObservableObject("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            ViewModelTemplates.PortableRelayCommand("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            ScreenTemplates.RoleEnum("Acme").Replace("namespace Acme.Screens;", string.Empty),
            ViewModelTemplates.EntityViewModelInterface("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            RecordEditorTests.StoreAndModelStub,
            RecordEditorTests.Editor(validation: false),
            Stub,
            MasterScreenTemplate.Source("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty)
                .Replace("using Acme.Data;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            ShellTemplate.Dashboard("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            ShellTemplate.Shell("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);

        assembly.GetType("Session")!.GetMethod("Reset")!.Invoke(null, null);

        return assembly;
    }

    /// <summary>A shell whose only entity is a real master screen over two books.</summary>
    private static (object Shell, object Screen) ShellOverARealScreen(Assembly assembly)
    {
        var screen = MasterScreenTests.Screen(assembly, "MasterScreenViewModel", "First", "Second");

        var listType = typeof(List<>).MakeGenericType(assembly.GetType("IEntityViewModel")!);
        var entities = (IList)Activator.CreateInstance(listType)!;
        entities.Add(screen);

        var shell = Activator.CreateInstance(
            assembly.GetType("ShellViewModel")!,
            [entities, Activator.CreateInstance(listType)!])!;

        Assert.IsTrue(TryShow(shell, screen), "the list should be showing to begin with");

        return (shell, screen);
    }

    [TestMethod]
    public void A_screen_that_asks_for_an_editor_gets_one_in_the_content_area()
    {
        var assembly = EmitWithRealScreen();
        var (shell, screen) = ShellOverARealScreen(assembly);

        RecordEditorTests.Execute(screen, "NewCommand");

        var current = Current(shell);

        Assert.IsNotNull(current, "New raised a request nothing handled");
        Assert.AreNotSame(screen, current);
        StringAssert.StartsWith(current!.GetType().Name, "RecordEditorViewModel");
    }

    /// <summary>And closing it comes back to the list, which re-reads the store on the way.</summary>
    [TestMethod]
    public void Closing_the_editor_returns_to_the_list()
    {
        var assembly = EmitWithRealScreen();
        var (shell, screen) = ShellOverARealScreen(assembly);

        RecordEditorTests.Execute(screen, "NewCommand");

        var editor = Current(shell)!;

        // A saved record proves the reload: the list had two rows and the store now has three.
        editor.GetType().GetProperty("Draft")!.GetValue(editor)!
            .GetType().GetProperty("Title")!.SetValue(
                editor.GetType().GetProperty("Draft")!.GetValue(editor), "Third");

        RecordEditorTests.Execute(editor, "SaveCommand");

        Assert.AreSame(screen, Current(shell), "the shell did not come back to the list");

        var items = (IEnumerable)screen.GetType().GetProperty("Items")!.GetValue(screen)!;

        Assert.HasCount(3, items.Cast<object>().ToList());
    }

    [TestMethod]
    public void The_rail_groups_masters_and_transactions_separately()
    {
        var titles = GroupTitles(Shell(Emit()));

        CollectionAssert.Contains(titles, "Masters");
        CollectionAssert.Contains(titles, "Transactions");
    }

    /// <summary>A heading with nothing under it is noise.</summary>
    [TestMethod]
    public void An_empty_group_is_not_shown()
    {
        var assembly = Emit();
        assembly.GetType("Session")!.GetProperty("IsAdmin")!.SetValue(null, true);

        var shell = Shell(assembly, withAdmin: false);

        CollectionAssert.DoesNotContain(GroupTitles(shell), "Admin");
    }

    [TestMethod]
    public void The_admin_group_is_shown_to_an_administrator()
        => CollectionAssert.Contains(GroupTitles(Shell(Emit())), "Admin");

    [TestMethod]
    public void The_admin_group_is_absent_for_a_plain_user()
    {
        var assembly = Emit();
        assembly.GetType("Session")!.GetProperty("IsAdmin")!.SetValue(null, false);

        CollectionAssert.DoesNotContain(GroupTitles(Shell(assembly)), "Admin");
    }

    [TestMethod]
    public void The_shell_opens_on_the_dashboard()
    {
        var shell = Shell(Emit());

        Assert.AreEqual("DashboardViewModel", Current(shell)!.GetType().Name);
    }

    [TestMethod]
    public void The_dashboard_groups_the_entities_too()
    {
        var shell = Shell(Emit());
        var dashboard = shell.GetType().GetProperty("Dashboard")!.GetValue(shell)!;

        var titles = ((IEnumerable)dashboard.GetType().GetProperty("Groups")!.GetValue(dashboard)!)
            .Cast<object>()
            .Select(g => (string)g.GetType().GetProperty("Title")!.GetValue(g)!)
            .ToList();

        CollectionAssert.Contains(titles, "Transactions");
        CollectionAssert.Contains(titles, "Masters");
    }

    [TestMethod]
    public void Showing_a_screen_remembers_the_one_it_came_from()
    {
        var assembly = Emit();
        var shell = Shell(assembly);
        var pane = Pane(assembly, "Customer", "Master");

        Assert.IsTrue(TryShow(shell, pane));
        Assert.AreSame(pane, Current(shell));
        Assert.IsTrue((bool)shell.GetType().GetProperty("CanGoBack")!.GetValue(shell)!);

        shell.GetType().GetMethod("Back")!.Invoke(shell, null);

        Assert.AreEqual("DashboardViewModel", Current(shell)!.GetType().Name);
    }

    /// <summary>The guard that replaces the modal. Declining leaves the editor exactly where it
    /// was.</summary>
    [TestMethod]
    public void Navigating_away_from_a_dirty_editor_asks_first()
    {
        var assembly = Emit();
        var shell = Shell(assembly);

        var editor = Activator.CreateInstance(assembly.GetType("FakeEditor")!)!;
        editor.GetType().GetProperty("IsDirty")!.SetValue(editor, true);

        TryShow(shell, editor);

        shell.GetType().GetProperty("ConfirmDiscard")!.SetValue(shell, new Func<bool>(() => false));

        Assert.IsFalse(TryShow(shell, Pane(assembly, "Customer", "Master")));
        Assert.AreSame(editor, Current(shell));
    }

    [TestMethod]
    public void A_confirmed_discard_navigates()
    {
        var assembly = Emit();
        var shell = Shell(assembly);

        var editor = Activator.CreateInstance(assembly.GetType("FakeEditor")!)!;
        editor.GetType().GetProperty("IsDirty")!.SetValue(editor, true);

        TryShow(shell, editor);

        shell.GetType().GetProperty("ConfirmDiscard")!.SetValue(shell, new Func<bool>(() => true));

        var pane = Pane(assembly, "Customer", "Master");

        Assert.IsTrue(TryShow(shell, pane));
        Assert.AreSame(pane, Current(shell));
    }

    /// <summary>A clean editor never asks. Prompting on an untouched form would train people to
    /// dismiss the prompt without reading it.</summary>
    [TestMethod]
    public void A_clean_editor_is_left_without_a_question()
    {
        var assembly = Emit();
        var shell = Shell(assembly);

        var editor = Activator.CreateInstance(assembly.GetType("FakeEditor")!)!;

        TryShow(shell, editor);

        var asked = false;
        shell.GetType().GetProperty("ConfirmDiscard")!
            .SetValue(shell, new Func<bool>(() => { asked = true; return true; }));

        TryShow(shell, Pane(assembly, "Customer", "Master"));

        Assert.IsFalse(asked);
    }

    /// <summary>Signing out is a navigation too, so it asks — otherwise the one exit that loses
    /// work is the one that never warns.</summary>
    [TestMethod]
    public void Signing_out_of_a_dirty_editor_asks_first()
    {
        var assembly = Emit();
        var shell = Shell(assembly);

        var editor = Activator.CreateInstance(assembly.GetType("FakeEditor")!)!;
        editor.GetType().GetProperty("IsDirty")!.SetValue(editor, true);

        TryShow(shell, editor);

        shell.GetType().GetProperty("ConfirmDiscard")!.SetValue(shell, new Func<bool>(() => false));
        RecordEditorTests.Execute(shell, "SignOutCommand");

        Assert.IsFalse(
            (bool)assembly.GetType("Session")!.GetProperty("SignOutRequested")!.GetValue(null)!);
    }

    /// <summary>One assembly, one shell: each Emit() compiles a fresh assembly with its own
    /// static Session, so reading a property off one and invoking it on another is meaningless.</summary>
    [TestMethod]
    public void The_header_names_the_signed_in_user()
    {
        var shell = Shell(Emit());

        Assert.AreEqual("rohit", shell.GetType().GetProperty("SignedInUser")!.GetValue(shell));
    }
}

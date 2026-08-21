using System.Collections;
using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>The list finds a record; the editor changes it. The list itself has no edit path at
/// all, which is what makes the whole application edit one way.</summary>
[TestClass]
public class MasterScreenTests
{
    /// <summary>A session stub: the delete guard asks whether an administrator is signed in.</summary>
    private const string SessionStub = """
public static class Session
{
    public static bool IsAdmin { get; set; } = true;
}
""";

    internal static Assembly Emit()
        => RecordEditorTests.Emit(
            SessionStub,
            ScreenTemplates.RoleEnum("Acme").Replace("namespace Acme.Screens;", string.Empty),
            ViewModelTemplates.EntityViewModelInterface("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            MasterScreenTemplate.Source("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty)
                .Replace("using Acme.Data;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty));

    internal static object Screen(Assembly assembly, string typeName, params string[] titles)
    {
        var bookType = assembly.GetType("Book")!;
        var rows = RecordEditorTests.Rows(assembly);

        rows.Clear();

        var id = 1;
        foreach (var title in titles)
        {
            var book = Activator.CreateInstance(bookType)!;
            bookType.GetProperty("Id")!.SetValue(book, id++);
            bookType.GetProperty("Title")!.SetValue(book, title);
            rows.Add(book);
        }

        var screenType = assembly.GetType(typeName + "`1")!.MakeGenericType(bookType);

        return Activator.CreateInstance(screenType, ["Book"])!;
    }

    private static IList Items(object screen)
        => ((IEnumerable)screen.GetType().GetProperty("Items")!.GetValue(screen)!)
            .Cast<object>().ToList();

    /// <summary>
    /// Captures the editor the screen asks for.
    /// </summary>
    /// <remarks>
    /// The handler is GENERIC because the event is <c>EventHandler&lt;RecordEditorViewModel&lt;T&gt;&gt;</c>,
    /// and Delegate.CreateDelegate binds only on an exact signature match — a
    /// <c>(object?, object)</c> method is rejected outright.
    /// </remarks>
    private sealed class Capture
    {
        public object? Requested { get; private set; }

        public void Handle<TArgs>(object? sender, TArgs e) => Requested = e;
    }

    private static Capture Listen(object screen)
    {
        var capture = new Capture();
        var evt = screen.GetType().GetEvent("EditorRequested")!;

        var handler = typeof(Capture).GetMethod(nameof(Capture.Handle))!
            .MakeGenericMethod(evt.EventHandlerType!.GetGenericArguments()[0]);

        evt.AddEventHandler(
            screen, Delegate.CreateDelegate(evt.EventHandlerType!, capture, handler));

        return capture;
    }

    [TestMethod]
    public void It_lists_what_the_store_holds()
        => Assert.HasCount(2, Items(Screen(Emit(), "MasterScreenViewModel", "One", "Two")));

    [TestMethod]
    public void The_role_is_master()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "One");

        Assert.AreEqual("Master", screen.GetType().GetProperty("Role")!.GetValue(screen)!.ToString());
    }

    [TestMethod]
    public void Search_narrows_the_list_without_touching_the_store()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel", "Alpha", "Beta");

        screen.GetType().GetProperty("Search")!.SetValue(screen, "alph");

        Assert.HasCount(1, Items(screen));
        Assert.HasCount(2, RecordEditorTests.Rows(assembly));
    }

    /// <summary>A list that only matched exact casing would read as broken.</summary>
    [TestMethod]
    public void Search_ignores_case()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Alpha", "Beta");

        screen.GetType().GetProperty("Search")!.SetValue(screen, "ALPHA");

        Assert.HasCount(1, Items(screen));
    }

    /// <summary>Opening asks the shell for an editor rather than editing in place — the list has
    /// no edit path at all, by construction.</summary>
    [TestMethod]
    public void Opening_a_record_raises_an_editor_request()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "One");
        var capture = Listen(screen);

        screen.GetType().GetProperty("Selected")!.SetValue(screen, Items(screen)[0]);
        RecordEditorTests.Execute(screen, "OpenCommand");

        Assert.IsNotNull(capture.Requested);
        Assert.IsFalse(
            (bool)capture.Requested!.GetType().GetProperty("IsNew")!.GetValue(capture.Requested)!);
    }

    [TestMethod]
    public void New_raises_an_editor_request_for_a_new_record()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "One");
        var capture = Listen(screen);

        RecordEditorTests.Execute(screen, "NewCommand");

        Assert.IsNotNull(capture.Requested);
        Assert.IsTrue(
            (bool)capture.Requested!.GetType().GetProperty("IsNew")!.GetValue(capture.Requested)!);
    }

    [TestMethod]
    public void An_administrator_can_delete_a_record()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel", "One", "Two");

        screen.GetType().GetProperty("Selected")!.SetValue(screen, Items(screen)[0]);
        RecordEditorTests.Execute(screen, "DeleteCommand");

        Assert.HasCount(1, Items(screen));
    }

    /// <summary>One of the role's two effects, and it has to bite on the screen rather than only
    /// in the navigation.</summary>
    [TestMethod]
    public void A_plain_user_cannot_delete_a_record()
    {
        var assembly = Emit();
        assembly.GetType("Session")!.GetProperty("IsAdmin")!.SetValue(null, false);

        try
        {
            var screen = Screen(assembly, "MasterScreenViewModel", "One", "Two");

            screen.GetType().GetProperty("Selected")!.SetValue(screen, Items(screen)[0]);
            RecordEditorTests.Execute(screen, "DeleteCommand");

            Assert.HasCount(2, Items(screen));
            StringAssert.Contains(
                (string)screen.GetType().GetProperty("Status")!.GetValue(screen)!, "administrator");
        }
        finally
        {
            assembly.GetType("Session")!.GetProperty("IsAdmin")!.SetValue(null, true);
        }
    }

    /// <summary>The shell calls this when an editor closes, so a saved change shows without
    /// rebuilding the screen.</summary>
    [TestMethod]
    public void Reload_picks_up_a_change_made_elsewhere()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel", "One");

        var bookType = assembly.GetType("Book")!;
        var extra = Activator.CreateInstance(bookType)!;
        bookType.GetProperty("Id")!.SetValue(extra, 99);
        RecordEditorTests.Rows(assembly).Add(extra);

        screen.GetType().GetMethod("Reload")!.Invoke(screen, null);

        Assert.HasCount(2, Items(screen));
    }
}

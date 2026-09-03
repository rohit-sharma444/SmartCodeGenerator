using System.Collections;
using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>The form and the list are one screen: selecting a row loads it into the form above,
/// and nothing navigates. A transaction is the same class with that turned off, and both halves
/// are proved here — over compiled emitted code, because a screen that edited a row in place
/// rather than a copy would compile perfectly.</summary>
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

    /// <summary>A second model, with the long-text field Book has none of, so the Additional
    /// Information tab can be asserted both ways round in one assembly.</summary>
    private const string MemoStub = """
public class Memo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
}
""";

    internal static Assembly Emit() => Emit(withTransaction: false, validation: false);

    /// <summary>The master screen and the transaction screen that derives from it, in one
    /// assembly — the only way to exercise the one flag that separates the two.</summary>
    private static Assembly EmitWithTransaction() => Emit(withTransaction: true, validation: false);

    /// <summary>The screen over an app generated WITH validators, where a book with no title is
    /// refused — the only way a fixture here can produce a save that did not go through.</summary>
    private static Assembly EmitValidating() => Emit(withTransaction: false, validation: true);

    private static Assembly Emit(bool withTransaction, bool validation)
    {
        string[] sources =
        [
            SessionStub,
            MemoStub,
            ScreenTemplates.RoleEnum("Acme").Replace("namespace Acme.Screens;", string.Empty),
            ViewModelTemplates.EntityViewModelInterface("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            MasterScreenTemplate.Source("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty)
                .Replace("using Acme.Data;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
        ];

        if (!withTransaction)
            return RecordEditorTests.Emit(validation, sources);

        return RecordEditorTests.Emit(
            validation,
            [
                .. sources,
                TransactionScreenTemplate.Source("Acme")
                    .Replace("namespace Acme.ViewModels;", string.Empty)
                    .Replace("using Acme.Screens;", string.Empty),
            ]);
    }

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

        // The title, and a null for anything else the screen takes: Activator does not fill an
        // optional parameter, and a transaction screen's constructor has a second one — the
        // line-set factory.
        var arguments = new object?[screenType.GetConstructors().Single().GetParameters().Length];
        arguments[0] = "Book";

        return Activator.CreateInstance(screenType, arguments)!;
    }

    private static IList Items(object screen)
        => ((IEnumerable)screen.GetType().GetProperty("Items")!.GetValue(screen)!)
            .Cast<object>().ToList();

    /// <summary>The form at the top of the screen, which is never null.</summary>
    private static object EditorOf(object screen)
        => screen.GetType().GetProperty("Editor")!.GetValue(screen)!;

    private static object Draft(object editor)
        => editor.GetType().GetProperty("Draft")!.GetValue(editor)!;

    private static bool IsNew(object editor)
        => (bool)editor.GetType().GetProperty("IsNew")!.GetValue(editor)!;

    private static string TitleOf(object record)
        => (string)record.GetType().GetProperty("Title")!.GetValue(record)!;

    private static void SetTitle(object record, string value)
        => record.GetType().GetProperty("Title")!.SetValue(record, value);

    private static void Select(object screen, object? row)
        => screen.GetType().GetProperty("Selected")!.SetValue(screen, row);

    private static string StatusOf(object screen)
        => (string)screen.GetType().GetProperty("Status")!.GetValue(screen)!;

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

    /// <summary>
    /// A master edits on its own surface and asks the shell for nothing.
    /// </summary>
    /// <remarks>
    /// One of the pair of guards on the flag that separates the two screens. This one fails if a
    /// master is ever routed to the shell again — the editor would open OVER the list it is
    /// supposed to sit above — and the transaction guard below fails if a transaction is ever made
    /// to edit inline, which would leave its lines nowhere to be entered.
    /// </remarks>
    [TestMethod]
    public void A_master_edits_inline_and_asks_the_shell_for_nothing()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "One");
        var capture = Listen(screen);

        RecordEditorTests.Execute(screen, "NewCommand");

        var blank = EditorOf(screen);

        Select(screen, Items(screen)[0]);

        Assert.IsNull(capture.Requested, "a master asked the shell for an editor screen");
        Assert.AreNotSame(blank, EditorOf(screen), "selecting a row did not move the inline form");
        Assert.IsFalse(IsNew(EditorOf(screen)), "the row went into the form as a new record");
    }

    /// <summary>
    /// A transaction's list is a list: it opens a record on a screen of its own, which is the
    /// ONLY route to a transaction's entry screen.
    /// </summary>
    /// <remarks>
    /// Both bindings that reach it go through <c>OpenCommand</c> — WPF's transaction template and
    /// the WinForms list's Open button and double-click — so deleting the command, or making the
    /// screen swallow the request by editing inline, would leave no way to open a purchase order
    /// at all.
    /// </remarks>
    [TestMethod]
    public void A_transaction_opens_a_record_on_a_screen_of_its_own()
    {
        var screen = Screen(EmitWithTransaction(), "TransactionScreenViewModel", "One");
        var capture = Listen(screen);

        // The form this screen would hold if it edited inline, taken BEFORE the select so the
        // select can be shown not to have touched it. Comparing the requested editor against
        // EditorOf afterwards proves nothing: the two are built by separate calls either way.
        var untouched = EditorOf(screen);

        Select(screen, Items(screen)[0]);

        Assert.IsNull(capture.Requested, "selecting a row on a transaction list opened it");
        Assert.AreSame(
            untouched, EditorOf(screen), "selecting a row loaded it into a form of the list's own");

        RecordEditorTests.Execute(screen, "OpenCommand");

        Assert.IsNotNull(capture.Requested, "nothing can reach the transaction's entry screen");
        Assert.IsFalse(IsNew(capture.Requested!), "the selected record opened as a new one");
    }

    /// <summary>The half of the old behaviour that survives: New on a transaction list still asks
    /// the shell, because that is where its lines are entered.</summary>
    [TestMethod]
    public void New_on_a_transaction_still_asks_for_an_editor_screen()
    {
        var screen = Screen(EmitWithTransaction(), "TransactionScreenViewModel", "One");
        var capture = Listen(screen);

        RecordEditorTests.Execute(screen, "NewCommand");

        Assert.IsNotNull(capture.Requested);
        Assert.IsTrue(IsNew(capture.Requested!));
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

    // ---- the form the screen holds ----

    [TestMethod]
    public void The_screen_opens_on_a_blank_editor()
        => Assert.IsTrue(IsNew(EditorOf(Screen(Emit(), "MasterScreenViewModel", "Dune"))));

    [TestMethod]
    public void Selecting_a_row_loads_it_into_the_editor()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune");

        Select(screen, Items(screen)[0]);

        Assert.AreEqual("Dune", TitleOf(Draft(EditorOf(screen))));
        Assert.IsFalse(IsNew(EditorOf(screen)));
    }

    /// <summary>A fresh instance, not a mutated one: the draft is a clone, and reusing the
    /// instance would leave the previous record's snapshot in place and make IsDirty lie.</summary>
    [TestMethod]
    public void Selecting_a_second_row_replaces_the_editor()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune", "Emma");
        var rows = Items(screen);

        Select(screen, rows[0]);

        var first = EditorOf(screen);

        Select(screen, rows[1]);

        Assert.AreNotSame(first, EditorOf(screen));
        Assert.AreEqual("Emma", TitleOf(Draft(EditorOf(screen))));
    }

    [TestMethod]
    public void New_blanks_the_editor()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune");

        Select(screen, Items(screen)[0]);
        RecordEditorTests.Execute(screen, "NewCommand");

        Assert.IsTrue(IsNew(EditorOf(screen)));
    }

    /// <summary>Clear blanks the form and leaves the selection, so the list still shows where you
    /// were; Cancel drops both.</summary>
    [TestMethod]
    public void Clear_keeps_the_selection_and_Cancel_drops_it()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune");
        var selected = screen.GetType().GetProperty("Selected")!;

        Select(screen, Items(screen)[0]);
        RecordEditorTests.Execute(screen, "ClearCommand");

        Assert.IsNotNull(selected.GetValue(screen));
        Assert.IsTrue(IsNew(EditorOf(screen)));

        // Edit rather than a second Select: Items hands back the very instances already selected,
        // so re-selecting one sets nothing and the setter never reloads the form. The form would
        // still be blank when Cancel ran, and the closing assertion would pass with Cancel doing
        // nothing at all.
        RecordEditorTests.Execute(screen, "EditCommand");

        Assert.IsFalse(IsNew(EditorOf(screen)), "Edit did not put the row back in the form");

        RecordEditorTests.Execute(screen, "CancelCommand");

        Assert.IsNull(selected.GetValue(screen));
        Assert.IsTrue(IsNew(EditorOf(screen)), "Cancel left the record in the form");
    }

    /// <summary>Edit is what the action bar's button and the grid's double-click both run, and it
    /// loads the selection whether or not selecting it already did.</summary>
    [TestMethod]
    public void Edit_loads_the_selected_row_into_the_editor()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune");

        Select(screen, Items(screen)[0]);
        RecordEditorTests.Execute(screen, "ClearCommand");
        RecordEditorTests.Execute(screen, "EditCommand");

        Assert.IsFalse(IsNew(EditorOf(screen)));
        Assert.AreEqual("Dune", TitleOf(Draft(EditorOf(screen))));
    }

    [TestMethod]
    public void Saving_writes_the_editor_and_reloads_the_list()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune");

        SetTitle(Draft(EditorOf(screen)), "New Book");
        RecordEditorTests.Execute(screen, "SaveCommand");

        CollectionAssert.Contains(Items(screen).Cast<object>().Select(TitleOf).ToList(), "New Book");
    }

    /// <summary>
    /// Pressing Save twice must not enter the record twice.
    /// </summary>
    /// <remarks>
    /// The editor no longer closes over the list — it stays on screen with the Save button under
    /// the cursor — and an editor still marked new adds its draft again every time it is saved.
    /// </remarks>
    [TestMethod]
    public void Saving_twice_enters_the_record_once()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel");

        SetTitle(Draft(EditorOf(screen)), "New Book");
        RecordEditorTests.Execute(screen, "SaveCommand");
        RecordEditorTests.Execute(screen, "SaveCommand");

        Assert.HasCount(1, RecordEditorTests.Rows(assembly));
    }

    /// <summary>
    /// The form is still a copy after a save, not the row the list is showing.
    /// </summary>
    /// <remarks>
    /// The store takes the draft OBJECT rather than a copy of it, so the row the reloaded list
    /// shows and the record in the form are the same instance. Typing on into it would change a
    /// stored row with nothing saved, and Cancel would not put it back.
    /// </remarks>
    [TestMethod]
    public void Typing_after_a_save_does_not_reach_the_stored_row()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel");

        SetTitle(Draft(EditorOf(screen)), "Saved");
        RecordEditorTests.Execute(screen, "SaveCommand");

        SetTitle(Draft(EditorOf(screen)), "typed on, never saved");

        Assert.AreEqual("Saved", TitleOf(RecordEditorTests.Rows(assembly)[0]!));
    }

    /// <summary>
    /// Editing a saved record and saving again must not write the pre-edit values back.
    /// </summary>
    /// <remarks>
    /// The editor puts its draft OBJECT into the store in place of the row it was opened over, so
    /// after the reload the instance <c>Selected</c> points at is in no list at all. Left there,
    /// Edit loads those pre-edit values back into the form and the next Save writes them over what
    /// was just saved, by id, reporting success. WPF happens to mask it — replacing the list
    /// pushes SelectedItem back through the two-way binding — and WinForms, which rebinds only
    /// from the search box, does not, so the view model has to be right on its own.
    /// </remarks>
    [TestMethod]
    public void Editing_a_saved_record_again_does_not_write_the_old_values_back()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel", "Original");

        Select(screen, Items(screen)[0]);
        SetTitle(Draft(EditorOf(screen)), "Edited");
        RecordEditorTests.Execute(screen, "SaveCommand");

        RecordEditorTests.Execute(screen, "EditCommand");
        RecordEditorTests.Execute(screen, "SaveCommand");

        Assert.HasCount(1, RecordEditorTests.Rows(assembly));
        Assert.AreEqual(
            "Edited",
            TitleOf(RecordEditorTests.Rows(assembly)[0]!),
            "the second save reverted the record to what it held before the first");
    }

    /// <summary>
    /// A refused save leaves the form exactly as it was typed.
    /// </summary>
    /// <remarks>
    /// The only thing the Closed/written signal in <c>Save</c> is for. Swap the form for a fresh
    /// one unconditionally and every other test here still passes — the swap is right on a
    /// save that went through; on a refused one it throws away what the user typed, and takes the
    /// message telling them why with it.
    /// </remarks>
    [TestMethod]
    public void A_refused_save_leaves_the_typed_text_in_the_form()
    {
        var assembly = EmitValidating();
        var screen = Screen(assembly, "MasterScreenViewModel", "Dune");

        Select(screen, Items(screen)[0]);

        var form = EditorOf(screen);

        SetTitle(Draft(form), string.Empty);
        RecordEditorTests.Execute(screen, "SaveCommand");

        Assert.AreSame(form, EditorOf(screen), "the refused save replaced the form under the user");
        Assert.AreEqual(
            string.Empty, TitleOf(Draft(EditorOf(screen))), "the typed text was thrown away");
        Assert.AreEqual(
            "Dune", TitleOf(RecordEditorTests.Rows(assembly)[0]!), "the store took a refused record");
        StringAssert.Contains(StatusOf(screen), "Title is required.");
    }

    /// <summary>The second, validator-free way a save is refused: the record was deleted from
    /// somewhere else while the form was open. The editor says so, and the screen must show what
    /// the editor said rather than a success it did not have.</summary>
    [TestMethod]
    public void A_save_of_a_record_that_is_gone_keeps_the_form_and_says_why()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel", "Dune");

        Select(screen, Items(screen)[0]);

        var form = EditorOf(screen);

        RecordEditorTests.Rows(assembly).Clear();
        RecordEditorTests.Execute(screen, "SaveCommand");

        Assert.AreSame(form, EditorOf(screen), "the refused save replaced the form under the user");
        StringAssert.Contains(StatusOf(screen), "no longer exists");
    }

    /// <summary>
    /// A save that went through leaves the screen saying so.
    /// </summary>
    /// <remarks>
    /// The editor sets no status on success — it used to close, and a closed editor says
    /// nothing — so taking its status unconditionally would leave the line blank AND wipe
    /// whatever the screen itself had just said. The wording is Task 12's to settle on a
    /// running app; that there is any is this task's.
    /// </remarks>
    [TestMethod]
    public void A_successful_save_says_so_instead_of_blanking_the_status_line()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel");

        // Delete with nothing selected: a message of the screen's own, and one the save used
        // to wipe on its way past.
        RecordEditorTests.Execute(screen, "DeleteCommand");

        Assert.IsNotEmpty(StatusOf(screen), "the screen said nothing for the save to wipe");

        SetTitle(Draft(EditorOf(screen)), "New Book");
        RecordEditorTests.Execute(screen, "SaveCommand");

        Assert.IsNotEmpty(StatusOf(screen), "a successful save blanked the status line");
    }

    /// <summary>
    /// Save on a transaction LIST enters nothing.
    /// </summary>
    /// <remarks>
    /// The hazard the inline editor brought with it, and the reason the flag reaches <c>Save</c>
    /// at all: a transaction's records are entered on a screen of their own, so the form this
    /// class holds is never filled in on the list — and saving it would build a blank one on
    /// the spot and enter an empty purchase order.
    /// </remarks>
    [TestMethod]
    public void Save_on_a_transaction_list_enters_nothing()
    {
        var assembly = EmitWithTransaction();
        var screen = Screen(assembly, "TransactionScreenViewModel", "One");

        RecordEditorTests.Execute(screen, "SaveCommand");

        Assert.HasCount(1, RecordEditorTests.Rows(assembly));
    }

    /// <summary>
    /// A transaction list has nothing unsaved, and builds no form to work that out.
    /// </summary>
    /// <remarks>
    /// The EditsInline half of IsDirty is an allocation guard as much as an answer: without it
    /// every dirty check — one per attempt to navigate — would build a
    /// RecordEditorViewModel the screen never shows, whose constructor reads every line set out of
    /// the store. The answer is false either way, so only the field can tell the two apart.
    /// </remarks>
    [TestMethod]
    public void A_transaction_list_is_not_dirty_and_builds_no_form_to_say_so()
    {
        var screen = Screen(EmitWithTransaction(), "TransactionScreenViewModel", "One");

        Assert.IsFalse((bool)screen.GetType().GetProperty("IsDirty")!.GetValue(screen)!);

        var editor = screen.GetType().BaseType!
            .GetField("_editor", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.IsNull(editor.GetValue(screen), "the dirty check built a form the screen never shows");
    }

    [TestMethod]
    public void The_screen_is_dirty_when_its_inline_editor_is()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune");
        var isDirty = screen.GetType().GetProperty("IsDirty")!;

        Assert.IsFalse((bool)isDirty.GetValue(screen)!);

        SetTitle(Draft(EditorOf(screen)), "typed");

        Assert.IsTrue((bool)isDirty.GetValue(screen)!);
    }

    /// <summary>
    /// The screen the shell can ask, and NOT the one it renders as an editor.
    /// </summary>
    /// <remarks>
    /// ScreenTemplateSelector tests IRecordEditor BEFORE IEntityViewModel, so a master screen
    /// implementing it to get the dirty check would draw as the editor template on every screen in
    /// the application — it compiles, it passes the dirty check, and only a running app shows it.
    /// </remarks>
    [TestMethod]
    public void The_screen_is_a_dirty_screen_and_not_a_record_editor()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune");

        Assert.IsNotNull(
            screen.GetType().GetInterface("IDirtyScreen"),
            "the shell cannot ask this screen whether it has unsaved work");
        Assert.IsNull(
            screen.GetType().GetInterface("IRecordEditor"),
            "a master screen that is an IRecordEditor renders as the editor template");
    }

    /// <summary>A model with no long-text field must not grow an empty Additional Information
    /// tab — the same rule that drops an empty panel. Asserted both ways round, over two models in
    /// one assembly, because a hardcoded answer would satisfy either one alone.</summary>
    [TestMethod]
    public void Only_a_model_with_long_text_has_an_additional_tab()
    {
        var assembly = Emit();

        Assert.IsFalse(HasAdditional(assembly, "Book"), "Book has no long-text field");
        Assert.IsTrue(HasAdditional(assembly, "Memo"), "Memo.Remarks is a long-text field");
    }

    private static bool HasAdditional(Assembly assembly, string model)
    {
        var screenType = assembly.GetType("MasterScreenViewModel`1")!
            .MakeGenericType(assembly.GetType(model)!);

        var screen = Activator.CreateInstance(screenType, [model])!;

        return (bool)screenType.GetProperty("HasAdditional")!.GetValue(screen)!;
    }
}

using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The editor is why the whole application edits one way, and the copy is why Cancel means
/// something. Both are proved by running the emitted code, because an editor that wrote straight
/// through to the record would compile perfectly.
/// </summary>
[TestClass]
public class RecordEditorTests
{
    /// <summary>A store over a static list, and one model to edit. Shared with the screen tests so
    /// they cannot drift into testing different foundations.</summary>
    internal const string StoreAndModelStub = """
using System;
using System.Collections.Generic;
using System.Linq;

public interface IStore<T> where T : new()
{
    List<T> GetAll();
    bool SaveAll(List<T> all);
    int NextId(IEnumerable<T> existing);
    T NewItem(IEnumerable<T> existing);
}

public sealed class MemoryStore<T> : IStore<T> where T : class, new()
{
    public static readonly List<T> Rows = new();
    public List<T> GetAll() => Rows.ToList();
    public bool SaveAll(List<T> all) { Rows.Clear(); Rows.AddRange(all); return true; }
    public int NextId(IEnumerable<T> existing) => Rows.Count + 1;
    public T NewItem(IEnumerable<T> existing) => new();
}

public static class StoreFactory
{
    public static IStore<T> Create<T>() where T : class, new() => new MemoryStore<T>();
}

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}
""";

    /// <summary>The two view-model files every screen here builds on.</summary>
    internal static string ObservableAndCommand
        => ViewModelTemplates.ObservableObject("Acme").Replace("namespace Acme.ViewModels;", string.Empty)
         + Environment.NewLine
         + ViewModelTemplates.PortableRelayCommand("Acme").Replace("namespace Acme.ViewModels;", string.Empty);

    internal static Assembly Emit(params string[] extra)
    {
        var source = EmittedSource.Flatten(
            [
                StoreAndModelStub,
                ObservableAndCommand,
                Editor(validation: false),
                .. extra,
            ]);

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    /// <summary>The emitted editor, stripped of the namespaces the flattened source has none of.</summary>
    internal static string Editor(bool validation)
        => RecordEditorTemplate.Source("Acme", validation)
            .Replace("namespace Acme.ViewModels;", string.Empty)
            .Replace("using Acme.Data;", string.Empty)
            .Replace("using Acme.Validation;", string.Empty);

    /// <summary>
    /// The editor an app generated with validation on gets, over a validator that rejects a book
    /// with no title.
    /// </summary>
    private static Assembly EmitValidating()
    {
        var source = EmittedSource.Flatten(StoreAndModelStub, ValidatorStub, ObservableAndCommand, Editor(validation: true));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    /// <summary>The validation seam the generated app really has: an IValidator&lt;T&gt; resolved
    /// by a ValidatorFactory, degrading to no rules for an unmapped type.</summary>
    private const string ValidatorStub = """
using System;
using System.Collections.Generic;

public interface IValidator<T> { IReadOnlyList<string> Validate(T item); }

public sealed class BookValidator : IValidator<Book>
{
    public IReadOnlyList<string> Validate(Book item)
        => string.IsNullOrWhiteSpace(item.Title)
            ? new[] { "Title is required." }
            : Array.Empty<string>();
}

public static class ValidatorFactory
{
    public static IValidator<T> Create<T>() where T : class, new()
    {
        if (typeof(T) == typeof(Book))
            return (IValidator<T>)(object)new BookValidator();

        return new NullValidator<T>();
    }
}

internal sealed class NullValidator<T> : IValidator<T>
{
    public IReadOnlyList<string> Validate(T item) => Array.Empty<string>();
}
""";

    internal static System.Collections.IList Rows(Assembly assembly)
        => (System.Collections.IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(assembly.GetType("Book")!)
            .GetField("Rows")!.GetValue(null)!;

    private static (object Editor, object Original) OpenExisting(Assembly assembly)
    {
        var bookType = assembly.GetType("Book")!;
        var book = Activator.CreateInstance(bookType)!;

        bookType.GetProperty("Id")!.SetValue(book, 1);
        bookType.GetProperty("Title")!.SetValue(book, "Original");

        var rows = Rows(assembly);
        rows.Clear();
        rows.Add(book);

        var editorType = assembly.GetType("RecordEditorViewModel`1")!.MakeGenericType(bookType);

        return (Activator.CreateInstance(editorType, [book, false])!, book);
    }

    private static object Draft(object editor)
        => editor.GetType().GetProperty("Draft")!.GetValue(editor)!;

    private static void SetTitle(object record, string value)
        => record.GetType().GetProperty("Title")!.SetValue(record, value);

    private static string GetTitle(object record)
        => (string)record.GetType().GetProperty("Title")!.GetValue(record)!;

    internal static void Execute(object model, string command)
    {
        var value = model.GetType().GetProperty(command)!.GetValue(model)!;
        value.GetType().GetMethod("Execute")!.Invoke(value, [null]);
    }

    /// <summary>The point of the whole design. Editing the record itself would make Cancel a
    /// lie — the change would already be in the list behind the editor.</summary>
    [TestMethod]
    public void The_editor_works_on_a_copy_not_the_record()
    {
        var (editor, original) = OpenExisting(Emit());

        Assert.AreNotSame(original, Draft(editor));
        Assert.AreEqual("Original", GetTitle(Draft(editor)));
    }

    [TestMethod]
    public void Cancel_leaves_the_record_untouched()
    {
        var (editor, original) = OpenExisting(Emit());

        SetTitle(Draft(editor), "Changed");
        Execute(editor, "CancelCommand");

        Assert.AreEqual("Original", GetTitle(original));
    }

    [TestMethod]
    public void Save_writes_the_change_through_to_the_store()
    {
        var assembly = Emit();
        var (editor, _) = OpenExisting(assembly);

        SetTitle(Draft(editor), "Changed");
        Execute(editor, "SaveCommand");

        Assert.AreEqual("Changed", GetTitle(Rows(assembly)[0]!));
    }

    /// <summary>The shell asks this before letting anyone navigate away.</summary>
    [TestMethod]
    public void An_untouched_editor_is_not_dirty_and_an_edited_one_is()
    {
        var (editor, _) = OpenExisting(Emit());

        Assert.IsFalse((bool)editor.GetType().GetProperty("IsDirty")!.GetValue(editor)!);

        SetTitle(Draft(editor), "Changed");

        Assert.IsTrue((bool)editor.GetType().GetProperty("IsDirty")!.GetValue(editor)!);
    }

    /// <summary>Saving clears it, or the discard prompt fires straight after a successful
    /// save.</summary>
    [TestMethod]
    public void Saving_clears_the_dirty_flag()
    {
        var (editor, _) = OpenExisting(Emit());

        SetTitle(Draft(editor), "Changed");
        Execute(editor, "SaveCommand");

        Assert.IsFalse((bool)editor.GetType().GetProperty("IsDirty")!.GetValue(editor)!);
    }

    [TestMethod]
    public void Both_commands_raise_Closed_so_the_shell_can_go_back()
    {
        var (editor, _) = OpenExisting(Emit());

        var raised = 0;
        editor.GetType().GetEvent("Closed")!
            .AddEventHandler(editor, new EventHandler((_, _) => raised++));

        Execute(editor, "CancelCommand");

        Assert.AreEqual(1, raised);
    }

    /// <summary>A new record gets an id from the store and joins the list.</summary>
    [TestMethod]
    public void A_new_record_is_added_on_save()
    {
        var assembly = Emit();
        Rows(assembly).Clear();

        var editorType = assembly.GetType("RecordEditorViewModel`1")!
            .MakeGenericType(assembly.GetType("Book")!);

        var editor = Activator.CreateInstance(editorType, [null, true])!;

        SetTitle(Draft(editor), "Brand new");
        Execute(editor, "SaveCommand");

        Assert.HasCount(1, Rows(assembly));
        Assert.AreEqual("Brand new", GetTitle(Rows(assembly)[0]!));
    }

    /// <summary>
    /// The dirty check has to be reachable without knowing T.
    /// </summary>
    /// <remarks>
    /// The shell holds the current screen as <c>object</c>, so <c>is RecordEditorViewModel&lt;T&gt;</c>
    /// is not available to it — and <c>is RecordEditorViewModel&lt;object&gt;</c> compiles and is
    /// always false. A non-generic interface is the only way the shell can ask.
    /// </remarks>
    [TestMethod]
    public void The_editor_exposes_its_dirty_state_through_a_non_generic_interface()
    {
        var (editor, _) = OpenExisting(Emit());

        var contract = editor.GetType().GetInterface("IRecordEditor");

        Assert.IsNotNull(contract, "the shell cannot ask a closed generic whether it is dirty");
        Assert.IsFalse((bool)contract!.GetProperty("IsDirty")!.GetValue(editor)!);
    }

    /// <summary>
    /// The editor runs the generated validators, because the layout it replaces did.
    /// </summary>
    /// <remarks>
    /// This path writes through the store, not through <c>Repository&lt;T&gt;</c>, so nothing else
    /// would run them: the business shell would otherwise accept records the tabs layout rejects,
    /// from the same model, in the same application.
    /// </remarks>
    [TestMethod]
    public void A_record_the_validator_rejects_is_not_saved()
    {
        var assembly = EmitValidating();
        var (editor, _) = OpenExisting(assembly);

        SetTitle(Draft(editor), string.Empty);
        Execute(editor, "SaveCommand");

        Assert.AreEqual("Original", GetTitle(Rows(assembly)[0]!), "the store took a rejected record");
        StringAssert.Contains(Status(editor), "Title is required.");
    }

    /// <summary>A rejected save must not close the editor: the user has to be able to fix the
    /// field they were told about.</summary>
    [TestMethod]
    public void A_rejected_save_leaves_the_editor_open_and_dirty()
    {
        var assembly = EmitValidating();
        var (editor, _) = OpenExisting(assembly);

        var closed = 0;
        editor.GetType().GetEvent("Closed")!
            .AddEventHandler(editor, new EventHandler((_, _) => closed++));

        SetTitle(Draft(editor), string.Empty);
        Execute(editor, "SaveCommand");

        Assert.AreEqual(0, closed);
        Assert.IsTrue((bool)editor.GetType().GetProperty("IsDirty")!.GetValue(editor)!);
    }

    [TestMethod]
    public void A_record_the_validator_accepts_still_saves()
    {
        var assembly = EmitValidating();
        var (editor, _) = OpenExisting(assembly);

        SetTitle(Draft(editor), "Changed");
        Execute(editor, "SaveCommand");

        Assert.AreEqual("Changed", GetTitle(Rows(assembly)[0]!));
    }

    /// <summary>An app generated with validation off has no Validation folder to resolve a
    /// validator from, so naming one would not compile.</summary>
    [TestMethod]
    public void An_app_with_no_validators_resolves_none()
    {
        var source = RecordEditorTemplate.Source("Acme", validation: false);

        Assert.DoesNotContain("ValidatorFactory", source);
        Assert.DoesNotContain("Acme.Validation", source);

        StringAssert.Contains(RecordEditorTemplate.Source("Acme", validation: true), "ValidatorFactory.Create<T>()");
    }

    private static string Status(object editor)
        => (string)editor.GetType().GetProperty("Status")!.GetValue(editor)!;
}

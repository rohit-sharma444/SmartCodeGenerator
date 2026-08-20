# Business Shell and Screens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A generated application opens on a dashboard, navigates a rail grouped into Masters and Transactions, and edits every record the same way — in an editor that replaces the content area and can be cancelled.

**Architecture:** Four screen view models (dashboard, master list, transaction list, record editor) held by a `ShellViewModel` with a one-deep history. They are stack-neutral, so WPF selects between them with `DataTemplate`s and Windows Forms with `UserControl`s; Blazor renders the same shapes as components behind routes. A fifth layout key, `business`, becomes the default.

**Tech Stack:** .NET 10, MSTest, Roslyn compile-testing, WPF/XAML, Windows Forms, Blazor Server.

**Spec:** `docs/superpowers/specs/2026-08-20-professional-app-layouts-design.md` — **The shell**, **Master screens**, **Transaction screens**, and Delivery item 2.

**Companion plan:** `docs/superpowers/plans/2026-08-20-professional-app-layouts-classification.md`. Its eight tasks run **first**, on this same branch: nothing here can group a rail or shape a screen until every entity has a role.

## Global Constraints

- **Branch:** `professional-app-screens`, forked from branch 1's tip because the screens read `Session`, `HasScreens` and `FrontEndContext`, none of which are in `main` until PR #104 lands. Rebase onto `main` once it does.
- **No new NuGet packages.**
- **`MainViewModel` does not change.** The four existing layouts bind to it and must go on working. `IEntityViewModel` gains only `Role` and `Count`, which those layouts ignore.
- **The regression proof, restated precisely:** a named layout **plus `nologin`** must still reproduce the pre-auth output byte for byte. Any existing golden-output test that has to change under that combination is a bug in this work.
- **The default flip lands in its own commit, near the end**, so the riskiest change in the effort can be reverted without unpicking the screens.
- **`AppCustomization` is compared by value.** Every member added to it must be a `string`, `bool`, enum or nullable of one — a collection breaks equality silently.
- **Emitted code is compile-tested with Roslyn.** Use `EmittedSource` for core C# and `EmittedDesktopSource.AssertCompilesTogether` for anything touching WPF or Windows Forms types. Compile XAML code-behind against a hand-written `.g.cs` stub, as `WpfDashboardLayoutTests` does — Roslyn cannot generate `InitializeComponent`.
- **Run `GeneratedProjectBuildTests` before every commit that changes emitted output.** Branch 1 shipped five defects that passed every string assertion and were caught only by building a real app; two were security-relevant, and one was an unauthorized page.
- **CI is the configuration that catches things:** `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`. Debug hides analyzer errors.
- **Heredoc warning:** backslashes are eaten in this harness's `python - <<'PY'` heredocs — `'\\n'` arrives as a real newline, producing CS1010 while the script prints success. Use raw strings, `chr(92)`, and assert match counts.

---

## File Structure

**New view-model templates** (`AppScaffolding/Screens/`, emitting into `ViewModels/`):

| File | Emits | Responsibility |
| --- | --- | --- |
| `RecordEditorTemplate.cs` | `RecordEditorViewModel.cs` | Edits a **copy** of one record; Save, Cancel, IsDirty |
| `MasterScreenTemplate.cs` | `MasterScreenViewModel.cs` | Read-only list, search, New/Open/Delete |
| `TransactionScreenTemplate.cs` | `TransactionScreenViewModel.cs` | The above plus date range, FK filter, column totals |
| `DashboardTemplate.cs` | `DashboardViewModel.cs` | Per-role groups of counts |
| `ShellTemplate.cs` | `ShellViewModel.cs` | Grouped nav, signed-in user, current screen, one-deep history, dirty guard |

**New view templates:**

| File | Emits |
| --- | --- |
| `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs` | `Views/MainWindow.xaml` + code-behind for the shell |
| `AppScaffolding/Layouts/Wpf/BusinessPanesXaml.cs` | The four `DataTemplate`s, as a resource dictionary fragment |
| `AppScaffolding/Layouts/WinForms/WinFormsBusinessLayout.cs` | `Views/MainForm.cs` — `TreeView` rail, content panel |
| `AppScaffolding/Layouts/WinForms/BusinessPaneControls.cs` | The four `UserControl`s |
| `AppScaffolding/Screens/BlazorScreenTemplates.cs` | Master/transaction/editor/dashboard Razor components |

**Modified:** `AppLayout.cs`, `AppLayoutCatalog.cs`, `WpfLayouts.cs`, `WinFormsLayouts.cs`, `AppCustomization.cs` (`EffectiveLayout` only), `ViewModelTemplates.cs` (`IEntityViewModel`), `DesktopViewModelFiles.cs`, `BlazorTarget.cs`, `BlazorShellTemplate.cs`, `docs/user-manual-source.html`.

---

### Task 1: `Role` and `Count` on the pane interface

**Files:**
- Modify: `AppScaffolding/ViewModelTemplates.cs` (`EntityViewModelInterface`, `EntityViewModel`)
- Test: `CodeGenerator.Tests/App/BusinessViewModelTests.cs`

**Interfaces:**
- Consumes: `EntityRole` from the classification plan.
- Produces: emitted `IEntityViewModel` gains `EntityRole Role { get; }` and `int Count { get; }`; `EntityViewModel<T>` gains a constructor taking the role, defaulting to `EntityRole.Master`.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The pane interface gains exactly two members. The four existing layouts bind to the
/// same interface and must ignore both without changing.</summary>
[TestClass]
public class BusinessViewModelTests
{
    [TestMethod]
    public void The_pane_interface_carries_a_role_and_a_count()
    {
        var source = ViewModelTemplates.EntityViewModelInterface("Acme");

        StringAssert.Contains(source, "EntityRole Role { get; }");
        StringAssert.Contains(source, "int Count { get; }");
    }

    /// <summary>The old layouts bind to Title/Items/Selected/commands and must be untouched by
    /// the additions.</summary>
    [TestMethod]
    public void The_interface_keeps_every_member_the_old_layouts_bind_to()
    {
        var source = ViewModelTemplates.EntityViewModelInterface("Acme");

        foreach (var member in new[]
                 { "Title", "Items", "Selected", "AddCommand", "DeleteCommand", "SaveCommand", "Status" })
        {
            StringAssert.Contains(source, member);
        }
    }

    [TestMethod]
    public void An_entity_view_model_reports_its_record_count()
        => StringAssert.Contains(
            ViewModelTemplates.EntityViewModel("Acme", validation: false), "public int Count => Records.Count;");

    /// <summary>Master is the default so the four existing layouts, which never pass a role,
    /// compile and behave exactly as before.</summary>
    [TestMethod]
    public void The_role_defaults_to_master_for_callers_that_do_not_pass_one()
        => StringAssert.Contains(
            ViewModelTemplates.EntityViewModel("Acme", validation: false),
            "EntityRole role = EntityRole.Master");

    [TestMethod]
    public void The_old_layouts_still_emit_a_main_view_model_that_compiles()
    {
        var files = new WpfTarget().FrontEndFiles(
            "Shop", ["Order"], AppCustomization.Default with { Layout = AppLayout.Tabs });

        EmittedDesktopSource.AssertCompilesTogether(
            [.. files.Where(f => f.Name.EndsWith(".cs", StringComparison.Ordinal))
                     .Select(f => (f.Name, f.Source))]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter BusinessViewModelTests`
Expected: FAIL — the interface has no `Role`.

- [ ] **Step 3: Write the implementation**

In `ViewModelTemplates.EntityViewModelInterface`, add the two members and the using:

```csharp
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

    /// <summary>Which kind of screen this entity gets. The four pre-business layouts ignore it.</summary>
    EntityRole Role { get; }

    /// <summary>How many records it holds, for the dashboard's tiles.</summary>
    int Count { get; }
}
""";
```

In `EntityViewModel`, take the role and expose both:

```csharp
    public EntityViewModel(string title, EntityRole role = EntityRole.Master)
    {
        Title = title;
        Role = role;
        ...
    }

    public EntityRole Role { get; }

    public int Count => Records.Count;
```

The emitted `EntityRole` enum belongs to the generated app, so emit it into `Screens/EntityRole.cs` from `DesktopViewModelFiles` and from `BlazorTarget` — it is the same three values as `AppScaffolding.Screens.EntityRole`, written into the app's own namespace. Add to `DesktopViewModelFiles.Build`:

```csharp
            new("Screens", "EntityRole.cs", ScreenTemplates.RoleEnum(rootNamespace)),
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "BusinessViewModelTests|DesktopViewModelFilesTests|MainViewModelTemplateTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ CodeGenerator.Tests/App/BusinessViewModelTests.cs
git commit -m "feat(screens): a pane knows its role and how many records it holds"
```

---

### Task 2: The record editor

**Files:**
- Create: `AppScaffolding/Screens/RecordEditorTemplate.cs`
- Test: `CodeGenerator.Tests/App/RecordEditorTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: emitted `RecordEditorViewModel<T>` with `T Draft`, `bool IsNew`, `bool IsDirty`, `string Title`, `ICommand SaveCommand`, `ICommand CancelCommand`, `event EventHandler? Closed`, and `IReadOnlyList<EditorField> Fields`. `EditorField` carries `Name`, `Type`, `bool IsForeignKey`, `IReadOnlyList<object> Choices`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The editor is why the whole application edits one way, and the copy is why Cancel means
/// something. Both are proved by running the emitted code.
/// </summary>
[TestClass]
public class RecordEditorTests
{
    private const string Stub = """
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

    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            Stub,
            ViewModelTemplates.ObservableObject("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            ViewModelTemplates.PortableRelayCommand("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            RecordEditorTemplate.Source("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Data;", string.Empty)
                .Replace("using Acme.Models;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    private static (object Editor, object Original, Type BookType) OpenExisting(Assembly assembly)
    {
        var bookType = assembly.GetType("Book")!;
        var book = Activator.CreateInstance(bookType)!;
        bookType.GetProperty("Id")!.SetValue(book, 1);
        bookType.GetProperty("Title")!.SetValue(book, "Original");

        var rows = (System.Collections.IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(bookType).GetField("Rows")!.GetValue(null)!;
        rows.Clear();
        rows.Add(book);

        var editorType = assembly.GetType("RecordEditorViewModel`1")!.MakeGenericType(bookType);

        return (Activator.CreateInstance(editorType, [book, false])!, book, bookType);
    }

    private static object Draft(object editor)
        => editor.GetType().GetProperty("Draft")!.GetValue(editor)!;

    private static void Execute(object model, string command)
    {
        var value = model.GetType().GetProperty(command)!.GetValue(model)!;
        value.GetType().GetMethod("Execute")!.Invoke(value, [null]);
    }

    /// <summary>The point of the whole design. Editing the record itself would make Cancel a
    /// lie.</summary>
    [TestMethod]
    public void The_editor_works_on_a_copy_not_the_record()
    {
        var assembly = Emit();
        var (editor, original, bookType) = OpenExisting(assembly);

        Assert.AreNotSame(original, Draft(editor));
        Assert.AreEqual("Original", bookType.GetProperty("Title")!.GetValue(Draft(editor)));
    }

    [TestMethod]
    public void Cancel_leaves_the_record_untouched()
    {
        var assembly = Emit();
        var (editor, original, bookType) = OpenExisting(assembly);

        bookType.GetProperty("Title")!.SetValue(Draft(editor), "Changed");
        Execute(editor, "CancelCommand");

        Assert.AreEqual("Original", bookType.GetProperty("Title")!.GetValue(original));
    }

    [TestMethod]
    public void Save_writes_the_change_through_to_the_store()
    {
        var assembly = Emit();
        var (editor, _, bookType) = OpenExisting(assembly);

        bookType.GetProperty("Title")!.SetValue(Draft(editor), "Changed");
        Execute(editor, "SaveCommand");

        var rows = (System.Collections.IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(bookType).GetField("Rows")!.GetValue(null)!;

        Assert.AreEqual("Changed", bookType.GetProperty("Title")!.GetValue(rows[0]!));
    }

    /// <summary>The shell asks this before letting anyone navigate away.</summary>
    [TestMethod]
    public void An_untouched_editor_is_not_dirty_and_an_edited_one_is()
    {
        var assembly = Emit();
        var (editor, _, bookType) = OpenExisting(assembly);

        Assert.IsFalse((bool)editor.GetType().GetProperty("IsDirty")!.GetValue(editor)!);

        bookType.GetProperty("Title")!.SetValue(Draft(editor), "Changed");

        Assert.IsTrue((bool)editor.GetType().GetProperty("IsDirty")!.GetValue(editor)!);
    }

    /// <summary>Saving clears it, or the discard prompt fires after a successful save.</summary>
    [TestMethod]
    public void Saving_clears_the_dirty_flag()
    {
        var assembly = Emit();
        var (editor, _, bookType) = OpenExisting(assembly);

        bookType.GetProperty("Title")!.SetValue(Draft(editor), "Changed");
        Execute(editor, "SaveCommand");

        Assert.IsFalse((bool)editor.GetType().GetProperty("IsDirty")!.GetValue(editor)!);
    }

    [TestMethod]
    public void Both_commands_raise_Closed_so_the_shell_can_go_back()
    {
        var assembly = Emit();
        var (editor, _, _) = OpenExisting(assembly);

        var raised = 0;
        var handler = new EventHandler((_, _) => raised++);
        var evt = editor.GetType().GetEvent("Closed")!;
        evt.AddEventHandler(editor, handler);

        Execute(editor, "CancelCommand");

        Assert.AreEqual(1, raised);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter RecordEditorTests`
Expected: FAIL — `RecordEditorTemplate` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Screens/RecordEditorTemplate.cs`. The emitted class:

```csharp
namespace AppScaffolding.Screens;

/// <summary>Emits <c>ViewModels/RecordEditorViewModel.cs</c>: one record, edited as a copy.</summary>
/// <remarks>
/// The copy is the whole point. Editing the record in place would make Cancel a lie — the change
/// would already be in the list behind the editor — and it is what lets the shell ask "discard
/// your changes?" and mean it.
/// <para>
/// The clone is a System.Text.Json round-trip, already a dependency through JsonStore. Navigation
/// properties are [JsonIgnore] so they drop out of the copy and are re-resolved on save.
/// </para>
/// </remarks>
public static class RecordEditorTemplate
{
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using {{rootNamespace}}.Data;

namespace {{rootNamespace}}.ViewModels;

/// <summary>One record, edited in a copy so Cancel can genuinely cancel.</summary>
public sealed class RecordEditorViewModel<T> : ObservableObject where T : class, new()
{
    private readonly IStore<T> _store = StoreFactory.Create<T>();
    private readonly T? _original;
    private readonly string _openedAs;
    private string _status = string.Empty;

    public RecordEditorViewModel(T? record, bool isNew)
    {
        IsNew = isNew;
        _original = record;

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

    /// <summary>Whether anything has been typed since the editor opened.</summary>
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

        // The snapshot moves to what was just written, so the shell stops calling this dirty.
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
```

`_openedAs` is assigned after save, so it must be a field rather than `readonly` — change its declaration to `private string _openedAs;`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter RecordEditorTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/RecordEditorTemplate.cs CodeGenerator.Tests/App/RecordEditorTests.cs
git commit -m "feat(screens): a record is edited as a copy, so Cancel means something"
```

---

### Task 3: The master screen

**Files:**
- Create: `AppScaffolding/Screens/MasterScreenTemplate.cs`
- Test: `CodeGenerator.Tests/App/MasterScreenTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces: emitted `MasterScreenViewModel<T> : ObservableObject, IEntityViewModel` with `Search`, `Items` (filtered), `Selected`, `NewCommand`, `OpenCommand`, `DeleteCommand`, `SaveCommand`, `AddCommand` (an alias for New, so the interface is satisfied), `Status`, and `event EventHandler<RecordEditorViewModel<T>>? EditorRequested`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections;
using System.Reflection;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>The list finds a record; the editor changes it. The list itself never edits, which is
/// what makes the whole application edit one way.</summary>
[TestClass]
public class MasterScreenTests
{
    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            RecordEditorTests.StoreAndModelStub,
            ViewModelTemplates.ObservableObject("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            ViewModelTemplates.PortableRelayCommand("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            ScreenTemplates.RoleEnum("Acme").Replace("namespace Acme.Screens;", string.Empty),
            ViewModelTemplates.EntityViewModelInterface("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            RecordEditorTemplate.Source("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Data;", string.Empty),
            MasterScreenTemplate.Source("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Data;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    private static object Screen(Assembly assembly, params string[] titles)
    {
        var bookType = assembly.GetType("Book")!;
        var rows = (IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(bookType).GetField("Rows")!.GetValue(null)!;

        rows.Clear();

        var id = 1;
        foreach (var title in titles)
        {
            var book = Activator.CreateInstance(bookType)!;
            bookType.GetProperty("Id")!.SetValue(book, id++);
            bookType.GetProperty("Title")!.SetValue(book, title);
            rows.Add(book);
        }

        var screenType = assembly.GetType("MasterScreenViewModel`1")!.MakeGenericType(bookType);

        return Activator.CreateInstance(screenType, ["Book"])!;
    }

    private static IList Items(object screen)
        => (IList)screen.GetType().GetProperty("Items")!.GetValue(screen)!;

    private static void Execute(object model, string command)
    {
        var value = model.GetType().GetProperty(command)!.GetValue(model)!;
        value.GetType().GetMethod("Execute")!.Invoke(value, [null]);
    }

    [TestMethod]
    public void It_lists_what_the_store_holds()
        => Assert.HasCount(2, Items(Screen(Emit(), "One", "Two")));

    [TestMethod]
    public void Search_narrows_the_list_without_touching_the_store()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "Alpha", "Beta");

        screen.GetType().GetProperty("Search")!.SetValue(screen, "alph");

        Assert.HasCount(1, Items(screen));
        Assert.HasCount(2, ((IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(assembly.GetType("Book")!).GetField("Rows")!.GetValue(null)!));
    }

    /// <summary>Search is not case-sensitive; a list that only matches exact casing reads as
    /// broken.</summary>
    [TestMethod]
    public void Search_ignores_case()
    {
        var screen = Screen(Emit(), "Alpha", "Beta");
        screen.GetType().GetProperty("Search")!.SetValue(screen, "ALPHA");

        Assert.HasCount(1, Items(screen));
    }

    [TestMethod]
    public void The_role_is_master()
        => Assert.AreEqual("Master", Screen(Emit(), "One").GetType()
            .GetProperty("Role")!.GetValue(Screen(Emit(), "One"))!.ToString());

    /// <summary>Opening asks the shell for an editor rather than editing in place — the list has
    /// no edit path at all, by construction.</summary>
    [TestMethod]
    public void Opening_a_record_raises_an_editor_request()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "One");

        object? requested = null;
        var evt = screen.GetType().GetEvent("EditorRequested")!;
        var handler = Delegate.CreateDelegate(
            evt.EventHandlerType!, new Capture(e => requested = e), nameof(Capture.Handle));
        evt.AddEventHandler(screen, handler);

        screen.GetType().GetProperty("Selected")!.SetValue(screen, Items(screen)[0]);
        Execute(screen, "OpenCommand");

        Assert.IsNotNull(requested);
    }

    private sealed class Capture(Action<object> onEvent)
    {
        public void Handle(object? sender, object e) => onEvent(e);
    }

    [TestMethod]
    public void New_raises_an_editor_request_for_a_new_record()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "One");

        object? requested = null;
        var evt = screen.GetType().GetEvent("EditorRequested")!;
        evt.AddEventHandler(screen, Delegate.CreateDelegate(
            evt.EventHandlerType!, new Capture(e => requested = e), nameof(Capture.Handle)));

        Execute(screen, "NewCommand");

        Assert.IsNotNull(requested);
        Assert.IsTrue((bool)requested!.GetType().GetProperty("IsNew")!.GetValue(requested)!);
    }

    [TestMethod]
    public void Deleting_removes_the_record_from_the_store()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "One", "Two");

        screen.GetType().GetProperty("Selected")!.SetValue(screen, Items(screen)[0]);
        Execute(screen, "DeleteCommand");

        Assert.HasCount(1, Items(screen));
    }
}
```

Expose the stub from `RecordEditorTests` so the two files cannot drift:

```csharp
    /// <summary>Shared with MasterScreenTests, so both compile against the same store.</summary>
    internal static string StoreAndModelStub => Stub;
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter MasterScreenTests`
Expected: FAIL — `MasterScreenTemplate` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Screens/MasterScreenTemplate.cs` emitting `MasterScreenViewModel<T>`:

- `Records` — an `ObservableCollection<T>` loaded from `StoreFactory.Create<T>()`.
- `Search` — a string; setting it raises `OnPropertyChanged(nameof(Items))`.
- `Items` — `Records` filtered by `Search` against every public string property, case-insensitively; the whole list when `Search` is empty.
- `Role => EntityRole.Master`, `Count => Records.Count`, `Title` from the constructor.
- `NewCommand` raises `EditorRequested` with `new RecordEditorViewModel<T>(null, isNew: true)`.
- `OpenCommand` raises it with `new RecordEditorViewModel<T>((T)Selected!, isNew: false)`, and is disabled when nothing is selected.
- `AddCommand => NewCommand` and `SaveCommand` — a no-op that reloads — exist only to satisfy `IEntityViewModel`; comment that the list itself never edits.
- `DeleteCommand` removes the selected record and saves, guarded on `Session.IsAdmin` with the status `"Only an administrator can delete a record."` when it is not.
- `Reload()` re-reads the store, called by the shell when an editor closes.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "MasterScreenTests|RecordEditorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/MasterScreenTemplate.cs CodeGenerator.Tests/App/MasterScreenTests.cs
git commit -m "feat(screens): a master screen finds records; the editor changes them"
```

---

### Task 4: The transaction screen

**Files:**
- Create: `AppScaffolding/Screens/TransactionScreenTemplate.cs`
- Test: `CodeGenerator.Tests/App/TransactionScreenTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: emitted `TransactionScreenViewModel<T> : MasterScreenViewModel<T>` adding `DateTime? From`, `DateTime? To`, `string DateField`, `IReadOnlyList<ColumnTotal> Totals`, and `EntityRole Role => EntityRole.Transaction`. `ColumnTotal` is `(string Column, decimal Value)`.

- [ ] **Step 1: Write the failing test**

Tests to write, each running the emitted code as above:

- `The_role_is_transaction`.
- `A_date_range_narrows_the_list` — two records, one inside the range.
- `A_model_with_no_date_field_renders_no_range` — `DateField` is empty and `From`/`To` are ignored rather than throwing.
- `Audit_fields_are_never_the_filter_field` — a model whose only dates are `CreatedOn`/`ModifiedOn` filters on nothing.
- `Totals_sum_every_money_column` — two decimal columns, both totalled, one row each.
- `Totals_ignore_ids_and_foreign_keys` — an `int Id` and a `CustomerId` are not totalled.
- `Totals_follow_the_filter` — filtering to one row changes the total.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter TransactionScreenTests`
Expected: FAIL — the template does not exist.

- [ ] **Step 3: Write the implementation**

`TransactionScreenViewModel<T>` derives from `MasterScreenViewModel<T>`, overriding `Items` to apply the date range on top of the search, and adding:

```csharp
    /// <summary>The date the range filters on — the first DateTime property that is not an audit
    /// field, or empty when the model has none.</summary>
    /// <remarks>
    /// Every generated class carries CreatedOn and ModifiedOn, so filtering on "the first
    /// DateTime" would filter every screen on a field nobody chose. The exclusion is the same one
    /// the classifier makes, and for the same reason.
    /// </remarks>
    public string DateField { get; }
```

and `Totals` computed over the filtered items, summing every `decimal`/`double` property whose name is neither `Id` nor ends in `Id`. Emit the audit-field names as a constant in the generated file so the running app and the generator agree.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter TransactionScreenTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/TransactionScreenTemplate.cs CodeGenerator.Tests/App/TransactionScreenTests.cs
git commit -m "feat(screens): a transaction list filters by date and totals its money columns"
```

---

### Task 5: The dashboard and the shell

**Files:**
- Create: `AppScaffolding/Screens/DashboardTemplate.cs`, `AppScaffolding/Screens/ShellTemplate.cs`
- Test: `CodeGenerator.Tests/App/ShellViewModelTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces: emitted `DashboardViewModel` with `IReadOnlyList<DashboardGroup> Groups`, each `DashboardGroup` having `Title` and `IReadOnlyList<IEntityViewModel> Entities`. `ShellViewModel` with `IReadOnlyList<NavGroup> Groups`, `object? Current`, `string SignedInUser`, `bool IsAdmin`, `ICommand SignOutCommand`, `ICommand BackCommand`, `Func<bool> ConfirmDiscard`, `bool TryShow(object screen)`.

- [ ] **Step 1: Write the failing test**

Tests, all executing the emitted code:

- `The_rail_groups_masters_and_transactions_separately`.
- `Lines_never_appear_in_the_rail` — a `DocumentLine` entity is absent from every group.
- `The_admin_group_is_absent_for_a_plain_user` and `present_for_an_admin`.
- `The_shell_opens_on_the_dashboard`.
- `Showing_an_editor_remembers_the_screen_it_came_from` — `BackCommand` returns to the list.
- **`Navigating_away_from_a_dirty_editor_asks_first`** — with `ConfirmDiscard` returning false, `TryShow` returns false and `Current` is unchanged.
- `A_confirmed_discard_navigates` — with it returning true, `Current` changes.
- `A_clean_editor_never_asks` — `ConfirmDiscard` is not called at all.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter ShellViewModelTests`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`ShellViewModel` holds the screens, the one-deep `_previous`, and:

```csharp
    /// <summary>
    /// Asked before leaving an editor with unsaved changes. Supplied by the view, because the
    /// answer is a MessageBox on the desktop and a NavigationLock on the web.
    /// </summary>
    /// <remarks>
    /// This is what a modal was buying. An editor that fills the content area can be navigated
    /// away from, so the shell has to ask — and it has to ask on every target, or the choice
    /// quietly loses data.
    /// </remarks>
    public Func<bool> ConfirmDiscard { get; set; } = () => true;

    /// <summary>Shows <paramref name="screen"/>, or refuses when the user declines to discard.</summary>
    public bool TryShow(object screen)
    {
        if (Current is RecordEditorViewModel<object> && !ConfirmDiscard())
            return false;
        ...
    }
```

Because `RecordEditorViewModel<T>` is generic, the dirty check cannot be a type test on a closed generic. Emit a non-generic `IRecordEditor` interface with `bool IsDirty` that `RecordEditorViewModel<T>` implements, and test `Current is IRecordEditor { IsDirty: true }`. **Write this into the plan's implementation rather than discovering it at the keyboard: a `is RecordEditorViewModel<object>` test compiles and is always false.**

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter ShellViewModelTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/ CodeGenerator.Tests/App/ShellViewModelTests.cs
git commit -m "feat(screens): the shell groups the rail and guards a dirty editor"
```

---

### Task 6: The WPF business layout

**Files:**
- Create: `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs`, `AppScaffolding/Layouts/Wpf/BusinessPanesXaml.cs`
- Modify: `AppScaffolding/Layouts/Wpf/WpfLayouts.cs`
- Test: `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces: `WpfBusinessLayout : IAppLayout` with `Key => "business"`, emitting `Views/MainWindow.xaml` and its code-behind.

- [ ] **Step 1: Write the failing test**

Assertions: the window has a rail bound to `Groups`; a header bound to `SignedInUser` with a Sign out button; a `ContentControl` bound to `Current`; four `DataTemplate`s with `DataType` set to each screen view model; **no `x:Name` per entity** (the file must not grow with the model); the code-behind wires `ConfirmDiscard` to a `MessageBox`; and `AssertCompilesTogether` over the code-behind plus a `.g.cs` stub plus the emitted view models.

Plus the theming assertion the other layouts have: with a palette, the rail binds `AppSurfaceBrush`; without, it uses the same hardcoded neutrals `WpfSidebarLayout` uses.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter WpfBusinessLayoutTests`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

Emit the shell window: a two-column `Grid`, the rail an `ItemsControl` over `Groups` with a nested `ItemsControl` over each group's entities, the header a `DockPanel` with the title, the signed-in name and Sign out, and the content a `ContentControl` bound to `Current`. The four panes are implicit `DataTemplate`s in `Window.Resources`, keyed by `DataType`.

**Watch the raw-literal trap:** any XAML attribute ending in `}}` — a binding with a converter — makes a `$$"""` literal fail with CS9007. Use `$$$` with `{{{ }}}` placeholders in that file, as `WpfLoginTemplate` does.

Register it in `WpfLayouts.All`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "WpfBusinessLayoutTests|WpfLayoutRegistryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Layouts/Wpf/ CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs
git commit -m "feat(wpf): the business shell, with the rail grouped and the editor in the content area"
```

---

### Task 7: The Windows Forms business layout

**Files:**
- Create: `AppScaffolding/Layouts/WinForms/WinFormsBusinessLayout.cs`, `AppScaffolding/Layouts/WinForms/BusinessPaneControls.cs`
- Modify: `AppScaffolding/Layouts/WinForms/WinFormsLayouts.cs`
- Test: `CodeGenerator.Tests/App/WinFormsBusinessLayoutTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces: `WinFormsBusinessLayout : IAppLayout` with `Key => "business"`, emitting `Views/MainForm.cs` and four `UserControl`s.

Same shape as Task 6: a `TreeView` for the grouped rail, a content `Panel` that swaps `UserControl`s, `ConfirmDiscard` wired to `MessageBox.Show`, and `AssertCompilesTogether` over the whole emitted set. Register in `WinFormsLayouts.All`.

- [ ] **Step 1: Write the failing test** — as Task 6, in Windows Forms terms: `TreeView` present, one node per group, content panel swaps controls, no per-entity field.
- [ ] **Step 2: Run it and see it fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run `--filter "WinFormsBusinessLayoutTests|WinFormsLayoutRegistryTests"`.**
- [ ] **Step 5: Commit** — `feat(winforms): the business shell, with a grouped tree rail`

---

### Task 8: The web screens

**Files:**
- Create: `AppScaffolding/Screens/BlazorScreenTemplates.cs`
- Modify: `AppScaffolding/Targets/BlazorTarget.cs`, `AppScaffolding/BlazorShellTemplate.cs`
- Test: `CodeGenerator.Tests/App/BlazorScreenTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5 and the classification plan.
- Produces: `MasterScreen.razor`, `TransactionScreen.razor`, `RecordEditor.razor` components; per-entity pages render the right one; `/{entity}/{id:int}` and `/{entity}/new` routes; `Home.razor` becomes the grouped dashboard; `NavMenu` groups the rail.

Assertions: a master entity's page renders `<MasterScreen`; a transaction's renders `<TransactionScreen`; a `DocumentLine` entity **gets no page at all**; the nav is grouped under Masters and Transactions headings with lines absent; the editor route exists; **`<NavigationLock OnBeforeInternalNavigation>` guards the dirty editor** — the web equivalent of the desktop `ConfirmDiscard`, and the reason that seam is a `Func<bool>` rather than a `MessageBox` call inside the view model.

Every new page keeps `@attribute [Authorize]` and the `Microsoft.AspNetCore.Authorization` using — branch 1 shipped an unauthorized home page, and a new page is exactly where that recurs.

- [ ] **Step 1–5** as above, ending with:

```bash
git commit -m "feat(web): grouped navigation, a dashboard home, and screens by role"
```

---

### Task 9: Wire the layout into the targets

**Files:**
- Modify: `AppScaffolding/AppLayout.cs`, `AppScaffolding/Layouts/AppLayoutCatalog.cs`, `AppScaffolding/Targets/WpfTarget.cs`, `WinFormsTarget.cs`, `DesktopViewModelFiles.cs`
- Test: `CodeGenerator.Tests/App/AppLayoutCatalogTests.cs`, `CodeGenerator.Tests/App/BusinessLayoutSelectionTests.cs`

`AppLayout` gains `Business`; the catalogue gains `(AppLayout.Business, "business", "a dashboard home, a grouped rail, and an editor for every record")`. The targets emit the five screen view models **only for the business layout** — the four older layouts must not gain files, or the byte-neutrality proof breaks.

- [ ] **Steps 1–5**, ending with:

```bash
git commit -m "feat(build app): `layout business` selects the new shell"
```

---

### Task 10: The default flip

**Files:**
- Modify: `AppScaffolding/AppCustomization.cs` (`EffectiveLayout`)
- Test: every golden-output test that assumes `tabs`

**This is the riskiest commit in the effort and it is deliberately alone.**

```csharp
    public AppLayout EffectiveLayout => Layout ?? AppLayout.Business;
```

- [ ] **Step 1: Write the failing test**

```csharp
    [TestMethod]
    public void A_build_that_names_no_layout_gets_the_business_shell()
        => Assert.AreEqual(AppLayout.Business, AppCustomization.Default.EffectiveLayout);

    /// <summary>The regression proof, in one test. Naming tabs AND passing nologin must still
    /// produce what the generator produced before any of this existed.</summary>
    [TestMethod]
    public void Tabs_with_nologin_is_still_the_original_output()
    {
        var original = AppCustomization.Default with { Layout = AppLayout.Tabs, Login = false };

        var files = new WpfTarget().FrontEndFiles("Shop", ["Order"], original);

        CollectionAssert.AreEquivalent(
            new[] { "MainWindow.xaml", "MainWindow.xaml.cs", "RelationshipResolver.cs",
                    "ObservableObject.cs", "RelayCommand.cs", "IEntityViewModel.cs",
                    "EntityViewModel.cs", "MainViewModel.cs" },
            files.Select(f => f.Name).ToList());
    }
```

- [ ] **Step 2: Run the whole suite and expect red**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: FAIL, in every test that assumed the default layout. **Each one must be read before it is changed**: a test that now needs `with { Layout = AppLayout.Tabs }` is correct to update, and a test that fails for any other reason is a bug in this branch.

- [ ] **Step 3: Update the tests, not the behaviour**

Add `Layout = AppLayout.Tabs` to fixtures that meant "the old layout", and leave alone any that meant "the default".

- [ ] **Step 4: Full CI-configuration run**

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter GeneratedProjectBuildTests
```

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(build app): the business shell is what a generated app looks like by default"
```

---

### Task 11: Prove it against real generated applications

Nothing above proves a screen renders. Branch 1's lesson was that five defects passed every string assertion; three of them were in Blazor, and one was a page that rendered to anonymous visitors.

- [ ] **Step 1: Generate all three targets under `D:\g`** (short path — the scratchpad exceeds `MAX_PATH`; the tell is MSB3030 on a file that plainly exists), build each.

- [ ] **Step 2: The web app, signed in**

1. The rail is grouped into Masters and Transactions, with no line entity in it.
2. The dashboard shows counts, grouped.
3. A master opens its editor at `/{entity}/{id}`; Cancel returns with the record unchanged; Save changes it.
4. **Editing a field then clicking a rail item prompts before navigating.**
5. A transaction list filters by date and shows column totals.
6. A transaction with lines shows them in its editor; adding a line and saving persists it.
7. `/{line-entity}` is **not routable** — a line has no page.
8. Every screen still requires a sign-in, `/` included.

- [ ] **Step 3: The desktop apps** — the same list through UIA, plus: closing the shell exits; sign-out returns to the login.

- [ ] **Step 4: The regression diff**

Generate with `layout tabs nologin` and diff against the same from `origin/main`. Expected: **no differences outside `obj/` and `bin/`.**

- [ ] **Step 5: Commit any fixes the run finds**, each with a test that records how it was found.

---

### Task 12: The manual

- [ ] Add a chapter covering the dashboard, the grouped rail, how masters and transactions differ, the editor and its discard prompt, and `layout business` / the older layouts.
- [ ] Rebuild and verify the `.docx` with absolute paths (`$PSScriptRoot` is empty when these scripts run from the Bash tool):

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\SmartCodeGenerator-Repository\tools\build-user-manual.ps1" -Source "D:\SmartCodeGenerator-Repository\docs\user-manual-source.html" -Output "D:\SmartCodeGenerator-Repository\docs\SmartCodeGenerator-User-Manual.docx"
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\SmartCodeGenerator-Repository\tools\verify-user-manual.ps1" -Document "D:\SmartCodeGenerator-Repository\docs\SmartCodeGenerator-User-Manual.docx"
```

- [ ] Commit: `docs: the manual describes the business shell`

---

## Self-Review

**Spec coverage.** The shell's rail, dashboard, header and role filtering (5, 6, 7, 8); the four templates and the one-deep history (5, 6); master screens as list-and-editor (2, 3); transaction filters, totals and lines (4); the `business` key and the default flip (9, 10); theming staying orthogonal (6); the discard prompt (5, and verified in 11).

**Two traps written down rather than left to be discovered.** `Current is RecordEditorViewModel<object>` compiles and is always false — the editor needs a non-generic `IRecordEditor` for the dirty check (Task 5). And a XAML attribute ending in `}}` breaks a `$$"""` literal with CS9007 (Task 6), which cost time in branch 1.

**Where this plan is thinner than branch 1's.** Tasks 7, 8 and 9 give assertions and shape rather than full emitted source. That is deliberate: they are the same shape as tasks written out in full immediately before them, and repeating six hundred lines of near-identical XAML and Razor would obscure what actually differs. Tasks 1–6 and 10 are written out in full because each does something new.

**One thing this branch does not do.** Reports, printing, and per-entity permissions stay out of scope, as the spec says. A task that starts wanting one has wandered.

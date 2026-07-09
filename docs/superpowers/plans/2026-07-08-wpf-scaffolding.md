# WPF Scaffolding + Foldered Project Layout — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `build app:` generate a runnable WPF desktop application laid out in `Models/`, `Data/`, `ViewModels/`, `Views/` with namespaces that match the folders, replacing the flat console app.

**Architecture:** A single static `MainWindow.xaml` binds a `TabControl` to `ObservableCollection<IEntityViewModel>`, whose `ContentTemplate` holds one auto-column `DataGrid`. Entity names therefore appear in exactly one generated file, `MainViewModel.cs`. All templates are C# string templates owned by `AppScaffolding`; `RoslynClassGenerator` gains an additive optional namespace parameter.

**Tech Stack:** .NET 10, WPF (`net10.0-windows`, `UseWPF`), Roslyn (`Microsoft.CodeAnalysis.CSharp`), MSTest 4.0.2. No new NuGet dependencies — MVVM is hand-rolled.

**Spec:** `docs/superpowers/specs/2026-07-08-wpf-scaffolding-design.md`
**Branch:** `feat/build-app-wpf` (already created; the spec is committed on it)

## Global Constraints

- Repo root is `D:\SmartCodeGenerator-Repository`. All paths below are relative to it. The solution file lives at `SmartCodeGenerator/SmartCodeGenerator.slnx`.
- Generated-app target framework is passed as `net10.0`; `dotnet new wpf` resolves it to `net10.0-windows`. Do not hardcode `net10.0-windows` anywhere in the generator.
- The generated app has **one namespace root, `<AppName>`**. Every folder maps to a namespace under it: `Models/` → `<AppName>.Models`, `Data/` → `<AppName>.Data`, `ViewModels/` → `<AppName>.ViewModels`, `Views/` → `<AppName>.Views`.
- `RoslynClassGenerator.Generate`'s new parameter is **optional, defaulting to `"GeneratedCode"`**. The two non-scaffolding call sites (`CodeGenerationEngine/Service/CommandHandlingService.cs:486`, `CodeGenerator/App/CodeGeneratorStarter.cs:543`) must keep emitting byte-for-byte identical output.
- **No new NuGet packages.** No CommunityToolkit.Mvvm.
- The generated app is Windows-only. This is accepted, not a bug.
- Every file write in `AppScaffolding` keeps the existing best-effort contract: catch `IOException` / `UnauthorizedAccessException` per file, skip that file, continue, and report `n of m written`.
- Commit message style follows the repo: imperative sentence case, no `feat:` / `fix:` prefix. End every commit body with:
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  ```
- Run all tests with: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`
- **Before verifying anything through the CLI, rebuild `CodeGenerator`.** `dotnet test` leaves the CLI's `bin` stale, so a working change can look dead.

## File Structure

**Modified:**
- `CodeGenerationEngine/Core/RoslynClassGenerator.cs` — add optional `namespaceName` param (Task 1)
- `CodeGenerationEngine/Models/DotNetProjectType.cs` — unchanged; `Wpf` already exists and already maps to `dotnet new wpf`
- `AppScaffolding/JsonStoreTemplate.cs` — reshape to `GetAll`/`SaveAll`/`NextId`/`NewItem`, take a namespace (Task 2)
- `AppScaffolding/DomainModelMapper.cs` — refresh `ReservedNames` for the WPF templates (Task 5)
- `AppScaffolding/AppScaffolder.cs` — WPF project type, subfolders, fixup, template writes (Task 7)
- `CodeGenerator/App/BuildAppCommand.cs` — print paths relative to the project root (Task 8)

**Created (in `AppScaffolding/`):**
- `ViewModelTemplates.cs` — the four static MVVM sources: `ObservableObject`, `RelayCommand`, `IEntityViewModel`, `EntityViewModel<T>` (Task 3)
- `MainViewModelTemplate.cs` — the one entity-shaped source (Task 4)
- `MainWindowTemplate.cs` — `MainWindow.xaml` + `MainWindow.xaml.cs` (Task 4)
- `WpfProjectFixup.cs` — deletes the template's root `MainWindow.*`, rewrites `App.xaml`'s `StartupUri` (Task 6)

**Deleted:**
- `AppScaffolding/ProgramTemplate.cs` (Task 7)
- `CodeGenerator.Tests/App/ProgramTemplateTests.cs` (Task 7)

**Tests (in `CodeGenerator.Tests/`):**
- `Core/RoslynClassGeneratorTests.cs` (new, Task 1)
- `App/JsonStoreTemplateTests.cs` (rewritten, Task 2)
- `App/ViewModelTemplatesTests.cs` (new, Task 3)
- `App/MainViewModelTemplateTests.cs`, `App/MainWindowTemplateTests.cs` (new, Task 4)
- `App/DomainModelMapperTests.cs` (extended, Task 5)
- `App/WpfProjectFixupTests.cs` (new, Task 6)
- `App/AppScaffolderWriteTests.cs` (extended, Task 7)

Templates are split by *what varies*: `ViewModelTemplates` never varies by entity, `MainViewModelTemplate` always does, `MainWindowTemplate` is view-layer XAML+code-behind that changes together. `WpfProjectFixup` is separated from `AppScaffolder` precisely so it can be unit-tested without invoking `dotnet new`.

---

### Task 1: `RoslynClassGenerator` accepts a namespace

**Files:**
- Modify: `CodeGenerationEngine/Core/RoslynClassGenerator.cs:14-38`
- Test: `CodeGenerator.Tests/Core/RoslynClassGeneratorTests.cs` (create)

**Interfaces:**
- Consumes: `ClassDefinition` (from `CodeGenerationEngine.Models`), which has `string Name`, `List<PropertyDefinition> Properties`, `List<MethodDefinition> Methods`.
- Produces: `public static string Generate(ClassDefinition model, string namespaceName = "GeneratedCode")` — returns C# source with `namespace <namespaceName>`. Task 7 calls it with `$"{appName}.Models"`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/RoslynClassGeneratorTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class RoslynClassGeneratorTests
{
    private static ClassDefinition Employee() => new()
    {
        Name = "Employee",
        Properties = { new PropertyDefinition { Name = "Salary", Type = "decimal" } }
    };

    [TestMethod]
    public void Generate_WithoutNamespace_StillEmitsGeneratedCode()
    {
        var source = RoslynClassGenerator.Generate(Employee());

        StringAssert.Contains(source, "namespace GeneratedCode");
        StringAssert.Contains(source, "class Employee");
    }

    [TestMethod]
    public void Generate_WithNamespace_EmitsThatNamespace()
    {
        var source = RoslynClassGenerator.Generate(Employee(), "Payroll.Models");

        StringAssert.Contains(source, "namespace Payroll.Models");
        Assert.IsFalse(source.Contains("GeneratedCode"), "the default namespace must not leak in");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~RoslynClassGeneratorTests`

Expected: FAIL. `Generate_WithNamespace_EmitsThatNamespace` fails to compile — `Generate` takes 1 argument, not 2 (CS1501).

- [ ] **Step 3: Write minimal implementation**

In `CodeGenerationEngine/Core/RoslynClassGenerator.cs`, change the signature and the one `ParseName` call:

```csharp
    /// <summary>
    /// Generates C# source for <paramref name="model"/> in <paramref name="namespaceName"/>.
    /// The default keeps the historical namespace used by the engine's `add class` commands;
    /// app scaffolding passes a folder-matching namespace instead.
    /// </summary>
    public static string Generate(ClassDefinition model, string namespaceName = "GeneratedCode")
    {
```

and replace line 31-32:

```csharp
        var namespaceDeclaration = NamespaceDeclaration(
                ParseName(namespaceName)).AddMembers(classDeclaration);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~RoslynClassGeneratorTests`

Expected: PASS, 2 tests.

- [ ] **Step 5: Verify the other two call sites are untouched**

Run: `grep -rn "RoslynClassGenerator.Generate" --include=*.cs .`

Expected: exactly three hits — `AppScaffolding/AppScaffolder.cs`, `CodeGenerationEngine/Service/CommandHandlingService.cs`, `CodeGenerator/App/CodeGeneratorStarter.cs` — and the latter two pass a single argument, so they still emit `namespace GeneratedCode`.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`

Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Core/RoslynClassGenerator.cs CodeGenerator.Tests/Core/RoslynClassGeneratorTests.cs
git commit -m "$(cat <<'EOF'
Let RoslynClassGenerator emit a caller-chosen namespace

Scaffolded apps need entities in <AppName>.Models to match their folder.
The parameter is optional and defaults to GeneratedCode, so the engine's
`add class` call sites emit exactly what they did before.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Reshape `JsonStoreTemplate` for batch editing

**Files:**
- Modify: `AppScaffolding/JsonStoreTemplate.cs` (whole file)
- Test: `CodeGenerator.Tests/App/JsonStoreTemplateTests.cs` (rewrite)

**Interfaces:**
- Produces: `JsonStoreTemplate.Source(string namespaceName)` → the source of `JsonStore.cs`.
- The **generated** `JsonStore<T>` surface, relied on by Task 3's `EntityViewModel<T>`:
  - `List<T> GetAll()` — returns empty on missing/corrupt file
  - `bool SaveAll(List<T> all)` — false on IO failure, never throws
  - `int NextId(IEnumerable<T> existing)` — `max(Id) + 1`, or 1 when empty
  - `T NewItem(IEnumerable<T> existing)` — `new T()` with `Id` already assigned

Why `NewItem` exists: `EntityViewModel<T>` cannot write `new T { Id = ... }` because `T` has no statically-known `Id` member. Reflection stays confined to this one file.

- [ ] **Step 1: Write the failing test**

Replace the whole of `CodeGenerator.Tests/App/JsonStoreTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class JsonStoreTemplateTests
{
    [TestMethod]
    public void Source_EmitsGenericStoreInTheGivenNamespace()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "class JsonStore<T>");
        StringAssert.Contains(source, "AppContext.BaseDirectory");
    }

    [TestMethod]
    public void Source_ExposesTheBatchEditingSurface()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        StringAssert.Contains(source, "public List<T> GetAll()");
        StringAssert.Contains(source, "public bool SaveAll(List<T> all)");
        StringAssert.Contains(source, "public int NextId(IEnumerable<T> existing)");
        StringAssert.Contains(source, "public T NewItem(IEnumerable<T> existing)");
    }

    [TestMethod]
    public void Source_DropsThePerRecordConsoleSurface()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        Assert.IsFalse(source.Contains("public T Add(T item)"), "Add is dead code in a grid app");
        Assert.IsFalse(source.Contains("public bool Update(T item)"), "Update is dead code in a grid app");
        Assert.IsFalse(source.Contains("public bool Delete(int id)"), "Delete is dead code in a grid app");
    }

    [TestMethod]
    public void Source_SurvivesACorruptStoreInsteadOfCrashingTheWindow()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        StringAssert.Contains(source, "JsonException");
        StringAssert.Contains(source, "UnauthorizedAccessException");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~JsonStoreTemplateTests`

Expected: FAIL to compile — `Source()` takes no arguments (CS1501).

- [ ] **Step 3: Write minimal implementation**

Replace the whole of `AppScaffolding/JsonStoreTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits the source of the generated app's generic JSON store.</summary>
public static class JsonStoreTemplate
{
    /// <summary>Source of <c>Data/JsonStore.cs</c> in <paramref name="namespaceName"/>.</summary>
    public static string Source(string namespaceName) => $$"""
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace {{namespaceName}};

/// <summary>JSON-backed list store keyed by an int Id. One file per type.</summary>
public sealed class JsonStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path =
        Path.Combine(AppContext.BaseDirectory, typeof(T).Name + ".json");

    /// <summary>All stored records. An absent, unreadable, or corrupt file reads as empty.</summary>
    public List<T> GetAll()
    {
        if (!File.Exists(_path))
            return new List<T>();

        try
        {
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(_path)) ?? new List<T>();
        }
        catch (Exception ex)
            when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A GUI has nowhere to print a stack trace: start empty rather than
            // die before the window appears.
            return new List<T>();
        }
    }

    /// <summary>Overwrites the store with <paramref name="all"/>. False if it could not be written.</summary>
    public bool SaveAll(List<T> all)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(all, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>One past the highest Id in <paramref name="existing"/>, or 1 when there are none.</summary>
    public int NextId(IEnumerable<T> existing)
    {
        var ids = existing.Select(GetId).ToList();
        return ids.Count == 0 ? 1 : ids.Max() + 1;
    }

    /// <summary>A new record with its Id already assigned.</summary>
    public T NewItem(IEnumerable<T> existing)
    {
        var item = new T();
        IdProperty.SetValue(item, NextId(existing));
        return item;
    }

    private static PropertyInfo IdProperty =>
        typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(typeof(T).Name + " has no Id property.");

    private static int GetId(T item) => (int)(IdProperty.GetValue(item) ?? 0);
}
""";
}
```

Note: `$$"""` means `{{namespaceName}}` interpolates while single braces (`new() { WriteIndented = true }`) stay literal.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~JsonStoreTemplateTests`

Expected: PASS, 4 tests. The build will still fail elsewhere if `AppScaffolder.cs` calls `JsonStoreTemplate.Source()` with no argument — if so, pass `"GeneratedCode"` there as a temporary placeholder; Task 7 replaces the whole call.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/JsonStoreTemplate.cs CodeGenerator.Tests/App/JsonStoreTemplateTests.cs
git commit -m "$(cat <<'EOF'
Reshape JsonStore for batch editing and a GUI host

A DataGrid edits in place and commits as a batch, so per-record Add/Update/
Delete would ship as dead code in every scaffolded app. Replace them with
GetAll/SaveAll/NextId/NewItem.

GetAll now swallows a corrupt store instead of throwing: in a console app that
printed a stack trace, but in WPF it kills the process before the window opens.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: The four static MVVM templates

**Files:**
- Create: `AppScaffolding/ViewModelTemplates.cs`
- Test: `CodeGenerator.Tests/App/ViewModelTemplatesTests.cs` (create)

**Interfaces:**
- Consumes: the generated `JsonStore<T>` surface from Task 2 (`GetAll`, `SaveAll`, `NextId`, `NewItem`).
- Produces four methods, each taking the **app name** (not a full namespace) and returning one file's source:
  - `ViewModelTemplates.ObservableObject(string appName)`
  - `ViewModelTemplates.RelayCommand(string appName)`
  - `ViewModelTemplates.EntityViewModelInterface(string appName)`
  - `ViewModelTemplates.EntityViewModel(string appName)`
- The generated `IEntityViewModel` contract, which Task 4's XAML binds to: `string Title`, `IEnumerable Items`, `object? Selected`, `ICommand AddCommand`, `ICommand DeleteCommand`, `ICommand SaveCommand`, `string Status`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ViewModelTemplatesTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ViewModelTemplatesTests
{
    [TestMethod]
    public void AllTemplates_LiveInTheAppsViewModelsNamespace()
    {
        foreach (var source in new[]
        {
            ViewModelTemplates.ObservableObject("Payroll"),
            ViewModelTemplates.RelayCommand("Payroll"),
            ViewModelTemplates.EntityViewModelInterface("Payroll"),
            ViewModelTemplates.EntityViewModel("Payroll"),
        })
        {
            StringAssert.Contains(source, "namespace Payroll.ViewModels");
        }
    }

    [TestMethod]
    public void Interface_DeclaresEverythingTheXamlBindsTo()
    {
        var source = ViewModelTemplates.EntityViewModelInterface("Payroll");

        StringAssert.Contains(source, "interface IEntityViewModel");
        StringAssert.Contains(source, "string Title { get; }");
        StringAssert.Contains(source, "IEnumerable Items { get; }");
        StringAssert.Contains(source, "object? Selected { get; set; }");
        StringAssert.Contains(source, "ICommand AddCommand { get; }");
        StringAssert.Contains(source, "ICommand DeleteCommand { get; }");
        StringAssert.Contains(source, "ICommand SaveCommand { get; }");
        StringAssert.Contains(source, "string Status { get; }");
    }

    [TestMethod]
    public void EntityViewModel_ExposesRecordsAsTypedCollection()
    {
        var source = ViewModelTemplates.EntityViewModel("Payroll");

        // DataGrid reads its columns from IEnumerable<T>'s type argument, so the
        // backing collection must stay generic or empty tabs lose their columns.
        StringAssert.Contains(source, "public ObservableCollection<T> Records { get; }");
        StringAssert.Contains(source, "public IEnumerable Items => Records;");
    }

    [TestMethod]
    public void EntityViewModel_UsesTheStoreForIdsAndPersistence()
    {
        var source = ViewModelTemplates.EntityViewModel("Payroll");

        StringAssert.Contains(source, "using Payroll.Data;");
        StringAssert.Contains(source, "_store.NewItem(Records)");
        StringAssert.Contains(source, "_store.SaveAll(Records.ToList())");
    }

    [TestMethod]
    public void EntityViewModel_DisablesDeleteWithoutASelection()
    {
        var source = ViewModelTemplates.EntityViewModel("Payroll");

        StringAssert.Contains(source, "new RelayCommand(Delete, () => _selected is T)");
    }

    [TestMethod]
    public void RelayCommand_RequeriesOnUiActivity()
    {
        var source = ViewModelTemplates.RelayCommand("Payroll");

        // Without CommandManager the Delete button never re-enables on selection change.
        StringAssert.Contains(source, "CommandManager.RequerySuggested");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ViewModelTemplatesTests`

Expected: FAIL to compile — `ViewModelTemplates` does not exist (CS0103).

- [ ] **Step 3: Write minimal implementation**

Create `AppScaffolding/ViewModelTemplates.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// The MVVM plumbing every scaffolded app gets, identical apart from its namespace.
/// Hand-rolled so a generated app needs no MVVM package.
/// </summary>
public static class ViewModelTemplates
{
    /// <summary>Source of <c>ViewModels/ObservableObject.cs</c>.</summary>
    public static string ObservableObject(string appName) => $$"""
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace {{appName}}.ViewModels;

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
    public static string RelayCommand(string appName) => $$"""
using System;
using System.Windows.Input;

namespace {{appName}}.ViewModels;

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

    /// <summary>Source of <c>ViewModels/IEntityViewModel.cs</c>.</summary>
    public static string EntityViewModelInterface(string appName) => $$"""
using System.Collections;
using System.Windows.Input;

namespace {{appName}}.ViewModels;

/// <summary>The non-generic face of one entity's tab. MainWindow.xaml binds to this.</summary>
public interface IEntityViewModel
{
    string Title { get; }
    IEnumerable Items { get; }
    object? Selected { get; set; }
    ICommand AddCommand { get; }
    ICommand DeleteCommand { get; }
    ICommand SaveCommand { get; }
    string Status { get; }
}
""";

    /// <summary>Source of <c>ViewModels/EntityViewModel.cs</c>.</summary>
    public static string EntityViewModel(string appName) => $$"""
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using {{appName}}.Data;

namespace {{appName}}.ViewModels;

/// <summary>One entity's records, edited in memory and persisted on Save.</summary>
public sealed class EntityViewModel<T> : ObservableObject, IEntityViewModel where T : new()
{
    private readonly JsonStore<T> _store = new();
    private object? _selected;
    private string _status = string.Empty;

    public EntityViewModel(string title)
    {
        Title = title;
        Records = new ObservableCollection<T>(_store.GetAll());
        AddCommand = new RelayCommand(Add);
        DeleteCommand = new RelayCommand(Delete, () => _selected is T);
        SaveCommand = new RelayCommand(Save);
    }

    public string Title { get; }

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

        Records.Remove(item);
        Selected = null;
        Status = $"Removed a {Title}. Not saved yet.";
    }

    private void Save() =>
        Status = _store.SaveAll(Records.ToList())
            ? $"Saved {Records.Count} {Title} record(s)."
            : $"Could not write {typeof(T).Name}.json.";
}
""";
}
```

Note the escaping: inside `$$"""`, the generated app's own interpolated strings must be written as `$"Added a {Title}..."` with **single** braces — those are literal to the outer template and become interpolations in the generated file.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ViewModelTemplatesTests`

Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ViewModelTemplates.cs CodeGenerator.Tests/App/ViewModelTemplatesTests.cs
git commit -m "$(cat <<'EOF'
Add the scaffolded app's MVVM plumbing templates

ObservableObject, RelayCommand, IEntityViewModel, and EntityViewModel<T> are
identical in every scaffolded app apart from their namespace, so they live in
one template file. Hand-rolled to keep generated apps free of a MVVM package.

EntityViewModel exposes Records as ObservableCollection<T> rather than a
non-generic collection: DataGrid reads its columns from the type argument, so
a generic collection is what makes an empty tab still show its columns.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: `MainViewModel` and `MainWindow` templates

**Files:**
- Create: `AppScaffolding/MainViewModelTemplate.cs`
- Create: `AppScaffolding/MainWindowTemplate.cs`
- Test: `CodeGenerator.Tests/App/MainViewModelTemplateTests.cs` (create)
- Test: `CodeGenerator.Tests/App/MainWindowTemplateTests.cs` (create)

**Interfaces:**
- Consumes: `IEntityViewModel` / `EntityViewModel<T>` from Task 3.
- Produces:
  - `MainViewModelTemplate.Build(string appName, IReadOnlyList<string> entityNames)` → `ViewModels/MainViewModel.cs`
  - `MainWindowTemplate.Xaml(string appName)` → `Views/MainWindow.xaml`
  - `MainWindowTemplate.CodeBehind(string appName)` → `Views/MainWindow.xaml.cs`
- `MainViewModel` exposes `ObservableCollection<IEntityViewModel> Entities`, which the XAML's `TabControl` binds to.

`entityNames` arrive already de-conflicted by `DomainModelMapper` (Task 5), so this template does no name checking.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/MainViewModelTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class MainViewModelTemplateTests
{
    [TestMethod]
    public void Build_ConstructsOneViewModelPerEntity()
    {
        var source = MainViewModelTemplate.Build("Payroll", new[] { "Employee", "Department" });

        StringAssert.Contains(source, "namespace Payroll.ViewModels");
        StringAssert.Contains(source, "using Payroll.Models;");
        StringAssert.Contains(source, "new EntityViewModel<Employee>(\"Employee\"),");
        StringAssert.Contains(source, "new EntityViewModel<Department>(\"Department\"),");
        StringAssert.Contains(source, "ObservableCollection<IEntityViewModel> Entities");
    }

    [TestMethod]
    public void Build_NamesEachEntityExactlyOnceAsATypeArgument()
    {
        var source = MainViewModelTemplate.Build("Payroll", new[] { "Employee" });

        var occurrences = source.Split("EntityViewModel<Employee>").Length - 1;
        Assert.AreEqual(1, occurrences);
    }

    [TestMethod]
    public void Build_WithNoEntities_StillCompilesToAnEmptyCollection()
    {
        var source = MainViewModelTemplate.Build("Payroll", Array.Empty<string>());

        StringAssert.Contains(source, "ObservableCollection<IEntityViewModel>");
        Assert.IsFalse(source.Contains("new EntityViewModel<"));
    }
}
```

Create `CodeGenerator.Tests/App/MainWindowTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class MainWindowTemplateTests
{
    [TestMethod]
    public void Xaml_DeclaresTheViewsNamespacedPartialClass()
    {
        var xaml = MainWindowTemplate.Xaml("Payroll");

        StringAssert.Contains(xaml, "x:Class=\"Payroll.Views.MainWindow\"");
    }

    [TestMethod]
    public void Xaml_BindsATabPerEntityOverAGrid()
    {
        var xaml = MainWindowTemplate.Xaml("Payroll");

        StringAssert.Contains(xaml, "<TabControl ItemsSource=\"{Binding Entities}\"");
        StringAssert.Contains(xaml, "DisplayMemberPath=\"Title\"");
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Items}\"");
        StringAssert.Contains(xaml, "SelectedItem=\"{Binding Selected, Mode=TwoWay}\"");
        StringAssert.Contains(xaml, "AutoGenerateColumns=\"True\"");
    }

    [TestMethod]
    public void Xaml_ForbidsTheGridsOwnNewRow()
    {
        var xaml = MainWindowTemplate.Xaml("Payroll");

        // The placeholder row would construct T with Id = 0, bypassing NextId().
        StringAssert.Contains(xaml, "CanUserAddRows=\"False\"");
    }

    [TestMethod]
    public void Xaml_ExposesTheThreeCommandsAndStatus()
    {
        var xaml = MainWindowTemplate.Xaml("Payroll");

        StringAssert.Contains(xaml, "Command=\"{Binding AddCommand}\"");
        StringAssert.Contains(xaml, "Command=\"{Binding DeleteCommand}\"");
        StringAssert.Contains(xaml, "Command=\"{Binding SaveCommand}\"");
        StringAssert.Contains(xaml, "Text=\"{Binding Status}\"");
    }

    [TestMethod]
    public void CodeBehind_SetsTheDataContextAndLocksTheIdColumn()
    {
        var source = MainWindowTemplate.CodeBehind("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Views");
        StringAssert.Contains(source, "using Payroll.ViewModels;");
        StringAssert.Contains(source, "DataContext = new MainViewModel();");
        StringAssert.Contains(source, "e.Column.IsReadOnly = true;");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~MainViewModelTemplateTests|FullyQualifiedName~MainWindowTemplateTests"`

Expected: FAIL to compile — neither template class exists (CS0103).

- [ ] **Step 3: Write `MainViewModelTemplate`**

Create `AppScaffolding/MainViewModelTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>The one generated file whose shape depends on the entity set.</summary>
public static class MainViewModelTemplate
{
    public static string Build(string appName, IReadOnlyList<string> entityNames)
    {
        var registrations = string.Join("\n",
            entityNames.Select(name =>
                $"            new EntityViewModel<{name}>(\"{name}\"),"));

        return $$"""
using System.Collections.ObjectModel;
using {{appName}}.Models;

namespace {{appName}}.ViewModels;

/// <summary>One tab per discovered entity.</summary>
public sealed class MainViewModel
{
    public MainViewModel()
    {
        Entities = new ObservableCollection<IEntityViewModel>
        {
{{registrations}}
        };
    }

    public ObservableCollection<IEntityViewModel> Entities { get; }
}
""";
    }
}
```

With no entities, `registrations` is empty and the collection initializer is `{\n\n        }` — still valid C#.

- [ ] **Step 4: Write `MainWindowTemplate`**

Create `AppScaffolding/MainWindowTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// The view. Entirely static: a TabControl over IEntityViewModel renders any
/// entity set, so no entity name ever appears in XAML.
/// </summary>
public static class MainWindowTemplate
{
    /// <summary>Source of <c>Views/MainWindow.xaml</c>.</summary>
    public static string Xaml(string appName) => $$"""
<Window x:Class="{{appName}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{appName}}" Height="600" Width="900">
    <TabControl ItemsSource="{Binding Entities}" DisplayMemberPath="Title">
        <TabControl.ContentTemplate>
            <DataTemplate>
                <DockPanel>
                    <StatusBar DockPanel.Dock="Bottom">
                        <TextBlock Text="{Binding Status}" />
                    </StatusBar>
                    <StackPanel DockPanel.Dock="Bottom"
                                Orientation="Horizontal"
                                HorizontalAlignment="Right"
                                Margin="8">
                        <Button Content="Add" Command="{Binding AddCommand}"
                                Width="80" Margin="4,0" />
                        <Button Content="Delete" Command="{Binding DeleteCommand}"
                                Width="80" Margin="4,0" />
                        <Button Content="Save" Command="{Binding SaveCommand}"
                                Width="80" Margin="4,0" />
                    </StackPanel>
                    <DataGrid ItemsSource="{Binding Items}"
                              SelectedItem="{Binding Selected, Mode=TwoWay}"
                              AutoGenerateColumns="True"
                              CanUserAddRows="False"
                              AutoGeneratingColumn="OnAutoGeneratingColumn" />
                </DockPanel>
            </DataTemplate>
        </TabControl.ContentTemplate>
    </TabControl>
</Window>
""";

    /// <summary>Source of <c>Views/MainWindow.xaml.cs</c>.</summary>
    public static string CodeBehind(string appName) => $$"""
using System;
using System.Windows;
using System.Windows.Controls;
using {{appName}}.ViewModels;

namespace {{appName}}.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    // Column presentation is a view concern, so the Id lock lives here rather
    // than in a view model. This is the only code-behind in the generated app.
    private void OnAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (string.Equals(e.PropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            e.Column.IsReadOnly = true;
    }
}
""";
}
```

An event handler named inside a `DataTemplate` resolves against the code-behind of the XAML file that declares the template — the `Window` — so `OnAutoGeneratingColumn` wires up correctly.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~MainViewModelTemplateTests|FullyQualifiedName~MainWindowTemplateTests"`

Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/MainViewModelTemplate.cs AppScaffolding/MainWindowTemplate.cs CodeGenerator.Tests/App/MainViewModelTemplateTests.cs CodeGenerator.Tests/App/MainWindowTemplateTests.cs
git commit -m "$(cat <<'EOF'
Add the MainViewModel and MainWindow templates

MainWindow.xaml is entirely static: a TabControl bound to IEntityViewModel with
a DataGrid in its ContentTemplate renders any entity set, so no entity name ever
reaches the XAML. MainViewModel is the single generated file that names them,
one construction line each.

CanUserAddRows is off because the grid's placeholder row would construct records
with Id = 0, bypassing the store's NextId.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Refresh `ReservedNames` for the WPF templates

**Files:**
- Modify: `AppScaffolding/DomainModelMapper.cs:18-33` (the `ReservedNames` set) and its doc comment at `:11-17`
- Test: `CodeGenerator.Tests/App/DomainModelMapperTests.cs` (extend)

**Interfaces:**
- Consumes: nothing new.
- Produces: no signature change. `ToClassDefinitions` keeps returning `IReadOnlyList<ClassDefinition>` with unique, non-colliding names.

Why this task exists: an entity named `EntityViewModel` would make `new EntityViewModel<EntityViewModel>(...)` bind its type argument to the *view model*, not the model — the current namespace beats a `using`-imported one — and fail with CS0310 since it has no parameterless constructor. `Application` is the sharpest real-world case: it is a common harvested noun *and* `System.Windows.Application`.

**Two existing tests in this file contradict the new set and must be updated in the same commit — this is a behavior change, not a regression:**
- `DomainModelMapperTests.cs:22` (`..._ThatCollideWithTypesUsedByGeneratedCode`) asserts `Console` → `ConsoleItem` and `Crud` → `CrudItem`. Both names leave `ReservedNames`, since nothing generates a console host any more.
- `DomainModelMapperTests.cs:118` (`..._MultipleEntities_PreservesNamesAndOrder`) asserts `Application` survives unrenamed. It no longer does.

The file already has a `ModelOf(params string[] entityNames)` helper — use it.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/DomainModelMapperTests.cs`, inside the existing `[TestClass]`:

```csharp
    [TestMethod]
    public void ToClassDefinitions_RenamesEntitiesThatCollideWithGeneratedWpfTypes()
    {
        // `new EntityViewModel<EntityViewModel>(...)` would bind its type argument to
        // the view model rather than the model, since the current namespace beats a
        // using-imported one. `Application` is both a common noun and System.Windows.Application.
        var classes = DomainModelMapper.ToClassDefinitions(
            ModelOf("Application", "MainWindow", "EntityViewModel", "RelayCommand"));

        CollectionAssert.AreEqual(
            new[] { "ApplicationItem", "MainWindowItem", "EntityViewModelItem", "RelayCommandItem" },
            classes.Select(c => c.Name).ToList());
    }
```

Then update the two tests that assert the old set. Replace the body of `ToClassDefinitions_RenamesEntities_ThatCollideWithTypesUsedByGeneratedCode` (line ~22):

```csharp
    [TestMethod]
    public void ToClassDefinitions_RenamesEntities_ThatCollideWithTypesUsedByGeneratedCode()
    {
        // Entities land in <App>.Models, but a name shared with a type the generated
        // app declares or imports by simple name binds to the wrong type.
        var classes = DomainModelMapper.ToClassDefinitions(
            ModelOf("Type", "Convert", "JsonStore", "Window"));

        CollectionAssert.AreEqual(
            new[] { "TypeItem", "ConvertItem", "JsonStoreItem", "WindowItem" },
            classes.Select(c => c.Name).ToList());
    }
```

and replace `ToClassDefinitions_MultipleEntities_PreservesNamesAndOrder` (line ~118), whose `Application` no longer survives:

```csharp
    [TestMethod]
    public void ToClassDefinitions_MultipleEntities_PreservesNamesAndOrder()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Payroll" });
        model.Entities.Add(new CandidateEntity { Name = "Deduction" });

        var classes = DomainModelMapper.ToClassDefinitions(model);

        CollectionAssert.AreEqual(
            new[] { "Payroll", "Deduction" },
            classes.Select(c => c.Name).ToList());
    }
```

- [ ] **Step 2: Run tests to verify the new one fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~DomainModelMapperTests`

Expected: FAIL. The new test fails because `Application`, `MainWindow`, `EntityViewModel`, and `RelayCommand` come through unrenamed. `..._ThatCollideWithTypesUsedByGeneratedCode` also fails, on `Window` — expected, since `Window` is not yet reserved.

- [ ] **Step 3: Write minimal implementation**

In `AppScaffolding/DomainModelMapper.cs`, replace the doc comment and the `ReservedNames` initializer:

```csharp
    /// <summary>
    /// Identifiers already in scope in the generated app's own sources. An entity class
    /// sharing one of these names makes uses of it ambiguous, or silently binds to the
    /// wrong type, so the entity is renamed instead.
    /// C# keywords need no guard: entity names arrive PascalCased and keywords are lowercase.
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Types the generated app declares or references by simple name.
        "App", "Application", "MainWindow", "Window", "MainViewModel", "EntityViewModel",
        "IEntityViewModel", "ObservableObject", "RelayCommand", "ICommand", "JsonStore",
        "DataGrid", "TabControl", "ObservableCollection",

        // Commonly harvested words that are also System types.
        "Activator", "Array", "Attribute", "Buffer", "Comparer", "Convert", "Delegate",
        "Enum", "Environment", "Exception", "Guid", "Index", "Math", "Object", "Random",
        "Range", "String", "Task", "Timer", "Tuple", "Uri", "Version",
        "Boolean", "Byte", "Char", "DateTime", "Decimal", "Double", "Int16", "Int32", "Int64",
        "Single", "TimeSpan", "Type", "PropertyInfo",

        // System.IO / System.Collections names pulled in by the templates.
        "Directory", "File", "Path", "Stream", "Dictionary", "List", "Queue", "Stack",
        "Enumerable", "IEnumerable",
    };
```

`Console`, `TypeDescriptor`, and `Crud` are dropped: they were console-host names and nothing generates them any more.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~DomainModelMapperTests`

Expected: PASS, all 9 tests — the new one, the two rewritten ones, and the six untouched ones.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/App/DomainModelMapperTests.cs
git commit -m "$(cat <<'EOF'
Guard entity names against the WPF templates' identifiers

An entity named EntityViewModel binds `new EntityViewModel<EntityViewModel>` to
the view model rather than the model, since the current namespace beats a
using-imported one. `Application` is the likeliest collision in practice: a
common harvested noun and System.Windows.Application.

Drops Console/TypeDescriptor/Crud, which nothing generates now.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: `WpfProjectFixup` — reconcile the `dotnet new wpf` output

**Files:**
- Create: `AppScaffolding/WpfProjectFixup.cs`
- Test: `CodeGenerator.Tests/App/WpfProjectFixupTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `WpfProjectFixup.Apply(string projectFolder)` → `void`. Deletes `projectFolder/MainWindow.xaml` and `projectFolder/MainWindow.xaml.cs` if present, and rewrites `projectFolder/App.xaml`'s `StartupUri="MainWindow.xaml"` to `StartupUri="Views/MainWindow.xaml"`. Best-effort: an unwritable file is skipped, never thrown.

This is a separate file from `AppScaffolder` **so it can be tested without invoking `dotnet new`** — the whole reason `AppScaffolder.Scaffold` has no unit test today.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/WpfProjectFixupTests.cs`:

```csharp
using System.IO;
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfProjectFixupTests
{
    /// <summary>Exactly what `dotnet new wpf -n Payroll` writes for App.xaml.</summary>
    private const string TemplateAppXaml = """
<Application x:Class="Payroll.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Payroll"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
""";

    private static string NewProjectFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "App.xaml"), TemplateAppXaml);
        File.WriteAllText(Path.Combine(folder, "MainWindow.xaml"), "<Window />");
        File.WriteAllText(Path.Combine(folder, "MainWindow.xaml.cs"), "// template");
        return folder;
    }

    [TestMethod]
    public void Apply_DeletesTheTemplatesRootWindow()
    {
        var folder = NewProjectFolder();
        try
        {
            WpfProjectFixup.Apply(folder);

            // Left in place, its x:Class duplicates Views/MainWindow and the build fails.
            Assert.IsFalse(File.Exists(Path.Combine(folder, "MainWindow.xaml")));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "MainWindow.xaml.cs")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_RepointsStartupUriAtTheViewsFolder()
    {
        var folder = NewProjectFolder();
        try
        {
            WpfProjectFixup.Apply(folder);

            var appXaml = File.ReadAllText(Path.Combine(folder, "App.xaml"));
            StringAssert.Contains(appXaml, "StartupUri=\"Views/MainWindow.xaml\"");
            StringAssert.Contains(appXaml, "x:Class=\"Payroll.App\"", "the template's own markup must survive");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_OnAnAlreadyFixedProject_ChangesNothing()
    {
        var folder = NewProjectFolder();
        try
        {
            WpfProjectFixup.Apply(folder);
            var once = File.ReadAllText(Path.Combine(folder, "App.xaml"));

            WpfProjectFixup.Apply(folder);
            var twice = File.ReadAllText(Path.Combine(folder, "App.xaml"));

            Assert.AreEqual(once, twice, "Apply must be idempotent");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_WithNoAppXaml_DoesNotThrow()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            WpfProjectFixup.Apply(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~WpfProjectFixupTests`

Expected: FAIL to compile — `WpfProjectFixup` does not exist (CS0103).

- [ ] **Step 3: Write minimal implementation**

Create `AppScaffolding/WpfProjectFixup.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// Reconciles the files `dotnet new wpf` emits with the foldered layout this
/// scaffolder writes. Runs only against a project this scaffolder just created.
/// </summary>
public static class WpfProjectFixup
{
    private const string TemplateStartupUri = "StartupUri=\"MainWindow.xaml\"";
    private const string FolderedStartupUri = "StartupUri=\"Views/MainWindow.xaml\"";

    /// <summary>
    /// Deletes the template's root window and repoints <c>App.xaml</c> at
    /// <c>Views/MainWindow.xaml</c>. Best-effort and idempotent.
    /// </summary>
    public static void Apply(string projectFolder)
    {
        // The template's MainWindow declares the same x:Class as the one written
        // into Views/, so it must be removed, not merely shadowed.
        Delete(Path.Combine(projectFolder, "MainWindow.xaml"));
        Delete(Path.Combine(projectFolder, "MainWindow.xaml.cs"));

        RepointStartupUri(Path.Combine(projectFolder, "App.xaml"));
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; the build error that follows is clearer than a crash here.
        }
    }

    private static void RepointStartupUri(string appXamlPath)
    {
        try
        {
            if (!File.Exists(appXamlPath))
                return;

            var xaml = File.ReadAllText(appXamlPath);
            if (!xaml.Contains(TemplateStartupUri))
                return;

            // Rewrite the template's own markup rather than regenerate it, so its
            // x:Class and xmlns declarations survive untouched.
            File.WriteAllText(appXamlPath, xaml.Replace(TemplateStartupUri, FolderedStartupUri));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~WpfProjectFixupTests`

Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/WpfProjectFixup.cs CodeGenerator.Tests/App/WpfProjectFixupTests.cs
git commit -m "$(cat <<'EOF'
Reconcile the dotnet new wpf output with the foldered layout

The template's root MainWindow.xaml declares the same x:Class as the one we
write into Views/, so it has to go rather than be shadowed. App.xaml's
StartupUri is rewritten in place so the template's x:Class and xmlns survive.

Split out of AppScaffolder so it is unit-testable without invoking dotnet new.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Wire `AppScaffolder` to emit the foldered WPF app

**Files:**
- Modify: `AppScaffolding/AppScaffolder.cs` (whole file)
- Delete: `AppScaffolding/ProgramTemplate.cs`
- Delete: `CodeGenerator.Tests/App/ProgramTemplateTests.cs`
- Test: `CodeGenerator.Tests/App/AppScaffolderWriteTests.cs` (extend)

**Interfaces:**
- Consumes: everything from Tasks 1–6.
- Produces:
  - `AppScaffolder.WriteClasses(string outputFolder, IReadOnlyList<ClassDefinition> classes, string namespaceName)` — note the **third parameter**; `outputFolder` is now the `Models/` folder, not the project root.
  - `AppScaffolder.Scaffold(CandidateDomainModel model, string appName, string workspaceRoot)` — signature unchanged. `ScaffoldResult.FilesWritten` now contains paths with subfolders.

- [ ] **Step 1: Write the failing test**

Replace the whole of `CodeGenerator.Tests/App/AppScaffolderWriteTests.cs`:

```csharp
using System.IO;
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppScaffolderWriteTests
{
    private static List<ClassDefinition> OneClass() => new()
    {
        new()
        {
            Name = "Employee",
            Properties = { new PropertyDefinition { Name = "Salary", Type = "decimal" } }
        }
    };

    [TestMethod]
    public void WriteClasses_WritesOneFilePerClass_InTheGivenNamespace()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scaffold_" + Path.GetRandomFileName());
        try
        {
            var written = new AppScaffolder().WriteClasses(folder, OneClass(), "Payroll.Models");

            Assert.AreEqual(1, written.Count);
            var file = Path.Combine(folder, "Employee.cs");
            Assert.IsTrue(File.Exists(file), "Employee.cs should exist");

            var source = File.ReadAllText(file);
            StringAssert.Contains(source, "namespace Payroll.Models");
            StringAssert.Contains(source, "class Employee");
            StringAssert.Contains(source, "Salary");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void WriteClasses_CreatesTheTargetFolderIfMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "scaffold_" + Path.GetRandomFileName());
        var nested = Path.Combine(root, "Models");
        try
        {
            var written = new AppScaffolder().WriteClasses(nested, OneClass(), "Payroll.Models");

            Assert.AreEqual(1, written.Count);
            Assert.IsTrue(File.Exists(Path.Combine(nested, "Employee.cs")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
```

Then delete the console-host test file:

```bash
git rm CodeGenerator.Tests/App/ProgramTemplateTests.cs
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~AppScaffolderWriteTests`

Expected: FAIL to compile — `WriteClasses` takes 2 arguments, not 3 (CS1501).

- [ ] **Step 3: Write minimal implementation**

Delete `AppScaffolding/ProgramTemplate.cs`:

```bash
git rm AppScaffolding/ProgramTemplate.cs
```

Replace the whole of `AppScaffolding/AppScaffolder.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Factory;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>Generates a WPF project and one C# class file per domain entity.</summary>
public sealed class AppScaffolder
{
    /// <summary>
    /// Writes one <c>&lt;Name&gt;.cs</c> per class into <paramref name="outputFolder"/>,
    /// declaring <paramref name="namespaceName"/>. Best-effort: a file that cannot be
    /// written is skipped and the rest continue. Returns the paths successfully written.
    /// </summary>
    public IReadOnlyList<string> WriteClasses(
        string outputFolder, IReadOnlyList<ClassDefinition> classes, string namespaceName)
    {
        Directory.CreateDirectory(outputFolder);

        var written = new List<string>();
        foreach (var definition in classes)
        {
            var source = RoslynClassGenerator.Generate(definition, namespaceName);
            var path = WriteSource(outputFolder, definition.Name + ".cs", source);
            if (path is not null)
                written.Add(path);
        }

        return written;
    }

    /// <summary>
    /// Best-effort write of one source file into <paramref name="outputFolder"/>, creating
    /// the folder if needed. Returns the path on success, or null if it could not be written.
    /// </summary>
    private static string? WriteSource(string outputFolder, string fileName, string source)
    {
        try
        {
            Directory.CreateDirectory(outputFolder);
            var path = Path.Combine(outputFolder, fileName);
            File.WriteAllText(path, source);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a WPF project named <paramref name="appName"/> under
    /// <paramref name="workspaceRoot"/> (reusing it if it already exists) and writes a
    /// foldered, runnable CRUD app over the entities in <paramref name="model"/>.
    /// </summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot)
    {
        var classes = DomainModelMapper.ToClassDefinitions(model);
        var outputFolder = Path.Combine(workspaceRoot, appName);
        var csprojPath = Path.Combine(outputFolder, appName + ".csproj");

        if (!File.Exists(csprojPath))
        {
            var created = DotNetProjectFactory.CreateProject(new ProjectRequest
            {
                ProjectName = appName,
                OutputFolder = outputFolder,
                ProjectType = DotNetProjectType.Wpf,
                Framework = "net10.0"
            });

            if (!created.Success)
                return new ScaffoldResult(
                    false, outputFolder, Array.Empty<string>(),
                    $"Project creation failed: {created.Message}");

            // Only ever touches files this scaffolder just created: the reuse path
            // above skips it, so a project the user has edited is left alone.
            WpfProjectFixup.Apply(outputFolder);
        }

        var modelsFolder = Path.Combine(outputFolder, "Models");
        var dataFolder = Path.Combine(outputFolder, "Data");
        var viewModelsFolder = Path.Combine(outputFolder, "ViewModels");
        var viewsFolder = Path.Combine(outputFolder, "Views");

        var written = new List<string>(
            WriteClasses(modelsFolder, classes, $"{appName}.Models"));
        var expected = classes.Count;

        var entityNames = classes.Select(c => c.Name).ToList();

        var files = new (string Folder, string Name, string Source)[]
        {
            (dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{appName}.Data")),
            (viewModelsFolder, "ObservableObject.cs", ViewModelTemplates.ObservableObject(appName)),
            (viewModelsFolder, "RelayCommand.cs", ViewModelTemplates.RelayCommand(appName)),
            (viewModelsFolder, "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(appName)),
            (viewModelsFolder, "EntityViewModel.cs", ViewModelTemplates.EntityViewModel(appName)),
            (viewModelsFolder, "MainViewModel.cs", MainViewModelTemplate.Build(appName, entityNames)),
            (viewsFolder, "MainWindow.xaml", MainWindowTemplate.Xaml(appName)),
            (viewsFolder, "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(appName)),
        };

        foreach (var (folder, name, source) in files)
        {
            expected++;
            var path = WriteSource(folder, name, source);
            if (path is not null)
                written.Add(path);
        }

        var allWritten = written.Count == expected;

        var message = allWritten
            ? $"Generated {classes.Count} class(es) + a runnable WPF CRUD app."
            : $"Generated {written.Count} of {expected} file(s); " +
              "some files could not be written.";

        return new ScaffoldResult(allWritten, outputFolder, written, message);
    }
}
```

Note `WriteClasses` now routes through `WriteSource`, so the two share one best-effort path instead of duplicating the try/catch.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~AppScaffolderWriteTests`

Expected: PASS, 2 tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`

Expected: PASS. `ProgramTemplateTests` is gone; nothing references `ProgramTemplate`.

Confirm nothing still does:

Run: `grep -rn "ProgramTemplate" --include=*.cs .`

Expected: no output.

- [ ] **Step 6: Commit**

```bash
git add -A AppScaffolding CodeGenerator.Tests/App
git commit -m "$(cat <<'EOF'
Generate a foldered WPF app instead of a flat console app

Scaffold now creates a wpf project, applies the template fixup, and writes into
Models/, Data/, ViewModels/, and Views/ with namespaces matching the folders.
WriteClasses takes the namespace and routes through WriteSource, so both share
one best-effort write path.

Deletes ProgramTemplate: the console host is replaced, not kept behind a flag.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Print relative paths, then verify the generated app for real

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs:80-83`

**Interfaces:**
- Consumes: `ScaffoldResult.FilesWritten` (now containing subfolder paths) and `ScaffoldResult.ProjectPath`.
- Produces: nothing further.

- [ ] **Step 1: Fix the file listing**

`Path.GetFileName` would print `Models/Recipe.cs` and a root `Recipe.cs` identically. In `CodeGenerator/App/BuildAppCommand.cs`, replace:

```csharp
                    if (scaffold.Success)
                    {
                        Console.WriteLine($"Generated project at {scaffold.ProjectPath}");
                        foreach (var file in scaffold.FilesWritten)
                            Console.WriteLine($"  {Path.GetFileName(file)}");
                    }
```

with:

```csharp
                    if (scaffold.Success)
                    {
                        Console.WriteLine($"Generated project at {scaffold.ProjectPath}");
                        foreach (var file in scaffold.FilesWritten)
                            Console.WriteLine($"  {Path.GetRelativePath(scaffold.ProjectPath, file)}");
                    }
```

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`

Expected: build succeeds, 0 errors.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`

Expected: PASS, all tests.

- [ ] **Step 4: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs
git commit -m "$(cat <<'EOF'
Print generated files relative to the project root

FilesWritten now carries subfolder paths, and GetFileName would render
Models/Recipe.cs and a root Recipe.cs identically.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 5: Rebuild the CLI before touching it**

`dotnet test` leaves `CodeGenerator/bin` stale — a working change looks dead if you skip this.

Run: `dotnet build CodeGenerator/CodeGenerator.csproj`

Expected: build succeeds.

- [ ] **Step 6: Scaffold a real app**

Run the CLI and issue `build app: recipe management`, then `a` to approve at the review prompt.

Expected: `Generated project at <workspace>/RecipeManagement`, then a file list showing `Models/…`, `Data/JsonStore.cs`, five `ViewModels/…`, and two `Views/…` paths.

Then confirm the layout on disk:

Run: `find <workspace>/RecipeManagement -type f -not -path '*/obj/*' -not -path '*/bin/*'`

Expected: no root `MainWindow.xaml` or `MainWindow.xaml.cs`. `App.xaml` present. All generated sources under the four folders.

Run: `grep StartupUri <workspace>/RecipeManagement/App.xaml`

Expected: `StartupUri="Views/MainWindow.xaml"`

- [ ] **Step 7: Build the generated app and report what it prints**

Run: `dotnet build <workspace>/RecipeManagement/RecipeManagement.csproj`

Expected: build succeeds. **This is the only check that proves the XAML compiles** — no unit test can, because XAML compilation needs MSBuild.

If it fails, do not paper over it: read the error, fix the offending template, re-run the relevant task's unit tests, and repeat. Report the actual compiler output either way — do not claim success without pasting it.

- [ ] **Step 8: Launch it and exercise persistence**

Run: `dotnet run --project <workspace>/RecipeManagement/RecipeManagement.csproj`

Check, in order:
1. A window opens with one tab per entity, each tab labelled with the entity name.
2. A tab shows grid **columns** despite having no rows (this is the `IEnumerable<T>` type-argument behavior — if columns are missing, `Items` lost its generic backing).
3. `Add` appends a row with `Id = 1`, and the `Id` cell is not editable.
4. `Delete` is disabled with nothing selected, and enabled once a row is selected.
5. Type a value, click `Save`; the status bar reports the save.
6. Close the app, relaunch, and confirm the row is still there.
7. `<Entity>.json` exists under the app's `bin/` output folder.

Report what actually happened at each step, including anything that did not work.

- [ ] **Step 9: Update the roadmap spec if it claims a console host**

Run: `grep -rn "console" docs/superpowers/specs/2026-07-07-domain-discovery-build-app-design.md`

If that roadmap names a console host as the delivered slice, add one line noting it was superseded by `2026-07-08-wpf-scaffolding-design.md`. Commit only if changed.

---

## Self-Review

**Spec coverage.** Every section of the spec maps to a task: project template → Task 7; namespaces → Task 1 + 7; `MainViewModel` → Task 4; `IEntityViewModel`/`EntityViewModel<T>` → Task 3; `MainWindow.xaml`(.cs) → Task 4; `JsonStore<T>` reshape → Task 2; `AppScaffolder.Scaffold` → Task 7; deleted `ProgramTemplate` → Task 7; the three template edits (delete root window, rewrite `StartupUri`, leave csproj alone) → Task 6; scaffolding-time error handling → Task 7's shared `WriteSource`; runtime error handling → Task 2; relative-path printing → Task 8; `ReservedNames` re-examination → Task 5; manual build/launch smoke → Task 8.

**Two deviations from the spec, both deliberate:**
1. The spec sketched `Items.Add(new T { Id = store.NextId(Items) })`. That does not compile — `T` has no statically-known `Id`. Task 2 adds `NewItem(IEnumerable<T>)` alongside `NextId`, keeping reflection inside `JsonStore`.
2. The spec had `SaveAll` surfacing failure "through `Status`". A store that returns UI strings mixes layers, so `SaveAll` returns `bool` and `EntityViewModel` composes the message.

**One deliberate behavior change to existing tests.** Task 5 rewrites two assertions in `DomainModelMapperTests` (`Console`/`Crud` stop being reserved; `Application` starts being reserved). Both are intended consequences of dropping the console host, called out inline in that task so a reviewer does not mistake them for regressions.

**Type consistency.** `JsonStore<T>`: `GetAll`/`SaveAll`/`NextId`/`NewItem` — declared in Task 2, asserted in Task 2's tests, consumed in Task 3's `EntityViewModel`. `IEntityViewModel`'s seven members — declared in Task 3, bound in Task 4's XAML, asserted in both. `Records` (typed) vs `Items` (non-generic) — used consistently in Task 3 and asserted in Task 3's tests. `WriteClasses(folder, classes, namespaceName)` — declared in Task 7, exercised by Task 7's tests. `WpfProjectFixup.Apply(projectFolder)` — declared in Task 6, called in Task 7.

**Known risk this plan cannot retire.** Tasks 1–7 assert on **strings**, not compilability. The XAML, the `DataTemplate`-scoped event handler, and the empty-collection column behavior are all things only Task 8 Steps 7–8 can actually prove. Treat those steps as load-bearing, not ceremony.

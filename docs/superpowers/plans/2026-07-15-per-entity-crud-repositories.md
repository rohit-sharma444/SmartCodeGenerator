# Per-Entity CRUD Repositories Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** For each entity in `model.Entities`, the scaffolded app also gets a named, typed CRUD repository (`<Entity>Repository` with `GetAll`/`GetById`/`Create`/`Update`/`Delete`), generated as a standalone API surface that leaves the running app untouched.

**Architecture:** A new string-template producer `AppScaffolding/RepositoryTemplate.cs` emits one generic `Repository<T>` base plus one thin `<Entity>Repository : Repository<<Entity>>` per entity into the app's `Data/` folder. The five functions are built only on `JsonStore<T>`'s existing public API (`GetAll`/`SaveAll`/`NextId`), so `JsonStore.cs`, `EntityViewModel.cs`, and the UI persistence path are not modified. `AppScaffolder.Scaffold` writes the repository files, driven by the same class-name list that already drives `MainViewModel`.

**Tech Stack:** C# / .NET 10 (`net10.0`), WPF for the generated app. Templates are C# raw interpolated string literals (`$$"""…"""`). Tests in `CodeGenerator.Tests` use **MSTest**.

## Global Constraints

- All projects target `net10.0`. The generated app is a WPF app; the repositories use only `System.Collections.Generic`, `System.Linq`, and `System.Reflection` — no added NuGet packages.
- The generated app's own code lives under the `<App>.*` namespaces. Repository base and typed subclasses live in `<App>.Data`; entity POCOs live in `<App>.Models`.
- Do NOT modify `JsonStore.cs`, `EntityViewModel.cs`, `IEntityViewModel`, or the UI persistence flow. The repositories are a standalone, additive API surface.
- `JsonStore<T>`'s public surface is exactly `List<T> GetAll()`, `bool SaveAll(List<T>)`, `int NextId(IEnumerable<T>)`, `T NewItem(IEnumerable<T>)`. Build the repository on `GetAll`/`SaveAll`/`NextId` only.
- Repository files are driven by **class** names (post-rename), i.e. `classes.Select(c => c.Name)` — never raw entity names.
- `CodeGenerator.Tests` is MSTest: `[TestClass]` / `[TestMethod]`, `Assert.*`, `StringAssert.*`, `CollectionAssert.*`. `ImplicitUsings` is enabled with a global `using Microsoft.VisualStudio.TestTools.UnitTesting;`.
- Run all `dotnet` commands from the repository root: `D:\SmartCodeGenerator-Repository`.
- Solution: `SmartCodeGenerator/SmartCodeGenerator.slnx`. Test project: `CodeGenerator.Tests/CodeGenerator.Tests.csproj`. CLI project: `CodeGenerator/CodeGenerator.csproj`.
- Commit after each task. Work stays on the current branch `FixesAndEnhancements`.

## File Structure

- Create: `AppScaffolding/RepositoryTemplate.cs` — `Base(appName)` and `Typed(appName, className)` source producers (Task 1).
- Create: `CodeGenerator.Tests/App/RepositoryTemplateTests.cs` — template unit tests (Task 1).
- Modify: `AppScaffolding/DomainModelMapper.cs` — add `"Repository"` to `ReservedNames` (Task 2).
- Modify: `CodeGenerator.Tests/App/DomainModelMapperTests.cs` — add the rename case (Task 2).
- Modify: `AppScaffolding/AppScaffolder.cs` — write the base + per-entity repo files in `Scaffold` (Task 3).

## Reference: existing signatures (do not redefine)

- `AppScaffolding.JsonStoreTemplate.Source(string namespaceName) : string` — unchanged.
- `AppScaffolding.RelationshipResolverTemplate.Source(string appName) : string` — unchanged; the closest template to copy for style (`$$"""…"""`, `namespace {{appName}}.Data;`).
- `AppScaffolding.MainViewModelTemplate.Build(string appName, IReadOnlyList<string> entityNames) : string` — unchanged; shows how per-entity output is driven off the class-name list.
- `AppScaffolding.AppScaffolder.Scaffold(CandidateDomainModel, string appName, string workspaceRoot) : ScaffoldResult` — **modified** in Task 3.
- `AppScaffolding.DomainModelMapper.ToClassDefinitions(CandidateDomainModel) : IReadOnlyList<ClassDefinition>` — behavior extended in Task 2 (one more reserved name).
- Generated `JsonStore<T> where T : new()` exposes `List<T> GetAll()`, `bool SaveAll(List<T>)`, `int NextId(IEnumerable<T>)`.

---

### Task 1: `RepositoryTemplate` (generic base + typed subclass source)

**Files:**
- Create: `AppScaffolding/RepositoryTemplate.cs`
- Test: `CodeGenerator.Tests/App/RepositoryTemplateTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks (takes an app name and a class name as strings).
- Produces:
  - `AppScaffolding.RepositoryTemplate.Base(string appName) : string` — full source of `Data/Repository.cs`: a `public abstract class Repository<T> where T : class, new()` in `namespace <appName>.Data`, wrapping a `JsonStore<T>` and exposing `List<T> GetAll()`, `T? GetById(int id)`, `T Create(T item)`, `bool Update(T item)`, `bool Delete(int id)`.
  - `AppScaffolding.RepositoryTemplate.Typed(string appName, string className) : string` — full source of `Data/<className>Repository.cs`: `public sealed class <className>Repository : Repository<<className>>` in `namespace <appName>.Data`, with `using <appName>.Models;`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/RepositoryTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class RepositoryTemplateTests
{
    [TestMethod]
    public void Base_EmitsGenericRepositoryInTheGivenNamespace()
    {
        var source = RepositoryTemplate.Base("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "abstract class Repository<T>");
        StringAssert.Contains(source, "where T : class, new()");
        StringAssert.Contains(source, "JsonStore<T>");
    }

    [TestMethod]
    public void Base_ExposesTheFiveCrudFunctions()
    {
        var source = RepositoryTemplate.Base("Payroll");

        StringAssert.Contains(source, "public List<T> GetAll()");
        StringAssert.Contains(source, "public T? GetById(int id)");
        StringAssert.Contains(source, "public T Create(T item)");
        StringAssert.Contains(source, "public bool Update(T item)");
        StringAssert.Contains(source, "public bool Delete(int id)");
    }

    [TestMethod]
    public void Base_PersistsThroughJsonStoresExistingApi()
    {
        var source = RepositoryTemplate.Base("Payroll");

        // Standalone surface: it reuses JsonStore's batch API rather than a new Add/Update/Delete.
        StringAssert.Contains(source, "_store.GetAll()");
        StringAssert.Contains(source, "_store.SaveAll(all)");
        StringAssert.Contains(source, "_store.NextId(all)");
    }

    [TestMethod]
    public void Typed_SubclassesTheGenericBaseForTheEntity()
    {
        var source = RepositoryTemplate.Typed("Payroll", "Employee");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "using Payroll.Models;");
        StringAssert.Contains(source, "class EmployeeRepository : Repository<Employee>");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~RepositoryTemplateTests"`
Expected: FAILS to compile — `RepositoryTemplate` does not exist yet.

- [ ] **Step 3: Implement `RepositoryTemplate`**

Create `AppScaffolding/RepositoryTemplate.cs`. Both bodies are `$$"""…"""` raw interpolated literals, so `{{appName}}` / `{{className}}` interpolate while every single `{`/`}` in the generated code stays literal:

```csharp
namespace AppScaffolding;

/// <summary>
/// Emits the generated app's typed CRUD repositories: one generic base plus one thin
/// subclass per entity.
/// </summary>
/// <remarks>
/// Standalone by design. The running app persists through EntityViewModel and
/// JsonStore.SaveAll; these repositories are an additional, typed API to code against and
/// touch none of that. They are built only on JsonStore's existing public surface
/// (GetAll/SaveAll/NextId), so JsonStore itself is unchanged.
/// </remarks>
public static class RepositoryTemplate
{
    /// <summary>Source of <c>Data/Repository.cs</c> for <paramref name="appName"/>.</summary>
    public static string Base(string appName) => $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace {{appName}}.Data;

/// <summary>Typed CRUD over one entity's JSON store, keyed by an int Id.</summary>
public abstract class Repository<T> where T : class, new()
{
    private readonly JsonStore<T> _store = new();

    /// <summary>Every stored record.</summary>
    public List<T> GetAll() => _store.GetAll();

    /// <summary>The record with this Id, or null when there is none.</summary>
    public T? GetById(int id) => _store.GetAll().FirstOrDefault(item => GetId(item) == id);

    /// <summary>Assigns the next Id, appends the record, and persists it.</summary>
    public T Create(T item)
    {
        var all = _store.GetAll();
        SetId(item, _store.NextId(all));
        all.Add(item);
        _store.SaveAll(all);
        return item;
    }

    /// <summary>Replaces the record sharing this item's Id. False when there is none.</summary>
    public bool Update(T item)
    {
        var all = _store.GetAll();
        var index = all.FindIndex(existing => GetId(existing) == GetId(item));
        if (index < 0)
            return false;

        all[index] = item;
        return _store.SaveAll(all);
    }

    /// <summary>Removes the record with this Id. False when there is none.</summary>
    public bool Delete(int id)
    {
        var all = _store.GetAll();
        if (all.RemoveAll(item => GetId(item) == id) == 0)
            return false;

        return _store.SaveAll(all);
    }

    private static PropertyInfo IdProperty =>
        typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(typeof(T).Name + " has no Id property.");

    private static int GetId(T item) => (int)(IdProperty.GetValue(item) ?? 0);

    private static void SetId(T item, int id) => IdProperty.SetValue(item, id);
}
""";

    /// <summary>Source of <c>Data/<paramref name="className"/>Repository.cs</c>.</summary>
    public static string Typed(string appName, string className) => $$"""
using {{appName}}.Models;

namespace {{appName}}.Data;

/// <summary>Typed CRUD for {{className}} records.</summary>
public sealed class {{className}}Repository : Repository<{{className}}>
{
}
""";
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~RepositoryTemplateTests"`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/RepositoryTemplate.cs CodeGenerator.Tests/App/RepositoryTemplateTests.cs
git commit -m "Add RepositoryTemplate (generic base + typed per-entity CRUD source)"
```

---

### Task 2: Reserve `"Repository"` so an entity of that name cannot clash with the base type

**Files:**
- Modify: `AppScaffolding/DomainModelMapper.cs`
- Modify: `CodeGenerator.Tests/App/DomainModelMapperTests.cs`

**Interfaces:**
- Produces: `DomainModelMapper.ToClassDefinitions` now renames an entity named `Repository` (case-insensitive) to `RepositoryItem`, exactly as it already does for `JsonStore`, `EntityViewModel`, `Data`, etc.

Rationale: the typed subclass for an entity named `Repository` would be `RepositoryRepository : Repository<Repository>`, and in `<App>.Data` the bare `Repository` binds to the generic base type in the same namespace rather than the `<App>.Models` entity — an uncompilable/ambiguous reference. Reserving the name avoids it.

- [ ] **Step 1: Write the failing test**

In `CodeGenerator.Tests/App/DomainModelMapperTests.cs`, add this method inside the existing `DomainModelMapperTests` class (e.g. after `ToClassDefinitions_RenamesEntitiesThatCollideWithTheGeneratedNamespaces`):

```csharp
    [TestMethod]
    public void ToClassDefinitions_RenamesEntity_NamedRepository()
    {
        // The generated app declares a base Repository<T> in <App>.Data. An entity also
        // called Repository would make `Repository<Repository>` bind its type argument to
        // that base type rather than the model, so the name is reserved.
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Repository"));

        Assert.AreEqual("RepositoryItem", classes[0].Name);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ToClassDefinitions_RenamesEntity_NamedRepository"`
Expected: FAIL — the entity is currently left as `Repository`, so `AreEqual("RepositoryItem", "Repository")` fails.

- [ ] **Step 3: Add `"Repository"` to `ReservedNames`**

In `AppScaffolding/DomainModelMapper.cs`, in the `ReservedNames` initializer, extend the block that lists the generated app's own types. Change:

```csharp
        // Types the generated app declares or references by simple name.
        "App", "Application", "MainWindow", "Window", "MainViewModel", "EntityViewModel",
        "IEntityViewModel", "ObservableObject", "RelayCommand", "ICommand", "JsonStore",
        "DataGrid", "TabControl", "ObservableCollection",
```

to:

```csharp
        // Types the generated app declares or references by simple name.
        "App", "Application", "MainWindow", "Window", "MainViewModel", "EntityViewModel",
        "IEntityViewModel", "ObservableObject", "RelayCommand", "ICommand", "JsonStore",
        "DataGrid", "TabControl", "ObservableCollection", "Repository",
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DomainModelMapperTests"`
Expected: PASS — the new test plus all existing mapper tests stay green.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/App/DomainModelMapperTests.cs
git commit -m "Reserve 'Repository' so a same-named entity cannot clash with the base type"
```

---

### Task 3: Write the repository files in `Scaffold`; build, test, and smoke-run

**Files:**
- Modify: `AppScaffolding/AppScaffolder.cs`

**Interfaces:**
- Consumes: `RepositoryTemplate.Base(appName)` and `RepositoryTemplate.Typed(appName, className)` (Task 1); the `entityNames` list already computed in `Scaffold`.
- Produces: `AppScaffolder.Scaffold` now also writes `Data/Repository.cs` and one `Data/<Entity>Repository.cs` per class, counted in `expected` and included in `ScaffoldResult.FilesWritten`; `Success` stays true only when every file (including these) was written.

This task's final deliverable — a project that still builds with the repositories present — needs the .NET SDK, so it is verified by a **manual compile smoke** in addition to the unit suite.

- [ ] **Step 1: Turn the fixed `files` array into a list and append the repository files**

In `AppScaffolding/AppScaffolder.cs`, inside `Scaffold`, find this block:

```csharp
        var files = new (string Folder, string Name, string Source)[]
        {
            (dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{appName}.Data")),
            (dataFolder, "RelationshipResolver.cs", RelationshipResolverTemplate.Source(appName)),
            (viewModelsFolder, "ObservableObject.cs", ViewModelTemplates.ObservableObject(appName)),
            (viewModelsFolder, "RelayCommand.cs", ViewModelTemplates.RelayCommand(appName)),
            (viewModelsFolder, "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(appName)),
            (viewModelsFolder, "EntityViewModel.cs", ViewModelTemplates.EntityViewModel(appName)),
            (viewModelsFolder, "MainViewModel.cs", MainViewModelTemplate.Build(appName, entityNames)),
            (viewsFolder, "MainWindow.xaml", MainWindowTemplate.Xaml(appName)),
            (viewsFolder, "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(appName)),
        };

        foreach (var (folder, name, source) in files)
```

Replace it with (array literal → `List<...>`, then append the base repo and one typed repo per entity):

```csharp
        var files = new List<(string Folder, string Name, string Source)>
        {
            (dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{appName}.Data")),
            (dataFolder, "RelationshipResolver.cs", RelationshipResolverTemplate.Source(appName)),
            (dataFolder, "Repository.cs", RepositoryTemplate.Base(appName)),
            (viewModelsFolder, "ObservableObject.cs", ViewModelTemplates.ObservableObject(appName)),
            (viewModelsFolder, "RelayCommand.cs", ViewModelTemplates.RelayCommand(appName)),
            (viewModelsFolder, "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(appName)),
            (viewModelsFolder, "EntityViewModel.cs", ViewModelTemplates.EntityViewModel(appName)),
            (viewModelsFolder, "MainViewModel.cs", MainViewModelTemplate.Build(appName, entityNames)),
            (viewsFolder, "MainWindow.xaml", MainWindowTemplate.Xaml(appName)),
            (viewsFolder, "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(appName)),
        };

        // One typed repository per entity, alongside the generic base above. Driven by the
        // class names (post-rename), the same list MainViewModel is built from.
        foreach (var name in entityNames)
            files.Add((dataFolder, $"{name}Repository.cs", RepositoryTemplate.Typed(appName, name)));

        foreach (var (folder, name, source) in files)
```

Leave the rest of the loop and the `return` untouched: `expected` is incremented once per file inside the loop, so the added entries are counted automatically, and `allWritten` already gates `Success` on `written.Count == expected`.

- [ ] **Step 2: Build the solution**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Run the full test suite (no regressions)**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: all PASS — the existing suite plus the 4 template tests (Task 1) and the 1 mapper test (Task 2). Treat "all green" as the gate.

- [ ] **Step 4: Manual smoke — generate an app, confirm the repo files, and build it**

The generated entity set is supplied by hand in the review loop (`add`/`field`/`approve`), so this smoke does not depend on what discovery returns. Run (bash), from the repo root `D:\SmartCodeGenerator-Repository`:

```bash
WS="$(mktemp -d)"; echo "workspace: $WS"

# Discovery runs first, then the review loop reads these lines: build a two-entity
# model by hand and approve it.
printf 'add Customer\nfield Customer FullName:string\nadd Order\nfield Order CustomerId:int\napprove\n' \
  | PROJECTPATH="$WS" dotnet run --project CodeGenerator -- "build app: shop"

APP="$(dirname "$(find "$WS" -name '*.csproj' | head -n1)")"
echo "generated app: $APP"

echo "--- Data/ folder ---"; ls "$APP/Data"

echo "--- build the generated app ---"
dotnet build "$APP"/*.csproj
```

Expected:
- `Data/` contains `Repository.cs`, `CustomerRepository.cs`, and `OrderRepository.cs` (alongside `JsonStore.cs` and `RelationshipResolver.cs`).
- `CustomerRepository.cs` contains `class CustomerRepository : Repository<Customer>`; `OrderRepository.cs` contains `class OrderRepository : Repository<Order>`.
- `dotnet build` of the generated project **succeeds** (0 errors) — the repositories compile even though the UI does not use them.

If discovery already produced entities named `Customer`/`Order`, the `add` for a duplicate is a no-op (`ModelEditor.AddEntity` rejects duplicates) and the smoke still generates those two repositories; the assertions above hold either way.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/AppScaffolder.cs
git commit -m "Write per-entity CRUD repositories alongside the scaffolded app"
```

---

## Self-Review

**Spec coverage:**
- Typed per-entity repository with `GetAll`/`GetById`/`Create`/`Update`/`Delete`, backed by `JsonStore<T>` → `RepositoryTemplate.Base` (Task 1). ✓
- Named `<Entity>Repository` subclass per entity → `RepositoryTemplate.Typed` + the per-entity loop in `Scaffold` (Tasks 1, 3). ✓
- String-template pattern, not `DomainModelMapper`/`MethodBodyGenerator`; POCO entities untouched → Task 1 is a standalone template; Task 3 only appends files. ✓
- Standalone surface; `JsonStore`/`EntityViewModel`/UI untouched; built on `GetAll`/`SaveAll`/`NextId` only → enforced by Global Constraints and `Base_PersistsThroughJsonStoresExistingApi` (Task 1). ✓
- Driven by class names post-rename → the loop iterates `entityNames` (= `classes.Select(c => c.Name)`) (Task 3). ✓
- Id read/written by reflection inside `Repository<T>`; `where T : class, new()` → `IdProperty`/`GetId`/`SetId` in `Base` (Task 1). ✓
- Collision guard: `"Repository"` reserved → Task 2. ✓
- Tests: template contents, mapper rename, compile smoke → Tasks 1, 2, 3. ✓
- Out of scope (no async/DI/interfaces, no extra finders, no UI change) → nothing in any task adds them. ✓

**Reconciliation note (spec testing section):** The spec mentioned a "scaffolder write test" asserting the repo files land in `Data/`. The codebase has no unit-test harness for `Scaffold` (it invokes `DotNetProjectFactory`/`dotnet new`); only `WriteClasses` is unit-tested. Consistent with that altitude, Task 3 verifies the wiring via the **manual compile smoke** (Step 4) rather than a new slow integration test. Unit coverage stays on the template (Task 1) and the mapper guard (Task 2).

**Placeholder scan:** No TBD/TODO/"similar to". Every code step shows full code; every command states expected output. ✓

**Type consistency:** `RepositoryTemplate.Base(string):string` and `RepositoryTemplate.Typed(string,string):string` are defined in Task 1 and called with those exact signatures in Task 3. Generated `Repository<T>` calls only `_store.GetAll()`, `_store.SaveAll(all)`, `_store.NextId(all)` — all present on `JsonStore<T>`. The typed subclass `<className>Repository : Repository<<className>>` matches the file name `<className>Repository.cs` written in Task 3. `ReservedNames` string `"Repository"` matches the base type name emitted in Task 1. Consistent across tasks. ✓

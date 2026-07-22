# EF Core + SQLite Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a generated app persist through EF Core + SQLite instead of JSON files, chosen at generation time (`approve <target> sqlite`), with JSON remaining the default.

**Architecture:** Introduce a shared `IStore<T>` seam that both `JsonStore<T>` (default) and a new `EfStore<T>` (SQLite) implement. `Repository<T>` and `SeedData` obtain their store from a generated `StoreFactory.Create<T>()` instead of constructing `JsonStore<T>` directly, so only the factory body differs per mode. A new `PersistenceKind` threads from the CLI into an `AppScaffolder.Scaffold` overload, which branches the emitted Data-core file set and injects the EF package reference for SQLite.

**Tech Stack:** C# / .NET 10, EF Core 10 (`Microsoft.EntityFrameworkCore.Sqlite`), MSTest, raw-string template generation.

## Global Constraints

- **Repo root is `D:\SmartCodeGenerator-Repository`** — the PARENT of the default tool cwd (`D:\SmartCodeGenerator-Repository\SmartCodeGenerator`). Git root is the repo root. Run all `git` and `dotnet` commands from the repo root. All file paths below are repo-root-relative.
- **Target framework:** `net10.0` (set by repo `Directory.Build.props`; `ImplicitUsings` and `Nullable` are `enable`).
- **Generated apps live in a temp workspace** outside the repo, so they do NOT inherit the repo `Directory.Build.props`. Generated `.csproj`s created by `dotnet new` on this SDK enable implicit usings, but every template still emits explicit `using` directives (match the existing templates — e.g. `JsonStoreTemplate`).
- **EF Core package:** `Microsoft.EntityFrameworkCore.Sqlite`, Version `10.0.10`. There is NO central package management (`Directory.Packages.props` does not exist), so the injected `PackageReference` MUST carry an explicit `Version`.
- **Test framework is MSTest**: `[TestClass]`, `[TestMethod]`, `[DataRow]`, `Assert.*`, `StringAssert.Contains`. Template tests assert on emitted source strings. Integration tests carry `[TestCategory("Integration")]` and shell out to `dotnet`.
- **Templates use `$$"""..."""` raw interpolated strings** with `{{...}}` placeholders. Match the existing style in `AppScaffolding/*Template.cs`.
- **New public types live in the `AppScaffolding` namespace** unless noted; template-emitted code lives in the generated app's `<projectName>.Data` / `<projectName>.Models` namespaces.
- **CI builds with `-warnaserror`** — emit no warnings (XML doc comments on public members, matching existing files).
- **Commit after every task.** Branch is `feat/ef-core-sqlite-persistence` (already created off `main`).

---

### Task 1: Shared `IStore<T>` seam; `JsonStore<T>` implements it

Introduce the seam interface and have `JsonStore<T>` implement it, and emit `IStore.cs` from the scaffolder. Nothing routes through the seam yet, so JSON generated apps compile and behave exactly as before.

**Files:**
- Create: `AppScaffolding/StoreSeamTemplate.cs`
- Modify: `AppScaffolding/JsonStoreTemplate.cs` (class declaration line 18)
- Modify: `AppScaffolding/AppScaffolder.cs` (data-core file list, ~lines 98–107)
- Create: `CodeGenerator.Tests/App/StoreSeamTemplateTests.cs`
- Modify: `CodeGenerator.Tests/App/JsonStoreTemplateTests.cs`

**Interfaces:**
- Produces: `StoreSeamTemplate.Source(string projectName) : string` — emits `Data/IStore.cs` declaring `namespace <projectName>.Data;` and `public interface IStore<T> where T : new()` with members `List<T> GetAll(); bool SaveAll(List<T> all); int NextId(IEnumerable<T> existing); T NewItem(IEnumerable<T> existing);`
- Produces: generated apps now contain `Data/IStore.cs`, and `JsonStore<T> : IStore<T>`.

- [ ] **Step 1: Write the failing test for the seam template**

Create `CodeGenerator.Tests/App/StoreSeamTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class StoreSeamTemplateTests
{
    [TestMethod]
    public void Source_EmitsIStoreInterfaceInTheGivenNamespace()
    {
        var source = StoreSeamTemplate.Source("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "public interface IStore<T> where T : new()");
    }

    [TestMethod]
    public void Source_DeclaresTheFourStoreMembers()
    {
        var source = StoreSeamTemplate.Source("Payroll");

        StringAssert.Contains(source, "List<T> GetAll();");
        StringAssert.Contains(source, "bool SaveAll(List<T> all);");
        StringAssert.Contains(source, "int NextId(IEnumerable<T> existing);");
        StringAssert.Contains(source, "T NewItem(IEnumerable<T> existing);");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run (from repo root): `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~StoreSeamTemplateTests"`
Expected: FAIL / build error — `StoreSeamTemplate` does not exist.

- [ ] **Step 3: Create the seam template**

Create `AppScaffolding/StoreSeamTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits the persistence seam shared by the JSON and EF Core stores.</summary>
public static class StoreSeamTemplate
{
    /// <summary>Source of <c>Data/IStore.cs</c> in <paramref name="projectName"/>.Data.</summary>
    public static string Source(string projectName) => $$"""
using System.Collections.Generic;

namespace {{projectName}}.Data;

/// <summary>Per-type list store keyed by an int Id. Implemented by JsonStore and EfStore, so the
/// front-end persistence code is identical whichever backend a build selects.</summary>
public interface IStore<T> where T : new()
{
    /// <summary>Every stored record.</summary>
    List<T> GetAll();

    /// <summary>Overwrites the store with <paramref name="all"/>. False if it could not be written.</summary>
    bool SaveAll(List<T> all);

    /// <summary>One past the highest Id in <paramref name="existing"/>, or 1 when there are none.</summary>
    int NextId(IEnumerable<T> existing);

    /// <summary>A new record with its Id already assigned.</summary>
    T NewItem(IEnumerable<T> existing);
}
""";
}
```

- [ ] **Step 4: Run the seam template tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~StoreSeamTemplateTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Write the failing test that JsonStore implements the seam**

Add to `CodeGenerator.Tests/App/JsonStoreTemplateTests.cs` a new test method inside the existing class:

```csharp
    [TestMethod]
    public void Source_JsonStoreImplementsIStore()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        StringAssert.Contains(source, "class JsonStore<T> : IStore<T> where T : new()");
    }
```

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~JsonStoreTemplateTests"`
Expected: FAIL — current source declares `class JsonStore<T> where T : new()` without `: IStore<T>`.

- [ ] **Step 7: Make JsonStore implement IStore**

In `AppScaffolding/JsonStoreTemplate.cs`, change the class declaration (line 18):

```csharp
public sealed class JsonStore<T> where T : new()
```
to:
```csharp
public sealed class JsonStore<T> : IStore<T> where T : new()
```

(The four existing members already match the interface signatures exactly, so no other change is needed. `IStore<T>` is in the same `<projectName>.Data` namespace, so no `using` is required.)

- [ ] **Step 8: Emit IStore.cs from the scaffolder**

In `AppScaffolding/AppScaffolder.cs`, in the `files` list (currently starting ~line 98), add `IStore.cs` as the first data-core file, before `JsonStore.cs`:

```csharp
        var files = new List<(string Folder, string Name, string Source)>
        {
            (dataFolder, "IStore.cs", StoreSeamTemplate.Source(projectName)),
            (dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{projectName}.Data")),
            (dataFolder, "Repository.cs", RepositoryTemplate.Base(projectName)),
            (dataFolder, "SeedData.cs",
                SeedDataTemplate.Source(
                    projectName,
                    SeedDataTemplate.OrderByDependency(classes),
                    SeedDataTemplate.ForeignKeyFieldNames(classes))),
        };
```

- [ ] **Step 9: Run the full CodeGenerator.Tests suite (unit) to verify nothing regressed**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "TestCategory!=Integration"`
Expected: PASS (existing tests + new ones green).

- [ ] **Step 10: Verify a generated JSON app still builds (spot integration)**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~GeneratedProject_Builds"`
Expected: PASS — generated console/webapi/classlib/worker projects still build with the added `IStore.cs`.

- [ ] **Step 11: Commit**

```bash
git add AppScaffolding/StoreSeamTemplate.cs AppScaffolding/JsonStoreTemplate.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/StoreSeamTemplateTests.cs CodeGenerator.Tests/App/JsonStoreTemplateTests.cs
git commit -m "feat: add IStore<T> persistence seam; JsonStore implements it"
```

---

### Task 2: `StoreFactory` + `PersistenceKind`; route Repository & SeedData through the factory

Add the factory (both mode bodies) and the `PersistenceKind` enum, emit `StoreFactory.cs` (JSON body) from the scaffolder, and reroute `Repository<T>` and `SeedData` off `new JsonStore<T>()` onto `StoreFactory.Create<T>()`. JSON apps still build and behave identically.

**Files:**
- Create: `AppScaffolding/PersistenceKind.cs`
- Create: `AppScaffolding/StoreFactoryTemplate.cs`
- Modify: `AppScaffolding/RepositoryTemplate.cs` (line 31 field + remark)
- Modify: `AppScaffolding/SeedDataTemplate.cs` (line 146)
- Modify: `AppScaffolding/AppScaffolder.cs` (data-core file list)
- Create: `CodeGenerator.Tests/App/StoreFactoryTemplateTests.cs`
- Modify: `CodeGenerator.Tests/App/RepositoryTemplateTests.cs`
- Modify: `CodeGenerator.Tests/App/SeedDataTemplateTests.cs` (only if it asserts `new JsonStore`)

**Interfaces:**
- Consumes: `IStore<T>` (Task 1).
- Produces: `enum PersistenceKind { Json, Sqlite }` in `AppScaffolding`.
- Produces: `StoreFactoryTemplate.Source(string projectName, PersistenceKind persistence) : string` — emits `Data/StoreFactory.cs` with `public static class StoreFactory { public static IStore<T> Create<T>() where T : class, new() => new JsonStore<T>(); }` (Json) or `=> new EfStore<T>();` (Sqlite).
- Produces: generated `Repository<T>` field is `private readonly IStore<T> _store = StoreFactory.Create<T>();`; `SeedData.Seed<T>` uses `IStore<T> store = StoreFactory.Create<T>();`.

- [ ] **Step 1: Create the PersistenceKind enum**

Create `AppScaffolding/PersistenceKind.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Which storage backend the generated Data core persists through.</summary>
public enum PersistenceKind
{
    /// <summary>Per-type JSON files (the default).</summary>
    Json,

    /// <summary>A SQLite database via EF Core.</summary>
    Sqlite,
}
```

- [ ] **Step 2: Write the failing tests for the factory template**

Create `CodeGenerator.Tests/App/StoreFactoryTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class StoreFactoryTemplateTests
{
    [TestMethod]
    public void Source_Json_CreatesJsonStore()
    {
        var source = StoreFactoryTemplate.Source("Payroll", PersistenceKind.Json);

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "public static class StoreFactory");
        StringAssert.Contains(source, "public static IStore<T> Create<T>() where T : class, new()");
        StringAssert.Contains(source, "new JsonStore<T>()");
    }

    [TestMethod]
    public void Source_Sqlite_CreatesEfStore()
    {
        var source = StoreFactoryTemplate.Source("Payroll", PersistenceKind.Sqlite);

        StringAssert.Contains(source, "new EfStore<T>()");
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~StoreFactoryTemplateTests"`
Expected: FAIL / build error — `StoreFactoryTemplate` does not exist.

- [ ] **Step 4: Create the factory template**

Create `AppScaffolding/StoreFactoryTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits <c>Data/StoreFactory.cs</c>: the single place that decides which
/// <see cref="IStore{T}"/> implementation the generated app persists through. Only its body
/// differs by <see cref="PersistenceKind"/>, so Repository and SeedData stay backend-agnostic.</summary>
public static class StoreFactoryTemplate
{
    /// <summary>Source of <c>Data/StoreFactory.cs</c> in <paramref name="projectName"/>.Data for
    /// the chosen <paramref name="persistence"/> backend.</summary>
    public static string Source(string projectName, PersistenceKind persistence)
    {
        var (backend, note) = persistence switch
        {
            PersistenceKind.Sqlite => ("new EfStore<T>()", "a SQLite database via EF Core"),
            _ => ("new JsonStore<T>()", "per-type JSON files"),
        };

        return $$"""
namespace {{projectName}}.Data;

/// <summary>Constructs the configured store for an entity type. This build persists to {{note}}.</summary>
public static class StoreFactory
{
    /// <summary>The store for entity type <typeparamref name="T"/>.</summary>
    public static IStore<T> Create<T>() where T : class, new() => {{backend}};
}
""";
    }
}
```

- [ ] **Step 5: Run the factory tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~StoreFactoryTemplateTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Update the Repository template test to expect the seam + factory**

In `CodeGenerator.Tests/App/RepositoryTemplateTests.cs`, replace the `Base_EmitsGenericRepositoryInTheGivenNamespace` test's `JsonStore<T>` assertion. Change:

```csharp
        StringAssert.Contains(source, "JsonStore<T>");
```
to:
```csharp
        StringAssert.Contains(source, "IStore<T> _store = StoreFactory.Create<T>();");
```

(Leave the `Base_PersistsThroughJsonStoresExistingApi` test as-is: it asserts `_store.GetAll()`, `_store.SaveAll(all)`, `_store.NextId(all)`, which are unchanged.)

- [ ] **Step 7: Run it to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~RepositoryTemplateTests"`
Expected: FAIL — `Base` still emits `private readonly JsonStore<T> _store = new();`.

- [ ] **Step 8: Reroute Repository through the factory**

In `AppScaffolding/RepositoryTemplate.cs`, change the field (line 31):

```csharp
    private readonly JsonStore<T> _store = new();
```
to:
```csharp
    private readonly IStore<T> _store = StoreFactory.Create<T>();
```

Also update the `<remarks>` on the `Repository<T>` class (lines 25–28) so it no longer claims a JsonStore-specific path. Replace that `<remarks>` block with:

```csharp
/// <remarks>
/// Persists through the store returned by <c>StoreFactory.Create&lt;T&gt;()</c> — JSON files by
/// default, or SQLite via EF Core when the app was generated with that backend.
/// </remarks>
```

- [ ] **Step 9: Reroute SeedData through the factory**

In `AppScaffolding/SeedDataTemplate.cs`, change line 146:

```csharp
        var store = new JsonStore<T>();
```
to:
```csharp
        IStore<T> store = StoreFactory.Create<T>();
```

- [ ] **Step 10: Emit StoreFactory.cs from the scaffolder (JSON body for now)**

In `AppScaffolding/AppScaffolder.cs`, add `StoreFactory.cs` to the `files` list, immediately after `JsonStore.cs`:

```csharp
            (dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{projectName}.Data")),
            (dataFolder, "StoreFactory.cs", StoreFactoryTemplate.Source(projectName, PersistenceKind.Json)),
```

- [ ] **Step 11: Run the unit suite to verify green**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "TestCategory!=Integration"`
Expected: PASS.

- [ ] **Step 12: Verify a generated JSON app still builds and its tests pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~GeneratedTestProject_BuildsAndPasses"`
Expected: PASS — generated apps route CRUD/seed through `StoreFactory` → `JsonStore` and still round-trip.

- [ ] **Step 13: Commit**

```bash
git add AppScaffolding/PersistenceKind.cs AppScaffolding/StoreFactoryTemplate.cs AppScaffolding/RepositoryTemplate.cs AppScaffolding/SeedDataTemplate.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/StoreFactoryTemplateTests.cs CodeGenerator.Tests/App/RepositoryTemplateTests.cs
git commit -m "feat: route Repository and SeedData through a StoreFactory seam"
```

---

### Task 3: `EfStore<T>` template

Emit the EF Core implementation of `IStore<T>`. This task only adds the template and its unit tests; nothing wires it into scaffolding yet, so generated apps are unaffected.

**Files:**
- Create: `AppScaffolding/EfStoreTemplate.cs`
- Create: `CodeGenerator.Tests/App/EfStoreTemplateTests.cs`

**Interfaces:**
- Consumes: `IStore<T>` (Task 1), `AppDbContext` (Task 4, referenced by name in emitted source only).
- Produces: `EfStoreTemplate.Source(string projectName) : string` — emits `Data/EfStore.cs` declaring `public sealed class EfStore<T> : IStore<T> where T : class, new()`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/EfStoreTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class EfStoreTemplateTests
{
    [TestMethod]
    public void Source_EmitsEfStoreImplementingIStore()
    {
        var source = EfStoreTemplate.Source("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "using Microsoft.EntityFrameworkCore;");
        StringAssert.Contains(source, "class EfStore<T> : IStore<T> where T : class, new()");
    }

    [TestMethod]
    public void Source_UsesAFreshDbContextPerCall()
    {
        var source = EfStoreTemplate.Source("Payroll");

        StringAssert.Contains(source, "using var context = new AppDbContext();");
        StringAssert.Contains(source, "context.Set<T>().AsNoTracking().ToList()");
    }

    [TestMethod]
    public void Source_SaveAllReplacesTheWholeStore()
    {
        var source = EfStoreTemplate.Source("Payroll");

        // Faithful to JsonStore.SaveAll's overwrite contract: clear then insert.
        StringAssert.Contains(source, "set.RemoveRange(set.ToList());");
        StringAssert.Contains(source, "set.AddRange(all);");
        StringAssert.Contains(source, "context.SaveChanges();");
    }

    [TestMethod]
    public void Source_AssignsIdsClientSideLikeJsonStore()
    {
        var source = EfStoreTemplate.Source("Payroll");

        StringAssert.Contains(source, "ids.Count == 0 ? 1 : ids.Max() + 1");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~EfStoreTemplateTests"`
Expected: FAIL / build error — `EfStoreTemplate` does not exist.

- [ ] **Step 3: Create the EfStore template**

Create `AppScaffolding/EfStoreTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits <c>Data/EfStore.cs</c>: the EF Core + SQLite implementation of
/// <see cref="IStore{T}"/>. Deliberately faithful to JsonStore's contract — client-assigned int
/// Ids and whole-store SaveAll semantics — so Repository and SeedData need no behavioral change.</summary>
public static class EfStoreTemplate
{
    /// <summary>Source of <c>Data/EfStore.cs</c> in <paramref name="projectName"/>.Data.</summary>
    public static string Source(string projectName) => $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace {{projectName}}.Data;

/// <summary>SQLite-backed store keyed by an int Id. One table per type. A fresh context per call
/// mirrors JsonStore's stateless per-call file access.</summary>
public sealed class EfStore<T> : IStore<T> where T : class, new()
{
    /// <summary>Every stored record.</summary>
    public List<T> GetAll()
    {
        using var context = new AppDbContext();
        return context.Set<T>().AsNoTracking().ToList();
    }

    /// <summary>Overwrites the store with <paramref name="all"/> (clear then insert), matching
    /// JsonStore.SaveAll. False if the write failed.</summary>
    public bool SaveAll(List<T> all)
    {
        try
        {
            using var context = new AppDbContext();
            var set = context.Set<T>();
            set.RemoveRange(set.ToList());
            set.AddRange(all);
            context.SaveChanges();
            return true;
        }
        catch (DbUpdateException)
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

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~EfStoreTemplateTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/EfStoreTemplate.cs CodeGenerator.Tests/App/EfStoreTemplateTests.cs
git commit -m "feat: emit EfStore<T>, the EF Core + SQLite IStore backend"
```

---

### Task 4: `AppDbContext` template

Emit the generated `AppDbContext`: a `DbSet` per entity, SQLite connection in the Data core, client-assigned keys, scalar-only mapping (navigation/collection properties ignored, matching JSON's non-relational storage), and `EnsureCreated()` on construction. Template + unit tests only.

**Files:**
- Create: `AppScaffolding/DbContextTemplate.cs`
- Create: `CodeGenerator.Tests/App/DbContextTemplateTests.cs`

**Interfaces:**
- Consumes: nothing from prior tasks (emits standalone source referencing `<projectName>.Models`).
- Produces: `DbContextTemplate.Source(string projectName, IReadOnlyList<string> entityNames, string envVarName) : string` — emits `Data/AppDbContext.cs` with `public sealed class AppDbContext : DbContext`, one `public DbSet<Entity> Entity => Set<Entity>();` per entity name, `OnConfiguring` using `UseSqlite`, `OnModelCreating` applying `ValueGeneratedNever()` and ignoring non-scalar properties, and a constructor calling `Database.EnsureCreated()`. The connection string reads env var `envVarName` (a full SQLite connection string) or falls back to `Data Source=<projectName>.db` under `AppContext.BaseDirectory`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/DbContextTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DbContextTemplateTests
{
    private static string Sample() =>
        DbContextTemplate.Source("Shop", new[] { "Customer", "Order" }, "SHOP_DB");

    [TestMethod]
    public void Source_EmitsContextWithModelsUsingAndSqlite()
    {
        var source = Sample();

        StringAssert.Contains(source, "namespace Shop.Data");
        StringAssert.Contains(source, "using Shop.Models;");
        StringAssert.Contains(source, "using Microsoft.EntityFrameworkCore;");
        StringAssert.Contains(source, "class AppDbContext : DbContext");
        StringAssert.Contains(source, "optionsBuilder.UseSqlite(ConnectionString())");
    }

    [TestMethod]
    public void Source_EmitsOneDbSetPerEntity()
    {
        var source = Sample();

        StringAssert.Contains(source, "public DbSet<Customer> Customer => Set<Customer>();");
        StringAssert.Contains(source, "public DbSet<Order> Order => Set<Order>();");
    }

    [TestMethod]
    public void Source_KeepsKeysClientAssignedAndMapsScalarsOnly()
    {
        var source = Sample();

        StringAssert.Contains(source, "Property(\"Id\").ValueGeneratedNever();");
        StringAssert.Contains(source, "if (!IsScalar(prop.PropertyType))");
        StringAssert.Contains(source, "entity.Ignore(prop.Name);");
    }

    [TestMethod]
    public void Source_CreatesTheDatabaseAndHonorsTheEnvOverride()
    {
        var source = Sample();

        StringAssert.Contains(source, "Database.EnsureCreated();");
        StringAssert.Contains(source, "Environment.GetEnvironmentVariable(\"SHOP_DB\")");
        StringAssert.Contains(source, "\"Data Source=\" + Path.Combine(AppContext.BaseDirectory, \"Shop.db\")");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DbContextTemplateTests"`
Expected: FAIL / build error — `DbContextTemplate` does not exist.

- [ ] **Step 3: Create the DbContext template**

Create `AppScaffolding/DbContextTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits <c>Data/AppDbContext.cs</c>: a SQLite context with one table per entity. Maps
/// scalar columns only — navigation and collection properties are ignored — so storage stays flat
/// and non-relational, matching JsonStore (a JSON store enforces no foreign keys either). Ids are
/// client-assigned, and the database is created on first use.</summary>
public static class DbContextTemplate
{
    /// <summary>Source of <c>Data/AppDbContext.cs</c> in <paramref name="projectName"/>.Data with a
    /// DbSet per entity in <paramref name="entityNames"/>. <paramref name="envVarName"/> names an
    /// environment variable holding a full SQLite connection string that overrides the default.</summary>
    public static string Source(
        string projectName, IReadOnlyList<string> entityNames, string envVarName)
    {
        var dbSets = string.Join("\n",
            entityNames.Select(n => $"    public DbSet<{n}> {n} => Set<{n}>();"));

        return $$"""
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using {{projectName}}.Models;

namespace {{projectName}}.Data;

/// <summary>SQLite database context. One table per entity, scalar columns only (navigation
/// properties are ignored to match JsonStore's flat storage). Client-assigned int Ids. Creates the
/// database file on first use.</summary>
public sealed class AppDbContext : DbContext
{
{{dbSets}}

    /// <summary>Opens the context and ensures the database and schema exist.</summary>
    public AppDbContext()
    {
        Database.EnsureCreated();
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlite(ConnectionString());

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Snapshot the entity types: Ignore() below mutates the model, so iterating the live
        // collection could throw.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clr = entityType.ClrType;
            var entity = modelBuilder.Entity(clr);

            // Ids come from the store (NextId), never the database.
            entity.Property("Id").ValueGeneratedNever();

            // Map plain columns only; drop entity refs and collections so an unset foreign key is
            // just a 0 (as in JSON), never a constraint that fails an insert.
            foreach (var prop in clr.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (!IsScalar(prop.PropertyType))
                    entity.Ignore(prop.Name);
        }

        base.OnModelCreating(modelBuilder);
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid);
    }

    private static string ConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("{{envVarName}}");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return "Data Source=" + Path.Combine(AppContext.BaseDirectory, "{{projectName}}.db");
    }
}
""";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DbContextTemplateTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/DbContextTemplate.cs CodeGenerator.Tests/App/DbContextTemplateTests.cs
git commit -m "feat: emit AppDbContext (SQLite, scalar-only, client-assigned Ids)"
```

---

### Task 5: `.csproj` package-reference injection helper

Add a small helper that injects a `PackageReference` into a `.csproj` string, idempotently. Unit tests only.

**Files:**
- Create: `AppScaffolding/CsprojEditing.cs`
- Create: `CodeGenerator.Tests/App/CsprojEditingTests.cs`

**Interfaces:**
- Produces: `CsprojEditing.InjectPackageReference(string csproj, string packageId, string version) : string` — returns the csproj with a `<PackageReference Include="packageId" Version="version" />` item group inserted before `</Project>`; returns the input unchanged if it already references `packageId` or has no `</Project>`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/CsprojEditingTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class CsprojEditingTests
{
    private const string Csproj =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup></PropertyGroup>\n</Project>\n";

    [TestMethod]
    public void InjectPackageReference_InsertsBeforeClosingTag()
    {
        var result = CsprojEditing.InjectPackageReference(Csproj, "Microsoft.EntityFrameworkCore.Sqlite", "10.0.10");

        StringAssert.Contains(result,
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"10.0.10\" />");
        Assert.IsTrue(
            result.IndexOf("PackageReference", StringComparison.Ordinal)
                < result.IndexOf("</Project>", StringComparison.Ordinal),
            "the reference must be inside the project element");
    }

    [TestMethod]
    public void InjectPackageReference_IsIdempotentForTheSamePackage()
    {
        var once = CsprojEditing.InjectPackageReference(Csproj, "Microsoft.EntityFrameworkCore.Sqlite", "10.0.10");
        var twice = CsprojEditing.InjectPackageReference(once, "Microsoft.EntityFrameworkCore.Sqlite", "10.0.10");

        Assert.AreEqual(once, twice);
    }

    [TestMethod]
    public void InjectPackageReference_LeavesInputUnchangedWithoutClosingTag()
    {
        var result = CsprojEditing.InjectPackageReference("garbage without a project tag", "X", "1.0.0");

        Assert.AreEqual("garbage without a project tag", result);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CsprojEditingTests"`
Expected: FAIL / build error — `CsprojEditing` does not exist.

- [ ] **Step 3: Create the helper**

Create `AppScaffolding/CsprojEditing.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Minimal text edits to a project file's XML, mirroring
/// <see cref="TestProjectTemplates.InjectProjectReference"/>.</summary>
public static class CsprojEditing
{
    /// <summary>Inserts a <c>PackageReference</c> for <paramref name="packageId"/> just before the
    /// closing <c>&lt;/Project&gt;</c> tag. Idempotent: returns <paramref name="csproj"/> unchanged
    /// if it already references the package or has no closing tag.</summary>
    public static string InjectPackageReference(string csproj, string packageId, string version)
    {
        if (csproj.Contains($"Include=\"{packageId}\""))
            return csproj;

        var itemGroup =
            "  <ItemGroup>\n" +
            $"    <PackageReference Include=\"{packageId}\" Version=\"{version}\" />\n" +
            "  </ItemGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, itemGroup);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CsprojEditingTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/CsprojEditing.cs CodeGenerator.Tests/App/CsprojEditingTests.cs
git commit -m "feat: add idempotent csproj PackageReference injection helper"
```

---

### Task 6: Wire SQLite into `AppScaffolder`

Add a `PersistenceKind` overload to `Scaffold`, branch the Data-core file set (SQLite emits `EfStore.cs` + `AppDbContext.cs` + a SQLite `StoreFactory`, and omits `JsonStore.cs`), and inject the EF package into the app `.csproj`.

**Files:**
- Modify: `AppScaffolding/AppScaffolder.cs`
- Create: `CodeGenerator.Tests/App/AppScaffolderPersistenceTests.cs`

**Interfaces:**
- Consumes: `StoreSeamTemplate` (T1), `StoreFactoryTemplate`/`PersistenceKind` (T2), `EfStoreTemplate` (T3), `DbContextTemplate` (T4), `CsprojEditing` (T5).
- Produces: `AppScaffolder.Scaffold(CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target, PersistenceKind persistence) : ScaffoldResult`. The existing 4-arg `Scaffold(model, appName, workspaceRoot, target)` and 3-arg (WPF) overloads delegate to it with `PersistenceKind.Json`. Constants: `EfCorePackageId = "Microsoft.EntityFrameworkCore.Sqlite"`, `EfCorePackageVersion = "10.0.10"`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/AppScaffolderPersistenceTests.cs`:

```csharp
using System.IO;
using System.Linq;
using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppScaffolderPersistenceTests
{
    private static CandidateDomainModel OneEntity()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Book",
            Fields = { new CandidateField { Name = "Title", Type = "string" } }
        });
        return model;
    }

    private static (string Project, string[] DataFiles, string Csproj) Scaffold(PersistenceKind kind)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var result = new AppScaffolder().Scaffold(OneEntity(), "Sample", workspace, target, kind);
        Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

        var dataFolder = Path.Combine(result.ProjectPath, "Data");
        var dataFiles = Directory.GetFiles(dataFolder).Select(Path.GetFileName).ToArray()!;
        var csproj = File.ReadAllText(
            Path.Combine(result.ProjectPath, Path.GetFileName(result.ProjectPath) + ".csproj"));
        return (result.ProjectPath, dataFiles!, csproj);
        // NOTE: the caller does not delete the workspace; MSTest temp dirs are transient.
    }

    [TestMethod]
    public void Sqlite_EmitsEfCoreFilesAndOmitsJsonStore()
    {
        var (_, dataFiles, csproj) = Scaffold(PersistenceKind.Sqlite);

        CollectionAssert.Contains(dataFiles, "EfStore.cs");
        CollectionAssert.Contains(dataFiles, "AppDbContext.cs");
        CollectionAssert.Contains(dataFiles, "IStore.cs");
        CollectionAssert.Contains(dataFiles, "StoreFactory.cs");
        CollectionAssert.DoesNotContain(dataFiles, "JsonStore.cs");

        StringAssert.Contains(csproj,
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"10.0.10\" />");
    }

    [TestMethod]
    public void Sqlite_StoreFactoryCreatesEfStore()
    {
        var (project, _, _) = Scaffold(PersistenceKind.Sqlite);
        var factory = File.ReadAllText(Path.Combine(project, "Data", "StoreFactory.cs"));

        StringAssert.Contains(factory, "new EfStore<T>()");
    }

    [TestMethod]
    public void Json_EmitsJsonStoreAndNoEfCoreFilesOrPackage()
    {
        var (_, dataFiles, csproj) = Scaffold(PersistenceKind.Json);

        CollectionAssert.Contains(dataFiles, "JsonStore.cs");
        CollectionAssert.DoesNotContain(dataFiles, "EfStore.cs");
        CollectionAssert.DoesNotContain(dataFiles, "AppDbContext.cs");
        Assert.IsFalse(csproj.Contains("Microsoft.EntityFrameworkCore.Sqlite"),
            "JSON mode must not reference EF Core");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~AppScaffolderPersistenceTests"`
Expected: FAIL / build error — the 5-arg `Scaffold` overload does not exist.

- [ ] **Step 3: Add the package constants and the persistence-aware Scaffold overload**

In `AppScaffolding/AppScaffolder.cs`, add constants near the top of the class:

```csharp
    private const string EfCorePackageId = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string EfCorePackageVersion = "10.0.10";
```

Change the existing 4-arg `Scaffold` method signature to the 5-arg version and add the persistence parameter. Replace the current method declaration:

```csharp
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target)
    {
```
with:
```csharp
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target)
        => Scaffold(model, appName, workspaceRoot, target, PersistenceKind.Json);

    /// <summary>As <see cref="Scaffold(CandidateDomainModel,string,string,ITargetScaffolder)"/>, but
    /// persisting through the chosen <paramref name="persistence"/> backend. SQLite emits an EF Core
    /// Data core (EfStore + AppDbContext) in place of JsonStore and adds the EF Core package.</summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target,
        PersistenceKind persistence)
    {
```

- [ ] **Step 4: Inject the EF package after the project exists**

In the same method, immediately after the `if (!File.Exists(csprojPath)) { ... target.FixupProject(...); }` block (currently ~line 85), add:

```csharp
        if (persistence == PersistenceKind.Sqlite)
            InjectEfCorePackage(csprojPath);
```

- [ ] **Step 5: Branch the Data-core file list on the persistence kind**

Replace the fixed `files` initializer (currently ~lines 98–109) with a kind-aware build. The `IStore.cs`, `StoreFactory.cs`, `Repository.cs`, `SeedData.cs`, and typed repositories are common; the backend files differ:

```csharp
        // Shared, target-agnostic core.
        var files = new List<(string Folder, string Name, string Source)>
        {
            (dataFolder, "IStore.cs", StoreSeamTemplate.Source(projectName)),
            (dataFolder, "StoreFactory.cs", StoreFactoryTemplate.Source(projectName, persistence)),
            (dataFolder, "Repository.cs", RepositoryTemplate.Base(projectName)),
            (dataFolder, "SeedData.cs",
                SeedDataTemplate.Source(
                    projectName,
                    SeedDataTemplate.OrderByDependency(classes),
                    SeedDataTemplate.ForeignKeyFieldNames(classes))),
        };

        // Persistence backend behind the seam.
        if (persistence == PersistenceKind.Sqlite)
        {
            files.Add((dataFolder, "EfStore.cs", EfStoreTemplate.Source(projectName)));
            files.Add((dataFolder, "AppDbContext.cs",
                DbContextTemplate.Source(projectName, entityNames, EnvVarName(projectName))));
        }
        else
        {
            files.Add((dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{projectName}.Data")));
        }

        foreach (var name in entityNames)
            files.Add((dataFolder, $"{name}Repository.cs", RepositoryTemplate.Typed(projectName, name)));
```

- [ ] **Step 6: Add the private helpers**

Add these private static methods to the `AppScaffolder` class (e.g. after `WriteSource`):

```csharp
    /// <summary>Best-effort insertion of the EF Core SQLite package into the app project file.
    /// Idempotent and silent on failure — a missing package surfaces later as a build error.</summary>
    private static void InjectEfCorePackage(string csprojPath)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            var updated = CsprojEditing.InjectPackageReference(
                File.ReadAllText(csprojPath), EfCorePackageId, EfCorePackageVersion);
            File.WriteAllText(csprojPath, updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Environment-variable name the generated AppDbContext reads for a connection-string
    /// override: the upper-cased project name with non-alphanumerics as underscores, plus "_DB".</summary>
    private static string EnvVarName(string projectName)
    {
        var chars = projectName.ToUpperInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_');
        return new string(chars.ToArray()) + "_DB";
    }
```

(`System.Linq` is available via implicit usings in the `AppScaffolding` project.)

- [ ] **Step 7: Run the persistence tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~AppScaffolderPersistenceTests"`
Expected: PASS (3 tests).

- [ ] **Step 8: Run the full unit suite to verify nothing regressed**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "TestCategory!=Integration"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/AppScaffolderPersistenceTests.cs
git commit -m "feat: scaffold an EF Core + SQLite Data core when persistence is sqlite"
```

---

### Task 7: CLI — `approve <target> sqlite`

Parse the optional persistence token on the `approve` command, reject `sqlite` for the WPF target, and thread the choice into the scaffolder. Update the review-loop help line.

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs`
- Create: `CodeGenerator.Tests/App/BuildAppPersistenceParsingTests.cs`

**Interfaces:**
- Consumes: `PersistenceKind` (T2), the 5-arg `AppScaffolder.Scaffold` (T6), `ITargetScaffolder.Key` (existing).
- Produces: `internal static PersistenceKind? BuildAppCommand.ResolvePersistence(string[] parts)` — `Json` when no token (`parts.Length < 3`), `Json`/`Sqlite` for `"json"`/`"sqlite"` at `parts[2]` (case-insensitive), `null` for any other token. Produces: `internal static bool BuildAppCommand.IsPersistenceAllowed(ITargetScaffolder target, PersistenceKind persistence)` — `false` only when `persistence == Sqlite && target.Key == "wpf"`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/BuildAppPersistenceParsingTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppPersistenceParsingTests
{
    [TestMethod]
    public void ResolvePersistence_DefaultsToJsonWithoutAToken()
    {
        Assert.AreEqual(PersistenceKind.Json,
            BuildAppCommand.ResolvePersistence(new[] { "approve", "console" }));
    }

    [TestMethod]
    public void ResolvePersistence_ReadsSqliteAndJsonCaseInsensitively()
    {
        Assert.AreEqual(PersistenceKind.Sqlite,
            BuildAppCommand.ResolvePersistence(new[] { "approve", "console", "SQLite" }));
        Assert.AreEqual(PersistenceKind.Json,
            BuildAppCommand.ResolvePersistence(new[] { "approve", "console", "json" }));
    }

    [TestMethod]
    public void ResolvePersistence_ReturnsNullForAnUnknownToken()
    {
        Assert.IsNull(BuildAppCommand.ResolvePersistence(new[] { "approve", "console", "postgres" }));
    }

    [TestMethod]
    public void IsPersistenceAllowed_RejectsSqliteForWpfOnly()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var console));

        Assert.IsFalse(BuildAppCommand.IsPersistenceAllowed(wpf, PersistenceKind.Sqlite));
        Assert.IsTrue(BuildAppCommand.IsPersistenceAllowed(wpf, PersistenceKind.Json));
        Assert.IsTrue(BuildAppCommand.IsPersistenceAllowed(console, PersistenceKind.Sqlite));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildAppPersistenceParsingTests"`
Expected: FAIL / build error — the helpers do not exist.

- [ ] **Step 3: Add the parsing helpers**

In `CodeGenerator/App/BuildAppCommand.cs`, add these `internal static` methods (near `ResolveTarget`). Add `using AppScaffolding;` at the top if not present:

```csharp
    /// <summary>The persistence backend an <c>approve</c> line selects. No third token means JSON;
    /// <c>sqlite</c>/<c>json</c> (case-insensitively) select explicitly; any other token is invalid
    /// (returns null).</summary>
    internal static PersistenceKind? ResolvePersistence(string[] parts)
    {
        if (parts.Length < 3)
            return PersistenceKind.Json;

        return parts[2].ToLowerInvariant() switch
        {
            "sqlite" => PersistenceKind.Sqlite,
            "json" => PersistenceKind.Json,
            _ => null,
        };
    }

    /// <summary>SQLite is available for every target except WPF, whose view models read JsonStore
    /// directly (off the shared seam).</summary>
    internal static bool IsPersistenceAllowed(ITargetScaffolder target, PersistenceKind persistence)
        => !(persistence == PersistenceKind.Sqlite
             && string.Equals(target.Key, "wpf", StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 4: Run the helper tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildAppPersistenceParsingTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Wire the helpers into the approve handler**

In `BuildAppCommand.RunReviewLoop`, in the `case "a": case "approve":` block, after the target is resolved and validated (after the `if (target is null) { ... break; }` block, ~line 93), insert persistence parsing and the WPF guard, then pass the kind to `Scaffold`:

```csharp
                    var persistence = ResolvePersistence(parts);
                    if (persistence is null)
                    {
                        Console.WriteLine("Unknown persistence option. Valid: json, sqlite.");
                        break;
                    }

                    if (!IsPersistenceAllowed(target, persistence.Value))
                    {
                        Console.WriteLine(
                            "SQLite persistence isn't supported for the WPF target yet — it stays JSON.");
                        break;
                    }

                    var scaffold = new AppScaffolder()
                        .Scaffold(model, appName, workspaceRoot, target, persistence.Value);
```

(Replace the existing `var scaffold = new AppScaffolder().Scaffold(model, appName, workspaceRoot, target);` line with the version above.)

- [ ] **Step 6: Update the review-loop help line**

In `RunReviewLoop`, change the help line (currently ~line 59):

```csharp
            Console.WriteLine("  [a]pprove [target]   add <name>   remove <name>   rename <old> <new>");
```
to:
```csharp
            Console.WriteLine("  [a]pprove [target] [sqlite]   add <name>   remove <name>   rename <old> <new>");
```

- [ ] **Step 7: Build the CLI and run the full unit suite**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "TestCategory!=Integration"`
Expected: PASS. (This also compiles `CodeGenerator`, confirming the `Scaffold` call wiring.)

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/BuildAppPersistenceParsingTests.cs
git commit -m "feat: accept 'approve <target> sqlite' and reject it for WPF"
```

---

### Task 8: Integration — generated SQLite apps build, seed, and round-trip

Extend the existing generated-output integration test with a SQLite variant for the seeding entry-point targets (console, webapi, worker). The generated app's own companion test project runs its CRUD round-trip and seed tests against a real SQLite database.

**Files:**
- Modify: `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`

**Interfaces:**
- Consumes: the 5-arg `AppScaffolder.Scaffold` (T6).

- [ ] **Step 1: Add the failing SQLite integration test**

In `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`, add a new test method (alongside the existing ones), and add `using AppScaffolding;` at the top if not already present:

```csharp
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("worker")]
    public void GeneratedSqliteProject_BuildsSeedsAndRoundTrips(string targetKey)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder()
                .Scaffold(SampleModel(), "Sample", workspace, target, PersistenceKind.Sqlite);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");

            // The generated companion tests exercise Repository + SeedData, which now route through
            // EfStore -> SQLite. A green run proves the schema is created, seeded, and round-tripped.
            var (exit, output) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet test failed for {targetKey} (sqlite):\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }
```

- [ ] **Step 2: Run the SQLite integration test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~GeneratedSqliteProject"`
Expected: PASS (3 data rows). First run restores `Microsoft.EntityFrameworkCore.Sqlite` 10.0.10 from nuget.org — network is required. If the generated test fails, read the captured `output`: a `DbUpdateException` or a missing-table error points at the `AppDbContext` mapping (Task 4); a restore error points at the package version/id (Task 6).

- [ ] **Step 3: Run the whole suite (unit + integration) to confirm nothing regressed**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS — the pre-existing JSON integration rows and all new tests are green.

- [ ] **Step 4: Commit**

```bash
git add CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs
git commit -m "test: dotnet-test generated SQLite apps (build, seed, round-trip)"
```

---

## Notes for the implementer

- **Scope clarification:** SQLite is allowed for every target except WPF (see Task 7). This includes `classlib` (it uses the shared `Repository`/seam and has no seam-bypassing front-end), even though the integration coverage in Task 8 focuses on the three entry-point targets that also run `SeedData.EnsureSeeded()`.
- **Why scalar-only EF mapping (Task 4):** JSON stores enforce no referential integrity, so `repository.Create(new T())` with an unset foreign key (a `0`) always succeeds. Mapping navigation/collection properties in EF would create FK constraints that fail that same insert. Ignoring non-scalars keeps `EfStore` a faithful drop-in for the `JsonStore` seam — the relational analogue of the replace-all `SaveAll` decision.
- **Replace-all `SaveAll` (Task 3):** deliberately clears and re-inserts to match `JsonStore.SaveAll`'s overwrite contract. Do not "optimize" it into per-row upserts — that would diverge from the seam and force `Repository`/`SeedData` changes this plan avoids.
- **Rebuild note:** unit tests build everything they touch. If you manually drive the CLI to eyeball `approve console sqlite`, rebuild `CodeGenerator` first (`dotnet build CodeGenerator/CodeGenerator.csproj`) — a `dotnet test` run can leave the CLI's `bin` stale.
```

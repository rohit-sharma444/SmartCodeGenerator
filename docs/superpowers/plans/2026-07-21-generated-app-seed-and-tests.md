# Generated-App Seed Data & Companion Test Project — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every generated app seed itself with sample data on first run and ship a companion xUnit test project that exercises (and thereby verifies) the shared Models/Repository/Seed core.

**Architecture:** A new reflection-based `SeedData` class is emitted into the `Data/` core (seed-if-empty, type-driven values, FK ints resolved to real parent Ids, parents seeded first via a generation-time-ordered call sequence). Each of the four entry points calls `SeedData.EnsureSeeded()`. The scaffolder gains a second-project step that emits `<app>.Tests` (xUnit, `ProjectReference` to the app, TFM matched) with CRUD + seed tests. CI is extended to `dotnet test` the generated test project.

**Tech Stack:** C# / .NET 10; generated tests use xUnit; `CodeGenerator.Tests` (the harness) uses MSTest; `dotnet new` / `dotnet build` / `dotnet test` via child process.

## Global Constraints

- Target framework `net10.0` (WPF app + its test project: `net10.0-windows`); `Nullable`/`ImplicitUsings` enable.
- `CodeGenerator.Tests` uses **MSTest** (`[TestClass]`/`[TestMethod]`/`[DataRow]`/`Assert.*`); generated test projects use **xUnit** (`[Fact]`/`Assert.*`).
- Our solution CI builds with `-warnaserror` (0 warnings). Generated projects are built with `dotnet build -c Release` (not `-warnaserror`), but keep generated source warning-clean.
- Seed is **seed-if-empty** and idempotent; never duplicates on re-run.
- `SeedRowCount = 3`.
- Foreign key convention (from Feature 1): an `int` property named `<Target>Id` (PascalCase, ends in ordinal `"Id"`, not `"Id"` itself) whose sibling nav property `<Target>` is typed as a generated entity class. A FK whose target was renamed (`Type`→`TypeItem`) degrades cleanly (seed value `0`) — same known limitation Feature 1 documented; do **not** try to solve renames here.
- Mined/seeded data must not overwrite existing data; existing single-project callers (`ScaffoldResult.ProjectPath`) must keep working.
- Source lives under `D:\SmartCodeGenerator-Repository` (git root = PARENT of the tool cwd). Run all `git`/`dotnet` from there. Branch: `feat/generated-app-seed-and-tests`.

---

### Task 1: `SeedData` template, dependency ordering, and emission

**Files:**
- Create: `AppScaffolding/SeedDataTemplate.cs`
- Modify: `AppScaffolding/AppScaffolder.cs:98-104` (add `SeedData.cs` to the emitted core files)
- Test: `CodeGenerator.Tests/App/SeedDataTemplateTests.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Models.ClassDefinition` (`.Name`, `.Properties` with `.Name`/`.Type`).
- Produces:
  - `SeedDataTemplate.OrderByDependency(IReadOnlyList<ClassDefinition>) : IReadOnlyList<string>` — class names, referenced (parent) entities before their referencers.
  - `SeedDataTemplate.Source(string projectName, IReadOnlyList<string> orderedEntityNames) : string` — the full source of `Data/SeedData.cs`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/SeedDataTemplateTests.cs`:

```csharp
using System.Collections.Generic;
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SeedDataTemplateTests
{
    private static ClassDefinition Class(string name, params PropertyDefinition[] props)
    {
        var def = new ClassDefinition { Name = name };
        foreach (var p in props) def.Properties.Add(p);
        return def;
    }

    private static PropertyDefinition Prop(string name, string type) =>
        new() { Name = name, Type = type };

    [TestMethod]
    public void OrderByDependency_PutsReferencedEntityBeforeItsReferencer()
    {
        // Order references Customer (CustomerId int + Customer nav), so Customer must seed first.
        var order = Class("Order",
            Prop("Id", "int"), Prop("CustomerId", "int"), Prop("Customer", "Customer"));
        var customer = Class("Customer", Prop("Id", "int"), Prop("Name", "string"));

        var ordered = SeedDataTemplate.OrderByDependency(new List<ClassDefinition> { order, customer });

        CollectionAssert.AreEqual(new[] { "Customer", "Order" }, (System.Collections.ICollection)ordered);
    }

    [TestMethod]
    public void OrderByDependency_ToleratesNoRelationships()
    {
        var a = Class("Alpha", Prop("Id", "int"));
        var b = Class("Beta", Prop("Id", "int"));

        var ordered = SeedDataTemplate.OrderByDependency(new List<ClassDefinition> { a, b });

        Assert.AreEqual(2, ordered.Count);
        CollectionAssert.Contains((System.Collections.ICollection)ordered, "Alpha");
        CollectionAssert.Contains((System.Collections.ICollection)ordered, "Beta");
    }

    [TestMethod]
    public void Source_EmitsSeedCallsInOrder_AndSeedRowCount()
    {
        var src = SeedDataTemplate.Source("Shop", new[] { "Customer", "Order" });

        StringAssert.Contains(src, "namespace Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "public const int SeedRowCount = 3;");
        StringAssert.Contains(src, "Seed<Customer>(seededIds);");
        StringAssert.Contains(src, "Seed<Order>(seededIds);");
        // Customer's Seed call precedes Order's.
        Assert.IsLessThan(src.IndexOf("Seed<Order>"), src.IndexOf("Seed<Customer>"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~SeedDataTemplateTests"`
Expected: FAIL — `SeedDataTemplate` does not exist (compile error / type not found).

- [ ] **Step 3: Implement `SeedDataTemplate`**

Create `AppScaffolding/SeedDataTemplate.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// Emits <c>Data/SeedData.cs</c>: a reflection-based seeder that populates every empty store
/// with sample rows on first run. Consistent with JsonStore/Repository, which already reflect
/// on Id. Seed-if-empty and idempotent, so a running app never accrues duplicate sample data.
/// </summary>
public static class SeedDataTemplate
{
    /// <summary>
    /// Entity class names in seed order: an entity referenced by a foreign key is placed before
    /// the entity that references it, so a child's FK can point at a parent row that already
    /// exists. A dependency cycle degrades to declaration order.
    /// </summary>
    public static IReadOnlyList<string> OrderByDependency(IReadOnlyList<ClassDefinition> classes)
    {
        var names = new HashSet<string>(classes.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var parents = classes.ToDictionary(
            c => c.Name, c => ForeignKeyTargets(c, names), StringComparer.OrdinalIgnoreCase);

        var ordered = new List<string>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool progress = true;
        while (progress && placed.Count < classes.Count)
        {
            progress = false;
            foreach (var c in classes)
            {
                if (placed.Contains(c.Name))
                    continue;

                // Placeable once every parent is already placed (a self-reference never blocks).
                if (parents[c.Name].All(p =>
                        placed.Contains(p) || string.Equals(p, c.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    ordered.Add(c.Name);
                    placed.Add(c.Name);
                    progress = true;
                }
            }
        }

        // Any remaining (a cycle) fall back to declaration order.
        foreach (var c in classes)
            if (placed.Add(c.Name))
                ordered.Add(c.Name);

        return ordered;
    }

    /// <summary>The class names this class references via a <c>&lt;Target&gt;Id</c> foreign key
    /// whose sibling nav property is typed as that target entity.</summary>
    private static IReadOnlyList<string> ForeignKeyTargets(ClassDefinition c, HashSet<string> allNames)
    {
        var targets = new List<string>();
        foreach (var p in c.Properties)
        {
            if (p.Type != "int" || p.Name.Length <= 2)
                continue;
            if (!p.Name.EndsWith("Id", StringComparison.Ordinal)
                || string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
                continue;

            var target = p.Name[..^2];
            if (allNames.Contains(target)
                && !string.Equals(target, c.Name, StringComparison.OrdinalIgnoreCase)
                && c.Properties.Any(n => string.Equals(n.Type, target, StringComparison.Ordinal)))
                targets.Add(target);
        }
        return targets;
    }

    /// <summary>Source of <c>Data/SeedData.cs</c> in <paramref name="projectName"/>.Data, seeding
    /// the entities in <paramref name="orderedEntityNames"/> order.</summary>
    public static string Source(string projectName, IReadOnlyList<string> orderedEntityNames)
    {
        var seedCalls = string.Join("\n",
            orderedEntityNames.Select(n => $"        Seed<{n}>(seededIds);"));

        return $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using {{projectName}}.Models;

namespace {{projectName}}.Data;

/// <summary>
/// Populates empty stores with sample rows on first run. Seed-if-empty and idempotent: a store
/// that already holds data is left untouched, so re-running never accrues duplicates. This is a
/// starting point — edit the sample values, or delete this file, as the app grows.
/// </summary>
public static class SeedData
{
    /// <summary>Rows inserted into each empty store.</summary>
    public const int SeedRowCount = 3;

    /// <summary>Seeds every empty store, referenced entities before their referencers so a
    /// foreign key points at a row that exists.</summary>
    public static void EnsureSeeded()
    {
        var seededIds = new Dictionary<string, List<int>>();
{{seedCalls}}
    }

    private static void Seed<T>(Dictionary<string, List<int>> seededIds) where T : class, new()
    {
        var store = new JsonStore<T>();
        var existing = store.GetAll();
        if (existing.Count > 0)
        {
            seededIds[typeof(T).Name] = existing.Select(GetId).ToList();
            return;
        }

        var rows = new List<T>();
        var ids = new List<int>();
        for (var n = 1; n <= SeedRowCount; n++)
        {
            var item = new T();
            foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite)
                    continue;

                if (string.Equals(prop.Name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    prop.SetValue(item, n);
                    continue;
                }

                var value = SampleValue(prop, n, seededIds);
                if (value is not null)
                    prop.SetValue(item, value);
            }

            rows.Add(item);
            ids.Add(n);
        }

        store.SaveAll(rows);
        seededIds[typeof(T).Name] = ids;
    }

    private static object? SampleValue(
        PropertyInfo prop, int n, Dictionary<string, List<int>> seededIds)
    {
        var type = prop.PropertyType;
        var name = prop.Name;

        // Foreign key: an int named <Target>Id -> an existing seeded <Target> row's Id.
        if (type == typeof(int) && name.Length > 2 && name.EndsWith("Id", StringComparison.Ordinal))
        {
            var target = name.Substring(0, name.Length - 2);
            if (seededIds.TryGetValue(target, out var parentIds) && parentIds.Count > 0)
                return parentIds[(n - 1) % parentIds.Count];
            return 0;
        }

        if (type == typeof(string))
        {
            if (name.Contains("Email", StringComparison.OrdinalIgnoreCase))
                return $"user{n}@example.com";
            if (name.Contains("Phone", StringComparison.OrdinalIgnoreCase))
                return $"555-010{n}";
            return $"{name} {n}";
        }

        if (type == typeof(int)) return n;
        if (type == typeof(decimal)) return n * 10 + 0.99m;
        if (type == typeof(double)) return n * 10 + 0.99;
        if (type == typeof(DateTime)) return DateTime.Today.AddDays(-n);
        if (type == typeof(bool)) return n % 2 == 0;

        // Navigation properties (entity types, List<>) and anything else are left at default.
        return null;
    }

    private static int GetId<T>(T item)
    {
        var id = typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return (int)(id?.GetValue(item) ?? 0);
    }
}
""";
    }
}
```

- [ ] **Step 4: Emit `SeedData.cs` from the scaffolder**

In `AppScaffolding/AppScaffolder.cs`, in `Scaffold`, add `SeedData.cs` to the shared core file list (the block starting `var files = new List<...>` at `:98`). Add this entry after the `Repository.cs` entry:

```csharp
            (dataFolder, "SeedData.cs",
                SeedDataTemplate.Source(projectName, SeedDataTemplate.OrderByDependency(classes))),
```

(`classes` is already in scope from `:61`.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~SeedDataTemplateTests"`
Expected: PASS (3/3). (Emission into `Data/` and that the generated source compiles are proven end-to-end by Task 4's build.)

- [ ] **Step 6: Build the solution warning-free**

Run: `dotnet build AppScaffolding/AppScaffolding.csproj -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/SeedDataTemplate.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/SeedDataTemplateTests.cs
git commit -m "feat: emit reflection-based SeedData into the generated Data core

Seed-if-empty, type-driven sample values, FK ints resolved to real parent
Ids; parents seeded first via generation-time dependency ordering. Emitted
for every target; not yet invoked at entry points (Task 2).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Invoke `EnsureSeeded()` at the four entry points

**Files:**
- Modify: `AppScaffolding/ConsoleProgramTemplate.cs`
- Modify: `AppScaffolding/MainViewModelTemplate.cs`
- Modify: `AppScaffolding/WebApiProgramTemplate.cs`
- Modify: `AppScaffolding/WorkerProgramTemplate.cs`
- Test: `CodeGenerator.Tests/App/SeedInvocationTests.cs`

**Interfaces:**
- Consumes: `SeedData.EnsureSeeded()` from Task 1 (generated into `<proj>.Data`).
- Produces: no new API; each entry-point template's emitted source now calls `SeedData.EnsureSeeded()`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/SeedInvocationTests.cs`:

```csharp
using System.Collections.Generic;
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SeedInvocationTests
{
    private static readonly IReadOnlyList<string> Entities = new[] { "Customer", "Order" };

    [TestMethod]
    public void ConsoleProgram_SeedsBeforeTheMenuLoop()
    {
        var src = ConsoleProgramTemplate.Build("Shop", Entities);
        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        // Seeds before building the repositories map.
        Assert.IsLessThan(src.IndexOf("var repositories"), src.IndexOf("SeedData.EnsureSeeded();"));
    }

    [TestMethod]
    public void WpfMainViewModel_SeedsBeforeTheViewModelsLoad()
    {
        var src = MainViewModelTemplate.Build("Shop", Entities);
        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        // Must seed before the ObservableCollection is built (each EntityViewModel loads its store).
        Assert.IsLessThan(src.IndexOf("new ObservableCollection"), src.IndexOf("SeedData.EnsureSeeded();"));
    }

    [TestMethod]
    public void WebApiProgram_SeedsBeforeRun()
    {
        var src = WebApiProgramTemplate.Build("Shop", Entities);
        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        Assert.IsLessThan(src.IndexOf("app.Run();"), src.IndexOf("SeedData.EnsureSeeded();"));
    }

    [TestMethod]
    public void WorkerProgram_SeedsBeforeRun_AndImportsDataNamespace()
    {
        var src = WorkerProgramTemplate.Source("Shop");
        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        Assert.IsLessThan(src.IndexOf("builder.Build().Run();"), src.IndexOf("SeedData.EnsureSeeded();"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~SeedInvocationTests"`
Expected: FAIL — no template emits `SeedData.EnsureSeeded();` yet.

- [ ] **Step 3: Add the call to the console template**

In `AppScaffolding/ConsoleProgramTemplate.cs`, in the returned raw string, insert the seed call right after the `using` block and before the `// typeof(Entity)` comment:

```csharp
using {{projectName}};
using {{projectName}}.Data;
using {{projectName}}.Models;

SeedData.EnsureSeeded();

// typeof(Entity) -> that entity's stored rows, so a menu can offer a picker for a foreign key.
```

- [ ] **Step 4: Add the call to the WPF MainViewModel template**

In `AppScaffolding/MainViewModelTemplate.cs`, make the constructor body start with the seed call:

```csharp
    public MainViewModel()
    {
        // Seed sample data before the view-models load: each EntityViewModel<T> reads its own
        // store in its constructor, so seeding after this point would leave the first run empty.
        SeedData.EnsureSeeded();

        Entities = new ObservableCollection<IEntityViewModel>
```

(`using {{appName}}.Data;` is already present.)

- [ ] **Step 5: Add the call to the WebApi template**

In `AppScaffolding/WebApiProgramTemplate.cs`, insert the seed call after `var app = builder.Build();`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

SeedData.EnsureSeeded();

{{{registrations}}}

app.Run();
```

(`using {{{projectName}}}.Data;` is already present.)

- [ ] **Step 6: Add the call + Data using to the Worker template**

In `AppScaffolding/WorkerProgramTemplate.cs`, add the Data using and the seed call:

```csharp
using {{projectName}};
using {{projectName}}.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

SeedData.EnsureSeeded();

builder.Build().Run();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~SeedInvocationTests"`
Expected: PASS (4/4).

- [ ] **Step 8: Commit**

```bash
git add AppScaffolding/ConsoleProgramTemplate.cs AppScaffolding/MainViewModelTemplate.cs AppScaffolding/WebApiProgramTemplate.cs AppScaffolding/WorkerProgramTemplate.cs CodeGenerator.Tests/App/SeedInvocationTests.cs
git commit -m "feat: invoke SeedData.EnsureSeeded() at each generated entry point

Console (before the menu loop), WPF (start of MainViewModel ctor, before the
view-models load their stores), WebApi (before app.Run), Worker (before host
Run; adds the Data using). ClassLibrary has no entry point and is unchanged.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Companion test-project scaffolding

**Files:**
- Modify: `CodeGenerationEngine/Models/DotNetProjectType.cs` (add `XUnitTest`)
- Modify: `CodeGenerationEngine/Factory/DotNetProjectFactory.cs:102-117` (`GetTemplate` → `"xunit"`)
- Create: `AppScaffolding/TestProjectTemplates.cs` (test file sources + `.csproj` ProjectReference injector)
- Modify: `AppScaffolding/ScaffoldResult.cs` (add `TestProjectPath`)
- Modify: `AppScaffolding/AppScaffolder.cs` (emit the test project)
- Test: `CodeGenerator.Tests/App/TestProjectTemplatesTests.cs`

**Interfaces:**
- Consumes: `DotNetProjectFactory.CreateProject`, `ProjectRequest`, `SeedData`/`Repository`/typed repos from the generated app.
- Produces:
  - `DotNetProjectType.XUnitTest`.
  - `TestProjectTemplates.RoundTripTests(string projectName, IReadOnlyList<string> entityNames) : string`
  - `TestProjectTemplates.SeedTests(string projectName, string firstEntityName) : string`
  - `TestProjectTemplates.DisableParallelization() : string`
  - `TestProjectTemplates.InjectProjectReference(string csproj, string relativeCsprojPath) : string`
  - `ScaffoldResult.TestProjectPath` (nullable).

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/TestProjectTemplatesTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TestProjectTemplatesTests
{
    [TestMethod]
    public void RoundTripTests_EmitsAFactPerEntity_UsingTheTypedRepository()
    {
        var src = TestProjectTemplates.RoundTripTests("Shop", new[] { "Customer", "Order" });

        StringAssert.Contains(src, "namespace Shop.Tests;");
        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "public void Customer_round_trips() => RoundTrip(new CustomerRepository());");
        StringAssert.Contains(src, "public void Order_round_trips() => RoundTrip(new OrderRepository());");
        StringAssert.Contains(src, "repository.Delete(id)");
    }

    [TestMethod]
    public void SeedTests_AssertsPopulatedAndIdempotent()
    {
        var src = TestProjectTemplates.SeedTests("Shop", "Customer");

        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        StringAssert.Contains(src, "new CustomerRepository().GetAll().Count");
        StringAssert.Contains(src, "Assert.Equal(afterFirst, afterSecond);");
    }

    [TestMethod]
    public void DisableParallelization_EmitsTheAssemblyAttribute()
    {
        StringAssert.Contains(
            TestProjectTemplates.DisableParallelization(),
            "[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]");
    }

    [TestMethod]
    public void InjectProjectReference_AddsAReferenceItemGroupBeforeTheClosingProjectTag()
    {
        var csproj = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n  </PropertyGroup>\n</Project>\n";

        var result = TestProjectTemplates.InjectProjectReference(csproj, @"..\Shop\Shop.csproj");

        StringAssert.Contains(result, "<ProjectReference Include=\"..\\Shop\\Shop.csproj\" />");
        Assert.IsLessThan(result.IndexOf("</Project>"), result.IndexOf("<ProjectReference"));
        Assert.IsTrue(result.TrimEnd().EndsWith("</Project>"), "closing tag preserved");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~TestProjectTemplatesTests"`
Expected: FAIL — `TestProjectTemplates` does not exist.

- [ ] **Step 3: Add the xUnit project type**

In `CodeGenerationEngine/Models/DotNetProjectType.cs`, add `XUnitTest` to the enum (after `Mvc`):

```csharp
    Mvc,
    XUnitTest
```

In `CodeGenerationEngine/Factory/DotNetProjectFactory.cs`, add to the `GetTemplate` switch (before the `_ =>` arm):

```csharp
            DotNetProjectType.XUnitTest => "xunit",
```

- [ ] **Step 4: Implement `TestProjectTemplates`**

Create `AppScaffolding/TestProjectTemplates.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits the sources of the companion <c>&lt;app&gt;.Tests</c> xUnit project, and the
/// one edit needed to point its .csproj at the app project.</summary>
public static class TestProjectTemplates
{
    /// <summary>A self-cleaning CRUD round-trip over each entity's typed repository. Asserts on the
    /// row it creates (not on store counts), so the tests tolerate seeded data and each other.</summary>
    public static string RoundTripTests(string projectName, IReadOnlyList<string> entityNames)
    {
        var facts = string.Join("\n\n", entityNames.Select(name => $$"""
    [Fact]
    public void {{name}}_round_trips() => RoundTrip(new {{name}}Repository());
"""));

        return $$"""
using System.Reflection;
using {{projectName}}.Data;
using {{projectName}}.Models;
using Xunit;

namespace {{projectName}}.Tests;

/// <summary>CRUD round-trip over each entity's typed repository. Each test creates a row and
/// deletes it, asserting on that row rather than on store counts.</summary>
public class RepositoryRoundTripTests
{
{{facts}}

    private static void RoundTrip<T>(Repository<T> repository) where T : class, new()
    {
        var created = repository.Create(new T());
        var id = IdOf(created);
        Assert.NotEqual(0, id);
        Assert.NotNull(repository.GetById(id));
        Assert.True(repository.Update(created));
        Assert.True(repository.Delete(id));
        Assert.Null(repository.GetById(id));
    }

    private static int IdOf<T>(T item)
    {
        var id = typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return (int)(id?.GetValue(item) ?? 0);
    }
}
""";
    }

    /// <summary>Asserts EnsureSeeded populates the first entity's store and is idempotent.</summary>
    public static string SeedTests(string projectName, string firstEntityName) => $$"""
using {{projectName}}.Data;
using Xunit;

namespace {{projectName}}.Tests;

/// <summary>SeedData populates empty stores on first run and does nothing on the second.</summary>
public class SeedDataTests
{
    [Fact]
    public void EnsureSeeded_populates_stores_and_is_idempotent()
    {
        SeedData.EnsureSeeded();
        var afterFirst = new {{firstEntityName}}Repository().GetAll().Count;
        Assert.True(afterFirst > 0);

        SeedData.EnsureSeeded();
        var afterSecond = new {{firstEntityName}}Repository().GetAll().Count;
        Assert.Equal(afterFirst, afterSecond);
    }
}
""";

    /// <summary>Disables xUnit parallelization: the JSON stores are shared files, so the tests
    /// run sequentially to stay deterministic.</summary>
    public static string DisableParallelization() => """
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
""";

    /// <summary>Inserts a ProjectReference to the app project into a freshly-generated test
    /// .csproj, just before the closing &lt;/Project&gt; tag.</summary>
    public static string InjectProjectReference(string csproj, string relativeCsprojPath)
    {
        var itemGroup =
            "  <ItemGroup>\n" +
            $"    <ProjectReference Include=\"{relativeCsprojPath}\" />\n" +
            "  </ItemGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, itemGroup);
    }
}
```

- [ ] **Step 5: Add `TestProjectPath` to `ScaffoldResult`**

Replace `AppScaffolding/ScaffoldResult.cs` body with:

```csharp
namespace AppScaffolding;

/// <summary>Outcome of scaffolding: the project folder, the files written, and (when a companion
/// test project was emitted) its project path.</summary>
public sealed record ScaffoldResult(
    bool Success,
    string ProjectPath,
    IReadOnlyList<string> FilesWritten,
    string Message,
    string? TestProjectPath = null);
```

- [ ] **Step 6: Emit the test project from `AppScaffolder.Scaffold`**

In `AppScaffolding/AppScaffolder.cs`, add a `using CodeGenerationEngine.Factory;`/`using CodeGenerationEngine.Models;` if not present (they are — `DotNetProjectFactory`/`DotNetProjectType` are already used via `DotNetProjectFactory` at `:69`). Then, in `Scaffold`, after `var allWritten = written.Count == expected;` and before building `message`, add the test-project step, and thread the returned path into the result. Replace the tail of `Scaffold` (from `var allWritten = ...` to the final `return`) with:

```csharp
        var testProjectPath = ScaffoldTestProject(workspaceRoot, projectName, entityNames);

        var allWritten = written.Count == expected;

        var message = allWritten
            ? $"Generated {classes.Count} class(es) + {target.Description}."
            : $"Generated {written.Count} of {expected} file(s); " +
              "some files could not be written.";

        return new ScaffoldResult(allWritten, outputFolder, written, message, testProjectPath);
    }

    /// <summary>
    /// Creates a sibling <c>&lt;appProject&gt;.Tests</c> xUnit project referencing the app project,
    /// with generated CRUD + seed tests. Best-effort: returns null (leaving the app itself intact)
    /// if the test project could not be created. The test project's TFM mirrors the app's, so a
    /// WPF app's tests target net10.0-windows and resolve the reference.
    /// </summary>
    private static string? ScaffoldTestProject(
        string workspaceRoot, string appProjectName, IReadOnlyList<string> entityNames)
    {
        if (entityNames.Count == 0)
            return null;

        var testProjectName = appProjectName + ".Tests";
        var testFolder = Path.Combine(workspaceRoot, testProjectName);
        var testCsproj = Path.Combine(testFolder, testProjectName + ".csproj");
        var appCsproj = Path.Combine("..", appProjectName, appProjectName + ".csproj");

        var framework = AppProjectFramework(workspaceRoot, appProjectName);

        if (!File.Exists(testCsproj))
        {
            var created = DotNetProjectFactory.CreateProject(new ProjectRequest
            {
                ProjectName = testProjectName,
                OutputFolder = testFolder,
                ProjectType = DotNetProjectType.XUnitTest,
                Framework = framework
            });

            if (!created.Success)
                return null;

            // dotnet new xunit emits a UnitTest1.cs placeholder; replace it with our tests.
            var placeholder = Path.Combine(testFolder, "UnitTest1.cs");
            try { if (File.Exists(placeholder)) File.Delete(placeholder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            try
            {
                File.WriteAllText(testCsproj,
                    TestProjectTemplates.InjectProjectReference(File.ReadAllText(testCsproj), appCsproj));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        WriteSource(testFolder, "RepositoryRoundTripTests.cs",
            TestProjectTemplates.RoundTripTests(appProjectName, entityNames));
        WriteSource(testFolder, "SeedDataTests.cs",
            TestProjectTemplates.SeedTests(appProjectName, entityNames[0]));
        WriteSource(testFolder, "TestParallelization.cs",
            TestProjectTemplates.DisableParallelization());

        return testCsproj;
    }

    /// <summary>The app project's &lt;TargetFramework&gt;, so the test project matches it
    /// (net10.0-windows for WPF). Falls back to net10.0 if it cannot be read.</summary>
    private static string AppProjectFramework(string workspaceRoot, string appProjectName)
    {
        try
        {
            var csproj = File.ReadAllText(
                Path.Combine(workspaceRoot, appProjectName, appProjectName + ".csproj"));
            var open = csproj.IndexOf("<TargetFramework>", StringComparison.Ordinal);
            var close = csproj.IndexOf("</TargetFramework>", StringComparison.Ordinal);
            if (open >= 0 && close > open)
            {
                open += "<TargetFramework>".Length;
                return csproj.Substring(open, close - open).Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return "net10.0";
    }
```

Note: this makes the whole `Scaffold` method still end correctly — the inserted `}` after the `return new ScaffoldResult(...)` closes `Scaffold`, and the two new private methods follow, before the existing backward-compatible `Scaffold(model, appName, workspaceRoot)` overload at `:129`. Verify the brace balance after editing.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~TestProjectTemplatesTests"`
Expected: PASS (4/4).

- [ ] **Step 8: Build the solution warning-free**

Run: `dotnet build AppScaffolding/AppScaffolding.csproj -warnaserror`
Expected: Build succeeded, 0 warnings. If the `Scaffold` edit unbalanced braces, fix before proceeding.

- [ ] **Step 9: Commit**

```bash
git add CodeGenerationEngine/Models/DotNetProjectType.cs CodeGenerationEngine/Factory/DotNetProjectFactory.cs AppScaffolding/TestProjectTemplates.cs AppScaffolding/ScaffoldResult.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/TestProjectTemplatesTests.cs
git commit -m "feat: scaffold a companion <app>.Tests xUnit project

New XUnitTest project type; AppScaffolder emits a sibling test project that
references the app (TFM matched), replaces the dotnet-new placeholder with
CRUD round-trip + seed idempotency tests, and disables parallelization for
the shared JSON stores. ScaffoldResult gains TestProjectPath.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Integration — build and `dotnet test` the generated output

**Files:**
- Modify: `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`

**Interfaces:**
- Consumes: `AppScaffolder.Scaffold` (now emits the test project + `ScaffoldResult.TestProjectPath`), and the existing `RunDotNet`/`TryDeleteDirectory` helpers.
- Produces: no API; a new integration test proving generated app + test project build and the tests pass end-to-end.

- [ ] **Step 1: Write the failing test**

In `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`, add this test method (after `GeneratedConsoleProject_WithRelationship_Builds`, before `RunDotNet`):

```csharp
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("classlib")]
    [DataRow("worker")]
    public void GeneratedTestProject_BuildsAndPasses(string targetKey)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(SampleModel(), "Sample", workspace, target);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");

            var (exit, output) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet test failed for {targetKey}:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }
```

WPF is deliberately excluded from the `DataRow` set (it needs a Windows host, exactly as `GeneratedProject_Builds` excludes it).

- [ ] **Step 2: Run the test to verify it passes (or fails honestly)**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~GeneratedTestProject_BuildsAndPasses"`
Expected: PASS for all four targets. `dotnet test` builds the test project (which builds the referenced app, exercising SeedData + repositories + the generated tests) and runs the xUnit tests. This is the end-to-end proof of Tasks 1–3.

If a target fails, read the captured `output`: a compile error means a generated-source bug (fix the relevant template in Task 1/3); a test failure means a seed/repository logic bug. Do not weaken the assertion — fix the generated code.

**Likely failure mode (webapi/worker):** a plain `Microsoft.NET.Sdk` test project that references an ASP.NET (`Microsoft.NET.Sdk.Web`) or Worker (`Microsoft.NET.Sdk.Worker`) app can fail to build with a missing-framework error (e.g. `NETSDK1080`/unresolved `Microsoft.AspNetCore.App`). If this occurs, the fix is in Task 3's test-project generation, not here: have the test project add the matching `<FrameworkReference Include="Microsoft.AspNetCore.App" />` when the referenced app is a WebApi (extend `InjectProjectReference`/`ScaffoldTestProject` to pass the target's needs), or the Worker's framework reference for worker. Since our generated tests only touch the Data/Models core, keeping the reference minimal is preferred; add a framework reference only if the build actually demands it. Report this as DONE_WITH_CONCERNS if you had to add one, so the reviewer sees the deviation.

- [ ] **Step 3: Run the full harness suite for regressions**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS — existing `GeneratedProject_Builds` still green (each generated project now also contains `SeedData.cs` and still builds), plus the new integration test.

- [ ] **Step 4: Commit**

```bash
git add CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs
git commit -m "test: dotnet-test the generated companion test project (integration)

Scaffolds console/webapi/classlib/worker, then runs dotnet test on the
emitted <app>.Tests project, proving seed + repositories + generated tests
build and pass end-to-end. WPF stays Windows-only excluded.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** Part A (SeedData, ordering, values, FK) → Task 1; entry-point invocation incl. WPF ordering → Task 2; Part B (new project type, test project, ProjectReference, TFM match, generated tests, ScaffoldResult) → Task 3; CI `dotnet test` on generated output → Task 4. Deferred items (EF Core, rename-FK fix, .sln) are in Global Constraints / untouched.
- **Type consistency:** `SeedDataTemplate.Source(string, IReadOnlyList<string>)` / `OrderByDependency(IReadOnlyList<ClassDefinition>)`, `TestProjectTemplates.*`, `ScaffoldResult.TestProjectPath`, `DotNetProjectType.XUnitTest` are used identically across tasks. `ConsoleProgramTemplate.Build`/`MainViewModelTemplate.Build`/`WebApiProgramTemplate.Build`/`WorkerProgramTemplate.Source` match the real signatures verified in the sources.
- **Placeholders:** none — every code and command step is concrete.
- **Known risk (flagged, not a placeholder):** the WPF companion test project targets `net10.0-windows` and can only build on Windows; it is excluded from the Linux integration matrix exactly as the WPF app is, and verified manually on Windows like prior WPF work. If referencing a `UseWPF` app from the test project needs `<UseWPF>` too, that is a Task-3 fixup the implementer applies (add it in `InjectProjectReference` for the windows TFM) — surface as DONE_WITH_CONCERNS if hit.
```

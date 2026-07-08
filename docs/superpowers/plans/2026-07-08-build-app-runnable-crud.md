# `build app:` Runnable JSON-Persisted CRUD App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `build app:` **approve** action generate a console app that runs — a menu-driven, JSON-persisted CRUD over the approved entities — so `dotnet run` produces a working application.

**Architecture:** Extend the existing `AppScaffolding` library. `DomainModelMapper` guarantees every class has an `int Id` key. Two new string-template producers emit a generic `JsonStore<T>` (per-type JSON persistence via `System.Text.Json`, keyed off `Id` by reflection) and a console-menu `Program.cs` (a generic reflection-based CRUD engine dispatched per entity). `AppScaffolder.Scaffold` writes these two files alongside the entity classes. Everything the generated app contains lives in the `GeneratedCode` namespace, so no change is needed to the shared `RoslynClassGenerator`.

**Tech Stack:** C# / .NET 10 (`net10.0`). Generated app uses only `System.Text.Json` and `System.ComponentModel` from the shared framework (no added packages). Tests in `CodeGenerator.Tests` use **MSTest**.

## Global Constraints

- All projects target `net10.0`. The generated project is a **console** app on `net10.0`.
- `AppScaffolding`'s root namespace is `AppScaffolding`. The **generated app's** code (entities, `JsonStore<T>`, `Program.cs`) is all in the `GeneratedCode` namespace — the fixed namespace `RoslynClassGenerator` already emits. Do **not** change `RoslynClassGenerator`.
- The generated app must compile with **no added NuGet packages** — only `System.Text.Json` and `System.ComponentModel` (both in the shared framework).
- Do NOT add a DomainDiscovery↔CodeGenerationEngine dependency — the bridge stays only in `AppScaffolding`.
- `CodeGenerator.Tests` is MSTest: `[TestClass]` / `[TestMethod]`, `Assert.*`, `CollectionAssert.*`, `StringAssert.*`. `ImplicitUsings` is enabled with a global `using Microsoft.VisualStudio.TestTools.UnitTesting;`.
- Run all commands from the repository root: `D:\SmartCodeGenerator-Repository`.
- Commit after each task. Work stays on the current branch `feat/build-app-runnable-crud`.

## Reference: existing signatures (do not redefine)

- `AppScaffolding.DomainModelMapper.ToClassDefinitions(CandidateDomainModel) : IReadOnlyList<ClassDefinition>` — **modified** in Task 1.
- `AppScaffolding.AppScaffolder.WriteClasses(string, IReadOnlyList<ClassDefinition>) : IReadOnlyList<string>` — unchanged.
- `AppScaffolding.AppScaffolder.Scaffold(CandidateDomainModel, string, string) : ScaffoldResult` — **modified** in Task 4.
- `AppScaffolding.ScaffoldResult(bool Success, string ProjectPath, IReadOnlyList<string> FilesWritten, string Message)` — unchanged.
- `CodeGenerationEngine.Models.ClassDefinition` → `string Name`, `List<PropertyDefinition> Properties`, `List<MethodDefinition> Methods`.
- `PropertyDefinition` → `string Name`, `string Type`.
- `DomainDiscovery.Model.CandidateDomainModel` → `List<CandidateEntity> Entities`; `CandidateEntity` → `required string Name`, `List<CandidateField> Fields`; `CandidateField` → `required string Name`, `required string Type`.
- `RoslynClassGenerator.Generate(ClassDefinition)` emits a `public` class in `namespace GeneratedCode` with a public parameterless constructor and public get/set properties (used by the generated app for `new T()` and JSON round-trip).

## File Structure

- Modify: `AppScaffolding/DomainModelMapper.cs` — guaranteed `int Id` (Task 1).
- Create: `AppScaffolding/JsonStoreTemplate.cs` — emits `JsonStore.cs` source (Task 2).
- Create: `AppScaffolding/ProgramTemplate.cs` — emits `Program.cs` source (Task 3).
- Modify: `AppScaffolding/AppScaffolder.cs` — write the two support files in `Scaffold` (Task 4).
- Modify: `CodeGenerator.Tests/App/DomainModelMapperTests.cs` — update + add mapper tests (Task 1).
- Create: `CodeGenerator.Tests/App/JsonStoreTemplateTests.cs` (Task 2).
- Create: `CodeGenerator.Tests/App/ProgramTemplateTests.cs` (Task 3).

---

### Task 1: `DomainModelMapper` guarantees an `int Id`

**Files:**
- Modify: `AppScaffolding/DomainModelMapper.cs`
- Modify: `CodeGenerator.Tests/App/DomainModelMapperTests.cs`

**Interfaces:**
- Produces: `DomainModelMapper.ToClassDefinitions(CandidateDomainModel) : IReadOnlyList<ClassDefinition>` — every returned `ClassDefinition` has an `int Id`; prepended as the first property when the entity declares no `Id` field (case-insensitive), otherwise the existing `Id` field is kept in place and not duplicated.

- [ ] **Step 1: Update the existing test and add the new cases**

In `CodeGenerator.Tests/App/DomainModelMapperTests.cs`, **replace** the `ToClassDefinitions_MapsFieldsToProperties` method with the version below (it now expects a prepended `Id`), and **add** the two new methods after it. Leave `ToClassDefinitions_FieldlessEntity_GetsIntId` and `ToClassDefinitions_MultipleEntities_PreservesNamesAndOrder` unchanged.

```csharp
    [TestMethod]
    public void ToClassDefinitions_PrependsIntId_ThenMapsFields()
    {
        var model = new CandidateDomainModel();
        var employee = new CandidateEntity { Name = "Employee" };
        employee.Fields.Add(new CandidateField { Name = "Salary", Type = "decimal" });
        employee.Fields.Add(new CandidateField { Name = "FullName", Type = "string" });
        model.Entities.Add(employee);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.AreEqual(1, classes.Count);
        Assert.AreEqual("Employee", classes[0].Name);
        CollectionAssert.AreEqual(
            new[] { "Id", "Salary", "FullName" },
            classes[0].Properties.Select(p => p.Name).ToList());
        Assert.AreEqual("int", classes[0].Properties[0].Type);
        Assert.AreEqual("decimal", classes[0].Properties[1].Type);
    }

    [TestMethod]
    public void ToClassDefinitions_ExistingIdField_NotDuplicated()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        order.Fields.Add(new CandidateField { Name = "id", Type = "int" });
        order.Fields.Add(new CandidateField { Name = "Total", Type = "decimal" });
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        var idCount = classes[0].Properties.Count(
            p => string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, idCount);
        CollectionAssert.AreEqual(
            new[] { "id", "Total" },
            classes[0].Properties.Select(p => p.Name).ToList());
    }
```

Note: the existing `ToClassDefinitions_MapsFieldsToProperties` method is being renamed/replaced by `ToClassDefinitions_PrependsIntId_ThenMapsFields`; delete the old one.

- [ ] **Step 2: Run the tests to verify the updated one fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DomainModelMapperTests"`
Expected: FAIL — `ToClassDefinitions_PrependsIntId_ThenMapsFields` and `ToClassDefinitions_ExistingIdField_NotDuplicated` fail (current mapper does not add/preserve `Id` for entities that have fields).

- [ ] **Step 3: Implement the guaranteed-`Id` rule**

Replace the body of `ToClassDefinitions` in `AppScaffolding/DomainModelMapper.cs` with:

```csharp
    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(CandidateDomainModel model)
    {
        var classes = new List<ClassDefinition>();

        foreach (var entity in model.Entities)
        {
            var definition = new ClassDefinition { Name = entity.Name };

            bool hasId = entity.Fields.Any(
                f => string.Equals(f.Name, "Id", StringComparison.OrdinalIgnoreCase));

            // Guarantee an int Id key for CRUD. If the entity already declares an
            // Id field, keep it in place; otherwise prepend one. A field-less
            // entity therefore ends up with a single int Id.
            if (!hasId)
                definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

            foreach (var field in entity.Fields)
                definition.Properties.Add(
                    new PropertyDefinition { Name = field.Name, Type = field.Type });

            classes.Add(definition);
        }

        return classes;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DomainModelMapperTests"`
Expected: PASS (4/4 — the two updated/new tests plus the two unchanged ones).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/App/DomainModelMapperTests.cs
git commit -m "Guarantee an int Id key in DomainModelMapper"
```

---

### Task 2: `JsonStoreTemplate` (generic JSON store source)

**Files:**
- Create: `AppScaffolding/JsonStoreTemplate.cs`
- Test: `CodeGenerator.Tests/App/JsonStoreTemplateTests.cs`

**Interfaces:**
- Produces: `AppScaffolding.JsonStoreTemplate.Source() : string` — the full source text of `JsonStore.cs`: a `public sealed class JsonStore<T> where T : new()` in `namespace GeneratedCode`, with `List<T> GetAll()`, `T Add(T)`, `bool Update(T)`, `bool Delete(int)`, persisting to `<T>.json` at `AppContext.BaseDirectory` and keying off a case-insensitive `Id` int property by reflection.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/JsonStoreTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class JsonStoreTemplateTests
{
    [TestMethod]
    public void Source_EmitsGenericStoreInGeneratedCodeNamespace()
    {
        var source = JsonStoreTemplate.Source();

        StringAssert.Contains(source, "namespace GeneratedCode");
        StringAssert.Contains(source, "class JsonStore<T>");
        StringAssert.Contains(source, "public T Add(T item)");
        StringAssert.Contains(source, "public bool Update(T item)");
        StringAssert.Contains(source, "public bool Delete(int id)");
        StringAssert.Contains(source, "AppContext.BaseDirectory");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~JsonStoreTemplateTests"`
Expected: FAILS to compile — `JsonStoreTemplate` does not exist yet.

- [ ] **Step 3: Implement `JsonStoreTemplate`**

Create `AppScaffolding/JsonStoreTemplate.cs`. The `Source()` body is a non-interpolated raw string literal, so every brace inside is literal:

```csharp
namespace AppScaffolding;

/// <summary>Emits the source of the generated app's generic JSON store.</summary>
public static class JsonStoreTemplate
{
    public static string Source() => """
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GeneratedCode;

/// <summary>JSON-backed list store keyed by an int Id. One file per type.</summary>
public sealed class JsonStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path =
        Path.Combine(AppContext.BaseDirectory, typeof(T).Name + ".json");

    public List<T> GetAll()
    {
        if (!File.Exists(_path))
            return new List<T>();
        return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(_path)) ?? new List<T>();
    }

    public T Add(T item)
    {
        var all = GetAll();
        SetId(item, all.Count == 0 ? 1 : all.Max(GetId) + 1);
        all.Add(item);
        Save(all);
        return item;
    }

    public bool Update(T item)
    {
        var all = GetAll();
        var id = GetId(item);
        var index = all.FindIndex(x => GetId(x) == id);
        if (index < 0)
            return false;
        all[index] = item;
        Save(all);
        return true;
    }

    public bool Delete(int id)
    {
        var all = GetAll();
        if (all.RemoveAll(x => GetId(x) == id) == 0)
            return false;
        Save(all);
        return true;
    }

    private void Save(List<T> all) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(all, Options));

    private static PropertyInfo IdProperty =>
        typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(typeof(T).Name + " has no Id property.");

    private static int GetId(T item) => (int)(IdProperty.GetValue(item) ?? 0);

    private static void SetId(T item, int id) => IdProperty.SetValue(item, id);
}
""";
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~JsonStoreTemplateTests"`
Expected: PASS (1/1).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/JsonStoreTemplate.cs CodeGenerator.Tests/App/JsonStoreTemplateTests.cs
git commit -m "Add JsonStoreTemplate (generic JSON store source)"
```

---

### Task 3: `ProgramTemplate` (console-menu Program.cs source)

**Files:**
- Create: `AppScaffolding/ProgramTemplate.cs`
- Test: `CodeGenerator.Tests/App/ProgramTemplateTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks (takes entity names as strings).
- Produces: `AppScaffolding.ProgramTemplate.Build(IReadOnlyList<string> entityNames) : string` — the full source of `Program.cs`: top-level menu statements listing each entity (`<n>) <Name>`) with a `switch` dispatching `Crud.Run<Name>("Name")`, followed by a generic reflection-based `Crud` static class. Uses `using GeneratedCode;`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ProgramTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ProgramTemplateTests
{
    [TestMethod]
    public void Build_ListsEachEntity_AndDispatchesCrud()
    {
        var source = ProgramTemplate.Build(new[] { "Payroll", "Application" });

        StringAssert.Contains(source, "using GeneratedCode;");
        StringAssert.Contains(source, "1) Payroll");
        StringAssert.Contains(source, "2) Application");
        StringAssert.Contains(source, "Crud.Run<Payroll>(\"Payroll\")");
        StringAssert.Contains(source, "Crud.Run<Application>(\"Application\")");
        StringAssert.Contains(source, "static class Crud");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ProgramTemplateTests"`
Expected: FAILS to compile — `ProgramTemplate` does not exist yet.

- [ ] **Step 3: Implement `ProgramTemplate`**

Create `AppScaffolding/ProgramTemplate.cs`. The outer literal is `$$"""…"""`, so `{{menu}}` / `{{cases}}` interpolate while every single `{`/`}` in the generated code stays literal:

```csharp
namespace AppScaffolding;

/// <summary>Builds the console-menu Program.cs for the generated CRUD app.</summary>
public static class ProgramTemplate
{
    public static string Build(IReadOnlyList<string> entityNames)
    {
        var menu = string.Join("\n",
            entityNames.Select((name, i) =>
                $"    Console.WriteLine(\"{i + 1}) {name}\");"));

        var cases = string.Join("\n",
            entityNames.Select((name, i) =>
                $"        case \"{i + 1}\": Crud.Run<{name}>(\"{name}\"); break;"));

        return $$"""
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using GeneratedCode;

while (true)
{
    Console.WriteLine("\n== Menu ==");
{{menu}}
    Console.WriteLine("0) Exit");
    Console.Write("> ");
    var choice = Console.ReadLine();
    if (choice == "0")
        break;
    switch (choice)
    {
{{cases}}
        default: Console.WriteLine("Unknown option."); break;
    }
}

static class Crud
{
    public static void Run<T>(string name) where T : new()
    {
        var store = new JsonStore<T>();
        while (true)
        {
            Console.WriteLine($"\n== {name} ==");
            Console.WriteLine("1) Add  2) List  3) Edit  4) Delete  0) Back");
            Console.Write("> ");
            switch (Console.ReadLine())
            {
                case "1": Add(store); break;
                case "2": List(store); break;
                case "3": Edit(store); break;
                case "4": Delete(store); break;
                case "0": return;
            }
        }
    }

    static void Add<T>(JsonStore<T> store) where T : new()
    {
        var item = new T();
        foreach (var p in Editable(typeof(T)))
            PromptInto(item, p);
        store.Add(item);
        Console.WriteLine("Added.");
    }

    static void List<T>(JsonStore<T> store) where T : new()
    {
        var all = store.GetAll();
        if (all.Count == 0)
        {
            Console.WriteLine("(none)");
            return;
        }
        foreach (var item in all)
            Console.WriteLine(Describe(item!));
    }

    static void Edit<T>(JsonStore<T> store) where T : new()
    {
        Console.Write("Id to edit: ");
        if (!int.TryParse(Console.ReadLine(), out var id))
            return;
        var item = store.GetAll()
            .FirstOrDefault(x => (int)IdProperty(typeof(T)).GetValue(x!)! == id);
        if (item is null)
        {
            Console.WriteLine("Not found.");
            return;
        }
        foreach (var p in Editable(typeof(T)))
            PromptInto(item, p, keepOnBlank: true);
        store.Update(item);
        Console.WriteLine("Updated.");
    }

    static void Delete<T>(JsonStore<T> store) where T : new()
    {
        Console.Write("Id to delete: ");
        if (!int.TryParse(Console.ReadLine(), out var id))
            return;
        Console.WriteLine(store.Delete(id) ? "Deleted." : "Not found.");
    }

    static PropertyInfo IdProperty(Type t) =>
        t.GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;

    static IEnumerable<PropertyInfo> Editable(Type t) =>
        t.GetProperties()
         .Where(p => p.CanWrite &&
                     !string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase));

    static string Describe(object item) =>
        string.Join(", ", item.GetType().GetProperties()
            .Select(p => $"{p.Name}={p.GetValue(item)}"));

    static void PromptInto(object item, PropertyInfo p, bool keepOnBlank = false)
    {
        var current = p.GetValue(item);
        Console.Write(keepOnBlank ? $"{p.Name} [{current}]: " : $"{p.Name}: ");
        var raw = Console.ReadLine();
        if (string.IsNullOrEmpty(raw))
            return;
        try
        {
            var converter = TypeDescriptor.GetConverter(p.PropertyType);
            p.SetValue(item, converter.ConvertFromInvariantString(raw));
        }
        catch
        {
            // Unparseable input: leave the property unchanged.
        }
    }
}
""";
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ProgramTemplateTests"`
Expected: PASS (1/1).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ProgramTemplate.cs CodeGenerator.Tests/App/ProgramTemplateTests.cs
git commit -m "Add ProgramTemplate (console-menu Program.cs source)"
```

---

### Task 4: Write the store + menu in `Scaffold`; build, test, and smoke-run the generated app

**Files:**
- Modify: `AppScaffolding/AppScaffolder.cs`

**Interfaces:**
- Consumes: `JsonStoreTemplate.Source()` (Task 2), `ProgramTemplate.Build(...)` (Task 3), `DomainModelMapper.ToClassDefinitions` (Task 1).
- Produces: `AppScaffolder.Scaffold(...)` now also writes `JsonStore.cs` and `Program.cs` into the project folder and includes them in `ScaffoldResult.FilesWritten`; `Success` is true only when all class files **and** both support files were written.

This task's final deliverable (a project that builds and runs) needs the .NET SDK, so it is verified by a **manual compile-and-run smoke** in addition to the unit suite.

- [ ] **Step 1: Add a best-effort single-file writer and wire the two support files into `Scaffold`**

In `AppScaffolding/AppScaffolder.cs`, add this private helper inside the `AppScaffolder` class (after `WriteClasses`):

```csharp
    /// <summary>
    /// Best-effort write of one source file into <paramref name="outputFolder"/>.
    /// Returns the path on success, or null if it could not be written.
    /// </summary>
    private static string? WriteSource(string outputFolder, string fileName, string source)
    {
        try
        {
            var path = Path.Combine(outputFolder, fileName);
            File.WriteAllText(path, source);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
```

Then replace the tail of `Scaffold` — from `var written = WriteClasses(...)` through the `return` — with:

```csharp
        var written = new List<string>(WriteClasses(outputFolder, classes));
        int expected = classes.Count;

        // Support files that make the project a runnable CRUD app.
        var storePath = WriteSource(outputFolder, "JsonStore.cs", JsonStoreTemplate.Source());
        if (storePath is not null)
            written.Add(storePath);
        expected++;

        var programPath = WriteSource(
            outputFolder, "Program.cs",
            ProgramTemplate.Build(classes.Select(c => c.Name).ToList()));
        if (programPath is not null)
            written.Add(programPath);
        expected++;

        bool allWritten = written.Count == expected;

        var message = allWritten
            ? $"Generated {classes.Count} class(es) + a runnable CRUD app."
            : $"Generated {written.Count} of {expected} file(s); " +
              "some files could not be written.";

        return new ScaffoldResult(allWritten, outputFolder, written, message);
```

(The `using System.Linq;` needed for `classes.Select(...)` is covered by `ImplicitUsings` in `AppScaffolding.csproj`.)

- [ ] **Step 2: Build the solution**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Run the full test suite (no regressions)**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: all PASS (the existing 89 + the mapper/template tests added in Tasks 1–3; net new methods: +2 mapper, +1 store, +1 program → 92 total, minus the one replaced mapper method = 92... treat "all green" as the gate, not the exact count).

- [ ] **Step 4: Manual smoke — generate, build, and run the app; confirm persistence**

Run (bash), from the repo root:

```bash
WS="$(mktemp -d)"; echo "workspace: $WS"
printf 'a\n' | PROJECTPATH="$WS" dotnet run --project CodeGenerator -- "build app: payroll application"
APP="$WS/PayrollApplication"
echo "--- generated files ---"; ls "$APP"

echo "--- build the generated app ---"
dotnet build "$APP/PayrollApplication.csproj"

echo "--- run 1: select Payroll(1), Add(1), List(2), Back(0), Exit(0) ---"
printf '1\n1\n2\n0\n0\n' | dotnet run --project "$APP"

echo "--- Payroll.json written? ---"
find "$APP/bin" -name "Payroll.json"

echo "--- run 2: select Payroll(1), List(2), Back(0), Exit(0) — record persists ---"
printf '1\n2\n0\n0\n' | dotnet run --project "$APP"
```

Expected:
- Generated files include `PayrollApplication.csproj`, `Program.cs`, `JsonStore.cs`, `Payroll.cs`, `Application.cs`.
- `dotnet build` of the generated project **succeeds** (0 errors).
- Run 1's List prints a record line containing `Id=1` (Payroll is field-less, so Add prompts for nothing and just assigns the Id).
- `find` locates `Payroll.json` under `.../bin/Debug/net10.0/`.
- Run 2's List **still** prints `Id=1`, proving the record persisted across process restarts.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/AppScaffolder.cs
git commit -m "Write JsonStore + console menu so the generated app runs"
```

---

## Self-Review

**Spec coverage:**
- Runnable console app with Add/List/Edit/Delete menu → `ProgramTemplate` (Task 3), wired in Task 4. ✓
- JSON persistence per entity, survives restarts → `JsonStoreTemplate` (Task 2), smoke run 2 (Task 4). ✓
- Guaranteed `int Id` key; existing `Id` not duplicated; field-less → single `Id` → `DomainModelMapper` (Task 1). ✓
- Everything in `GeneratedCode` namespace; `RoslynClassGenerator` untouched → both templates emit/`using GeneratedCode`; no engine file changed. ✓
- String-template approach in `AppScaffolding`; engine stubs not reused → Tasks 2–3. ✓
- No added NuGet packages (System.Text.Json / System.ComponentModel only) → templates' usings; smoke build proves it. ✓
- `AppScaffolder.Scaffold` writes the two support files, `Success` iff all written, files listed → Task 4. ✓
- Reflection-based console input conversion via `TypeDescriptor`, blank/unparseable keeps value → `PromptInto` (Task 3). ✓
- JSON at `AppContext.BaseDirectory` → `JsonStore` `_path` (Task 2), verified by `find` (Task 4). ✓
- Tests: mapper Id rules, template contents, compile-and-run smoke → Tasks 1–4. ✓
- Scope boundaries (no relationships/validators/search/DB) → respected; no task adds them. ✓

**Placeholder scan:** No TBD/TODO/"similar to". Every code step shows full code; every command has expected output. ✓

**Type consistency:** `ToClassDefinitions(CandidateDomainModel):IReadOnlyList<ClassDefinition>` (unchanged signature, new behavior); `JsonStoreTemplate.Source():string`; `ProgramTemplate.Build(IReadOnlyList<string>):string`; `AppScaffolder.WriteSource(string,string,string):string?`; `Scaffold(...):ScaffoldResult` unchanged. Generated `JsonStore<T>` members (`GetAll`/`Add`/`Update`/`Delete`) match the `Crud` engine's calls in `ProgramTemplate`. `Crud.Run<T>(string)` matches the dispatch `Crud.Run<Name>("Name")`. `Id` handled case-insensitively in both the mapper check and the generated store/menu reflection. Consistent across tasks. ✓

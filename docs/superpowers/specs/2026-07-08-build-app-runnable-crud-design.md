# `build app:` Runnable JSON-Persisted CRUD App — Design Spec

**Date:** 2026-07-08
**Status:** Approved (design)
**Branch:** `feat/build-app-runnable-crud`

## Goal

On the `build app:` **approve** action, generate a console application that
actually runs. `dotnet run` launches a menu where the user picks an entity and
performs Add / List / Edit / Delete on its records. Records persist to per-entity
JSON files that survive restarts.

This delivers roadmap **slices 4 (JSON store + CRUD) and 5 (console-menu
`Program.cs` → `dotnet run` works)** from
`docs/superpowers/specs/2026-07-07-domain-discovery-build-app-design.md`,
building on the model generation delivered in the scaffolding phase
(`docs/superpowers/specs/2026-07-07-build-app-scaffolding-design.md`).

## Background

The scaffolding phase generates one C# class per approved entity into a fresh
console project, but the project's default `Program.cs` (from `dotnet new`) does
not reference those entities — it is not a wired-up app. All generated entity
classes live in the fixed `GeneratedCode` namespace emitted by
`RoslynClassGenerator` (unchanged). The existing `RepositoryGenerator` and
`ServiceGenerator` in `CodeGenerationEngine` are empty stubs (bodyless
interfaces/classes, inconsistent namespaces) and are **not** reused here.

## Approach

`AppScaffolding` owns the new generation via C# **string templates** (not Roslyn
syntax factory, not the engine's stub generators). Two template producers emit a
generic JSON-backed store and a console-menu `Program.cs`. Everything the
generated app contains lands in the `GeneratedCode` namespace so it compiles
against the already-generated entity classes without touching
`RoslynClassGenerator`.

## What gets generated

Into the generated project folder (`<workspaceRoot>/<AppName>/`), all in
namespace `GeneratedCode`:

- `<Entity>.cs` — the POCO classes (existing scaffolding behavior), **plus a
  guaranteed `int Id`** as the first property (see Identity).
- `JsonStore.cs` — a single generic template `JsonStore<T> where T : new()`.
  Loads/saves `List<T>` to `<T>.json` located at `AppContext.BaseDirectory` (next
  to the built assembly, independent of the current working directory) via
  `System.Text.Json`. Assigns `Id = max(existing Id) + 1` on Add (via reflection
  on the `Id` property). Supports `GetAll`, `Add`, `Update` (by `Id`), and
  `Delete` (by `Id`). Identical for every generated app (no per-entity variation).
- `Program.cs` — **overwrites** the `dotnet new` default. Top-level menu that
  lists the entities, then a per-entity submenu: Add / List / Edit / Delete /
  Back. `using GeneratedCode;`. Field prompts read strings from the console and
  convert to each property's CLR type via `TypeDescriptor.GetConverter` /
  `Convert.ChangeType`; input that cannot be parsed is skipped (on Edit, the old
  value is kept). The `Id` property is auto-managed and never prompted.

## Identity

`DomainModelMapper.ToClassDefinitions` guarantees every produced `ClassDefinition`
has an `int Id` key property:

- If the entity already has a field named `Id` (case-insensitive), it is **not**
  duplicated: its existing position and declared type are preserved and it is
  treated as the key. The `JsonStore<T>` key logic assumes this field is
  `int`-compatible; typical discovered models do not declare their own `Id`, so
  this is an edge case, not the common path.
- Otherwise an `int Id` property is **prepended** as the first property.
- The scaffolding-phase rule "field-less entity gets an `int Id`" becomes a
  special case of this general rule (no `Id` field present → prepend `int Id`).

`JsonStore<T>` reads and writes the `Id` value by reflection on a public
`Id` property of type `int`. No `IEntity` interface is introduced, so entities
remain plain POCOs and entity generation (`RoslynClassGenerator`) is untouched.
The reflection surface is confined to the single `JsonStore.cs` template.

## Components (new / changed in `AppScaffolding`)

- **`JsonStoreTemplate`** (new, static) — `string Source()` returns the full
  `JsonStore.cs` source. A constant template; no inputs.
- **`ProgramTemplate`** (new, static) — `string Build(IReadOnlyList<string>
  entityNames)` returns the `Program.cs` source for the given entities.
- **`DomainModelMapper`** (changed) — enforce the guaranteed-`Id` rule above.
- **`AppScaffolder.Scaffold`** (changed) — after `WriteClasses`, also write
  `JsonStore.cs` and `Program.cs` into the project folder. Include them in
  `ScaffoldResult.FilesWritten`. Success remains "all expected files written".
- **`AppScaffolder`** may gain a small private helper to write a single named
  source file (reusing the best-effort IO pattern from `WriteClasses`).

No changes to `CodeGenerationEngine`, `DomainDiscovery`, or the CLI surface of
`BuildAppCommand` beyond what already calls `Scaffold`.

## Data flow

```
build app: <desc>
  → DomainDiscoveryPipeline.Discover → review loop → approve
    → AppScaffolder.Scaffold(model, appName, workspaceRoot)
        → DomainModelMapper.ToClassDefinitions(model)   # + guaranteed Id
        → DotNetProjectFactory.CreateProject (reuse if csproj exists)
        → WriteClasses(...)                              # <Entity>.cs
        → write JsonStore.cs   (JsonStoreTemplate.Source())
        → write Program.cs     (ProgramTemplate.Build(names))  # overwrite default
    → print project path + files written
```

## Namespaces

Entities, `JsonStore<T>`, and the menu logic all live in `GeneratedCode`.
`Program.cs` declares `using GeneratedCode;`. This resolves the scaffolding
phase's documented risk (default `Program.cs` ignored the entities) without
changing the shared `RoslynClassGenerator`.

## Testing

Unit (MSTest, `CodeGenerator.Tests`):

- `DomainModelMapper`: when no `Id` field exists, an `int Id` is prepended as the
  first property; an existing `Id` field (case-insensitive) is not duplicated;
  a field-less entity still yields a single `int Id`.
- `ProgramTemplate.Build`: emitted source is non-empty, contains a menu entry
  per entity name, and `using GeneratedCode;`.
- `JsonStoreTemplate.Source`: emitted source contains `class JsonStore` and the
  `GeneratedCode` namespace.
- `AppScaffolder.WriteClasses`/file-writing: unchanged best-effort behavior still
  holds; new file writes are covered where they can be exercised without the SDK.

Compile-and-run smoke (requires the .NET SDK; manual, not a unit test):

- `build app: payroll application` → approve → the generated project **builds**
  (`dotnet build` succeeds), and a scripted `dotnet run` with piped menu input
  (select entity → Add a record → List → exit) prints the added record and
  leaves a `Payroll.json` on disk. A second run lists the persisted record,
  proving persistence.

## Scope boundaries (YAGNI)

- No relationships / foreign keys between entities.
- No validation attributes (the remaining slice-3 "validators" work is a
  separate future plan).
- No search, filtering, paging, or sorting in the menu.
- Console host only — no Web API, GUI, or service host.
- JSON files only — no database, EF Core, or migrations.
- No configuration of storage location (app base directory) or app name (still
  derived from the description, per the scaffolding phase).

## Risks

- **Reflection-based console input conversion** handles primitives, `string`,
  `bool`, `decimal`, and `DateTime` via `TypeDescriptor`/`Convert.ChangeType`;
  exotic property types fall back to leaving the value unset/unchanged.
  Acceptable for a starter scaffold.
- **`Program.cs` overwrite** replaces the `dotnet new` default. Intended, and
  scoped to the generated app folder only.
- **`dotnet new` / `dotnet build` require the SDK on PATH** and are slow; the
  build/run verification is a manual smoke step, consistent with the scaffolding
  phase's approach.
- **JSON store concurrency**: the generated app is single-process, single-user;
  the store reads/writes the whole file with no locking. Fine for the scope.

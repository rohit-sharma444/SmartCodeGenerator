# Generated-app seed data & companion test project — design

**Date:** 2026-07-21
**Status:** Approved (design)
**Feature:** 3 of 9 ("Seed data + test project for generated app")

## Goal

A freshly generated app is not a satisfying starting point:

1. **It opens empty.** `JsonStore.GetAll()` returns an empty list when no `<Type>.json`
   file exists, so the console grid, the WPF `DataGrid`, and the WebApi collection
   endpoints all show nothing until the user hand-enters rows. There is nothing to demo.
2. **It ships with no tests.** The scaffolder emits a single project with no companion
   test project, so the generated `Models`/`Repository` core has zero test coverage and the
   user has no starting point to extend.

This feature makes every generated app **seed itself with sample data on first run** and
**ship a companion xUnit test project** that exercises the shared core (and thereby the
seed data).

Scope (per decisions): **all five targets** (WPF, Console, WebApi, Worker, ClassLibrary).
Seed runs at startup for the four with an entry point; ClassLibrary emits `SeedData` but
does not auto-invoke it (no entry point). Every target gets a companion test project.

Non-goals (explicitly deferred):

- No EF Core / database target (that is Feature 4). Seed populates the existing JSON store.
- No change to the discovery pipeline, NLE, `CandidateField`, or the validation/relationship
  behaviour shipped in Feature 1 — seed *reuses* the FK convention Feature 1 established.
- No `.sln` generation. The test project references the app project directly.

## Verified starting state

- `AppScaffolding/AppScaffolder.cs:58` — `Scaffold(model, appName, workspaceRoot, target)`
  creates exactly **one** project (`DotNetProjectFactory.CreateProject`, once, `:69`), writes
  the shared `Models/` + `Data/` core and the target's front-end files into that one folder,
  and returns a `ScaffoldResult` (`:125`). This is the single-project assumption Part B extends.
- `AppScaffolding/JsonStoreTemplate.cs:26` — `GetAll()` returns `new List<T>()` for an absent
  file; `SaveAll` (`:45`) and `NextId`/`NewItem` (`:59`/`:66`) are the surface a seeder uses.
  The store path is `AppContext.BaseDirectory + typeof(T).Name + ".json"` (`:22`).
- `AppScaffolding/RepositoryTemplate.cs` — `Repository<T>` (`:29`) exposes `GetAll`,
  `GetById`, `Create` (assigns Id), `Update`, `Delete`. The seeder and the generated tests
  build only on this public surface.
- Entry points where seed is invoked:
  - `AppScaffolding/ConsoleProgramTemplate.cs` — top-level `Program`; seed before the menu loop.
  - `AppScaffolding/MainViewModelTemplate.cs:22` — the WPF `MainViewModel` constructor.
    **Ordering constraint:** each `EntityViewModel<T>` loads its own store in its constructor
    (per the comment at `:29-33`), so `SeedData.EnsureSeeded()` must run at the **very start**
    of the constructor, before the `ObservableCollection` of view-models is built — otherwise
    the tabs load the still-empty stores.
  - `AppScaffolding/WebApiProgramTemplate.cs:16` — after `builder.Build()`, before `app.Run()`.
  - `AppScaffolding/WorkerProgramTemplate.cs` — before `builder.Build().Run()`.
- `AppScaffolding/DomainModelMapper.cs` — the model→`ClassDefinition` mapping; a foreign key
  is an `int` property named `<Target>Id` whose sibling `<Target>` navigation property is a
  generated entity class (the convention Feature 1 introduced). This is the signal both the
  seed ordering and the runtime FK assignment use.
- `AppScaffolding/ITargetScaffolder.cs` / `GeneratedFile.cs:5` — a target contributes
  `GeneratedFile(RelativeFolder, Name, Source)` records, all resolved under the one project
  root. Part B's test project is emitted by the scaffolder itself, not routed through a target.
- `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs:28` — MSTest, `[TestCategory("Integration")]`,
  `[DataRow]` over `console`/`webapi`/`classlib`/`worker` (WPF excluded — needs a Windows host).
  Scaffolds into a temp dir and shells out via `RunDotNet` (`:94`) to `dotnet build -c Release`.
  **No `dotnet test` on generated output exists anywhere yet** — Part B adds the first.
- `CodeGenerationEngine/Factory/DotNetProjectFactory.cs:102` — `GetTemplate` maps
  `DotNetProjectType` to a `dotnet new` template; there is no test-project member today.
  `ProjectRequest` has no notion of a project reference.

## Part A — Seed data (`Data/SeedData.cs`)

A single generated file in the `Data/` core, namespace `<projectName>.Data`, present for
**every** target. Reflection-based, consistent with `JsonStore`/`Repository` (which already
reflect on `Id`).

**Public surface:**

```csharp
public static class SeedData
{
    /// <summary>Rows inserted per empty entity store.</summary>
    public const int SeedRowCount = 3;

    /// <summary>Populates every empty store with sample rows, parents before children so
    /// foreign keys point at real rows. A store that already holds data is left untouched.</summary>
    public static void EnsureSeeded();
}
```

**Ordering:** `EnsureSeeded` calls an internal `Seed<T>()` once per entity **in dependency
order — referenced (parent) entities first, referencing (child) entities after** — and the
order is baked in at generation time from the model's FK relationships (the same relationship
data Feature 1 surfaced). A dependency cycle degrades gracefully to declaration order (FKs in
the later-seeded entity simply reference whatever parents already seeded).

**Per-store idempotency:** `Seed<T>()` returns immediately if that type's store is non-empty,
so first run seeds and every subsequent run is a no-op — no duplicates.

**Value generation (per property, by type, reflection over public settable scalars; `Id`
skipped — `Repository.Create` assigns it):**

| Property | Sample value for row `n` (1-based) |
|---|---|
| `string`, name contains `email` | `"user{n}@example.com"` |
| `string`, name contains `phone` | `"555-010{n}"` |
| other `string` | `"{PropertyName} {n}"` (e.g. `"Reference 1"`) |
| `int` **foreign key** (`<Target>Id`, sibling nav is an entity) | Id of an existing seeded `<Target>` row, round-robin |
| other `int` | `n` |
| `decimal`/`double` | `n * 10 + 0.99m` |
| `DateTime` | `DateTime.Today.AddDays(-n)` |
| `bool` | `n % 2 == 0` |

FK detection at runtime mirrors Feature 1: an `int` property whose name ends in `Id` (and is
not `Id` itself) and whose sibling property `<name-minus-Id>` is typed as a generated entity.
The parent rows are read back through the parent's store; because parents seed first, the
list is non-empty. If no parent rows exist (cycle / missing target), the FK is left `0`.

**Invocation (one line each):** `SeedData.EnsureSeeded();` in the four entry points listed in
Verified starting state — for WPF at the very start of the `MainViewModel` constructor
(before the view-models load). ClassLibrary emits `SeedData.cs` but nothing calls it.

## Part B — Companion test project (`<projectName>.Tests`)

A new scaffolder capability: emit a second project next to the app.

**Creation:**
- Add a `DotNetProjectType` member for an xUnit test project → `GetTemplate` returns `"xunit"`.
- After the app project is scaffolded, create `<projectName>.Tests` in a sibling folder via
  `DotNetProjectFactory.CreateProject`, then:
  - inject `<ProjectReference Include="..\<projectName>\<projectName>.csproj" />` into the
    generated test `.csproj` (a deterministic `.csproj` edit — no extra `dotnet` process call);
  - delete the template's default `UnitTest1.cs`.
- **TFM matches the app:** `net10.0` for Console/WebApi/Worker/ClassLibrary, `net10.0-windows`
  for WPF (so the reference to a `UseWPF` project resolves). The WPF test project inherits the
  Windows-only build limitation and is excluded from Linux CI exactly as the WPF app is.

**Generated tests (xUnit, target-agnostic — they test the shared core every target ships):**
- **`RepositoryRoundTripTests`** — per entity, a self-contained CRUD cycle on the typed
  repository: `Create` a sample instance → `GetById` returns it → `Update` a field persists →
  `Delete` removes it. Self-cleaning (deletes what it created) and asserting on the specific
  row rather than absolute counts, so tests don't couple to store state or to each other.
- **`SeedDataTests`** — `SeedData.EnsureSeeded()` makes at least one entity store non-empty,
  and a second call does not increase counts (idempotency). This is where Part B verifies Part A.

Store isolation: `JsonStore` writes one file per type under `AppContext.BaseDirectory`. The
round-trip tests are written to tolerate shared state (create-then-delete a uniquely-valued
row, assert on that row). This is a documented simplicity of the starter suite, not a bug.

**`ScaffoldResult` / `AppScaffolder`:** `Scaffold` gains the test-project emission step; the
test project's files are added to the returned `written` list, and `ScaffoldResult` gains a
`TestProjectPath` (nullable — a target/caller that skips tests leaves it null). Existing
single-project callers keep working (the app project is still `ProjectPath`).

## Testing

- **Scaffolder unit (MSTest, `CodeGenerator.Tests`):** assert `SeedData.cs` is emitted for
  every target into `Data/`; assert `EnsureSeeded` is invoked in each of the four entry-point
  templates (source-substring, matching existing template tests); assert the test project is
  emitted with the ProjectReference to the app and without `UnitTest1.cs`; assert the seed
  ordering places a referenced entity before its referencer for a two-entity FK model.
- **Seed value logic:** where the value-generation and FK-resolution logic can be exercised
  without a scaffolded app, unit-test the emitted `SeedData` behaviour via a compiled
  generated project (integration, below) rather than duplicating it in the harness.
- **Generated-project build + test (integration, extends `GeneratedProjectBuildTests`):**
  for `console`/`webapi`/`classlib`/`worker`, after `dotnet build` succeeds, run
  `dotnet test` on the generated `<app>.Tests` project and assert exit 0 (a `RunDotNet`
  already exists; add a `dotnet test` invocation). WPF stays build/test-excluded on Linux CI,
  consistent with today. This proves seed + repositories + generated tests all compile and pass
  end-to-end on a real toolchain.
- **Determinism:** generated sources must be stable across runs (no timestamps baked into
  source — `DateTime.Today` is evaluated at the generated app's runtime, not at generation).

## Files touched (anticipated)

- `AppScaffolding/SeedDataTemplate.cs` — **new**; emits `Data/SeedData.cs` (Part A).
- `AppScaffolding/TestProjectScaffolder.cs` (+ `TestFileTemplates`) — **new**; emits the
  companion test project and its tests (Part B).
- `AppScaffolding/AppScaffolder.cs` — write `SeedData.cs`; emit the test project; extend
  `ScaffoldResult`.
- `AppScaffolding/ScaffoldResult.cs` — add `TestProjectPath`.
- `AppScaffolding/ConsoleProgramTemplate.cs`, `MainViewModelTemplate.cs`,
  `WebApiProgramTemplate.cs`, `WorkerProgramTemplate.cs` — the `EnsureSeeded()` call.
- `CodeGenerationEngine/Factory/DotNetProjectFactory.cs` + `DotNetProjectType.cs` — xUnit
  test-project template member.
- `CodeGenerator.Tests/` — scaffolder unit tests + extended `GeneratedProjectBuildTests`
  (`dotnet test` on the generated test project).

## Backward compatibility

- The shared Models/Data core is unchanged except for the added `SeedData.cs`; existing
  generated files keep their shapes.
- Seed is seed-if-empty, so a user who has already entered data sees no change; a user
  regenerating over an existing project keeps their data.
- The extra test project is additive; `ProjectPath` still points at the app project, so
  existing callers are unaffected.
- Every generated target must still `dotnet build` clean, and the new generated test project
  must `dotnet test` green, under the project's `-warnaserror` CI.
```

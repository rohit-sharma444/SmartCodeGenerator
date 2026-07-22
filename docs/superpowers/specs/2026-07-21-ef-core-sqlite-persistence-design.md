# EF Core + SQLite persistence for generated apps

**Date:** 2026-07-21
**Feature:** 4 of the 9-feature functionality-improvement effort
**Base:** `main` (tip `1b0ad9e`)

## Summary

Let a generated app persist to a SQLite database via EF Core instead of JSON
files, chosen at generation time. JSON stays the default. The choice is
orthogonal to the front-end: a single build-time selection swaps the generated
Data core's storage backend behind a shared interface, so the front-end code is
unchanged. Scope is the three Repository-based targets — **console, webapi,
worker**. **WPF stays JSON** (its view models read `JsonStore<T>` directly, off
the shared seam).

## Motivation

Generated apps today persist only to per-type JSON files. That is fine for a
throwaway demo but is not how a real app stores relational data: no queries, no
concurrent access, no indexing, and every save rewrites the whole file. Adding a
relational option makes the generated code a more credible starting point, and
the models are already EF-friendly (`[Key]` on `Id`, `int` foreign keys with
navigation properties, DataAnnotations validation).

## Current state (the gap)

Persistence in a generated app flows through two seams, both built on
`JsonStore<T>`:

1. **`Repository<T>`** (`AppScaffolding/RepositoryTemplate.cs:31`) —
   `private readonly JsonStore<T> _store = new();`. Typed CRUD
   (GetAll/GetById/Create/Update/Delete). Used by console, webapi, worker.
2. **`SeedData`** (`AppScaffolding/SeedDataTemplate.cs:146`) —
   `var store = new JsonStore<T>();`. Reflection-based seed-if-empty. Invoked by
   every entry point.

WPF is different: `ViewModelTemplates.cs:106` news up `JsonStore<T>` directly in
`EntityViewModel`, bypassing `Repository<T>`. That is why WPF is out of scope
here — routing only `Repository`/`SeedData` onto a SQLite backend would leave the
WPF UI reading JSON while the seeder wrote SQLite (split-brain).

EF Core is greenfield: nothing in the repo references it.

`JsonStore<T>`'s public surface is `GetAll() / SaveAll(List<T>) / NextId(...) /
NewItem(...)`, with `Id` reflected and assigned client-side. `SaveAll` overwrites
the entire store.

## Design decisions (settled)

| Decision | Choice | Why |
|---|---|---|
| **Persistence shape** | Orthogonal build-time selection; JSON default | Lowest blast radius; front-ends unchanged; both backends behind one seam |
| **Front-end scope** | Repository-based only (console/webapi/worker); WPF stays JSON | Clean drop-in on the shared Repository seam; avoids a WPF refactor + Windows-only reverification |
| **Schema creation** | `Database.EnsureCreated()` | No `dotnet-ef` toolchain, no design-time factory, no migration codegen; each generated app is a fresh scaffold |
| **Connection string** | In the Data core (`DbContext.OnConfiguring`) | One definition, identical across all three targets; mirrors how `JsonStore` picks its file today |
| **Seeding** | Reuse the existing `SeedData.EnsureSeeded()` path | Because `SeedData` now goes through the shared seam, it seeds SQLite automatically — no separate seed code |

## Architecture — one shared seam

Introduce `IStore<T>` capturing `JsonStore<T>`'s existing public surface. Both
backends implement it. `Repository<T>` and `SeedData` obtain their store from a
generated `StoreFactory.Create<T>()` instead of constructing `JsonStore<T>`
directly. Only the factory body differs per mode, so the `Repository` and
`SeedData` templates become mode-agnostic (one-line edits each).

```
Repository<T>, SeedData  ─►  StoreFactory.Create<T>()  ─►  IStore<T>
                                                            ├─ JsonStore<T>  (json, default)
                                                            └─ EfStore<T>    (sqlite)
```

### `IStore<T>` (emitted in both modes)

```csharp
public interface IStore<T> where T : new()
{
    List<T> GetAll();
    bool SaveAll(List<T> all);
    int NextId(IEnumerable<T> existing);
    T NewItem(IEnumerable<T> existing);
}
```

## Components

### 1. `PersistenceKind` enum (`AppScaffolding`)
`Json` (default) and `Sqlite`. Threaded from the CLI into scaffolding.

### 2. Shared seam interface — `Data/IStore.cs`
New template (`StoreSeamTemplate` or similar). Emitted in **both** modes.

### 3. `JsonStore<T>` — implements `IStore<T>`
One-line change to `JsonStoreTemplate`: `public sealed class JsonStore<T> :
IStore<T> where T : new()`. Behavior unchanged. Emitted only in **JSON** mode
(WPF still emits it because WPF is always JSON — see note under AppScaffolder).

### 4. `StoreFactory` — `Data/StoreFactory.cs`
New template. Body varies by mode:

```csharp
// JSON
public static class StoreFactory
{
    public static IStore<T> Create<T>() where T : class, new() => new JsonStore<T>();
}
// SQLite
public static class StoreFactory
{
    public static IStore<T> Create<T>() where T : class, new() => new EfStore<T>();
}
```

### 5. `Repository<T>` — routed through the factory
`RepositoryTemplate.cs:31` changes from `new JsonStore<T>()` to
`StoreFactory.Create<T>()`. Field type becomes `IStore<T>`. Mode-agnostic.

### 6. `SeedData` — routed through the factory
`SeedDataTemplate.cs:146` changes from `new JsonStore<T>()` to
`StoreFactory.Create<T>()`. Mode-agnostic.

### 7. `EfStore<T>` — `Data/EfStore.cs` (SQLite only)
New template. Implements `IStore<T>` over EF Core, a fresh `AppDbContext` per
call (matching `JsonStore`'s stateless-per-call file access):

- `GetAll()` → `ctx.Set<T>().AsNoTracking().ToList()`.
- `SaveAll(all)` → **replace-all**: remove every existing row, add `all`, one
  `SaveChanges`. Faithful to `JsonStore.SaveAll`'s overwrite contract, so
  `Repository`/`SeedData` need no behavioral change. (Inefficient by design;
  acceptable for generated sample apps — see Alternatives.)
- `NextId(existing)` → `existing` max `Id` + 1, or 1 when empty (same as
  `JsonStore`).
- `NewItem(existing)` → `new T()` with `Id` set to `NextId`.
- `Id` read via reflection, mirroring `JsonStore`.

### 8. `AppDbContext` — `Data/AppDbContext.cs` (SQLite only)
New template, generated from the entity names:

- One `DbSet<Entity>` per entity.
- `OnConfiguring` → `optionsBuilder.UseSqlite(ConnectionString)`, where
  `ConnectionString` is an env override (`<APPNAME>_DB`) falling back to
  `Data Source=<path>` under `AppContext.BaseDirectory` named `<AppName>.db`.
- `OnModelCreating` → for each entity, `Property(e => e.Id).ValueGeneratedNever()`
  so client-assigned ints (via `NextId`) are honored rather than overridden by
  SQLite auto-increment.
- Constructor calls `Database.EnsureCreated()` (idempotent; a cheap no-op once
  the file exists).

### 9. `.csproj` package injection (SQLite only)
Inject a `Microsoft.EntityFrameworkCore.Sqlite` `PackageReference` into the app
`.csproj`, reusing the string-injection approach of
`TestProjectTemplates.InjectProjectReference`. Package version aligned to the
`net10.0` TFM (EF Core 10.x); the exact pin is resolved during planning against
what restores.

### 10. `AppScaffolder.Scaffold` — mode-aware overload
Add `Scaffold(model, appName, workspaceRoot, target, PersistenceKind)`. The
existing 4-arg and 3-arg (WPF) overloads delegate with `PersistenceKind.Json`,
so current callers and tests are unaffected. The data-core file list branches on
the kind:

- **Json:** `IStore.cs`, `JsonStore.cs`, `StoreFactory.cs`(→Json),
  `Repository.cs`, `SeedData.cs`, typed repos. (today's set + `IStore` +
  `StoreFactory`)
- **Sqlite:** `IStore.cs`, `EfStore.cs`, `AppDbContext.cs`,
  `StoreFactory.cs`(→Ef), `Repository.cs`, `SeedData.cs`, typed repos — **no**
  `JsonStore.cs` — plus the package injection into the app csproj.

### 11. `BuildAppCommand` — parse the persistence token
Extend the `approve` command in `RunReviewLoop`
(`CodeGenerator/App/BuildAppCommand.cs`):

- Syntax: `approve <target> [sqlite]`. The line already splits into ≤3 parts, so
  `parts[2]` (when present) is the persistence token.
- Map `sqlite` → `PersistenceKind.Sqlite`; absent/`json` → `Json`; any other
  token → error listing valid values.
- **Reject** `Sqlite` when the resolved target is WPF, with:
  `"SQLite persistence isn't supported for the WPF target yet — it stays JSON."`
  (Do not generate; let the user re-issue.)
- Thread the kind into the new `Scaffold` overload.
- Update the review-loop help line to advertise the optional `sqlite` token.

## Data flow (SQLite, generated app at runtime)

1. Entry point calls `SeedData.EnsureSeeded()`.
2. `SeedData.Seed<T>` calls `StoreFactory.Create<T>()` → `EfStore<T>`.
3. `EfStore<T>` constructs `AppDbContext`; its ctor runs `EnsureCreated()` →
   schema built from `DbSet`s on first run.
4. Empty store → seed rows written via `SaveAll`; non-empty → left untouched
   (idempotent, unchanged semantics).
5. Front-end CRUD goes `Repository<T>` → `StoreFactory` → `EfStore<T>` → SQLite.

## Error handling

- `JsonStore` swallows IO/JSON errors to an empty list so a GUI can still start.
  `EfStore` targets non-GUI hosts (console/webapi/worker); it lets EF/SQLite
  exceptions surface rather than masking a broken database. `SaveAll` returns
  `bool` per the interface; it returns `false` on a caught persistence failure to
  match the seam contract, without hiding a misconfigured connection at open time.
- `EnsureCreated()` failure (e.g. unwritable directory) surfaces at startup —
  correct for a server/worker.

## Testing

**Unit (in `AppScaffolding` / `CodeGenerator` test projects):**
- Each new template emits the expected source (`IStore`, `EfStore`,
  `AppDbContext` with a `DbSet` per entity and `ValueGeneratedNever`,
  `StoreFactory` with the mode-appropriate body).
- `Repository` and `SeedData` templates reference `StoreFactory.Create<T>()`, not
  `new JsonStore<T>()`.
- `AppScaffolder` writes the correct file set per `PersistenceKind` (SQLite
  includes `EfStore`/`AppDbContext`, excludes `JsonStore`; injects the package
  reference).
- `BuildAppCommand` parses `approve <target> sqlite`, defaults to JSON, and
  rejects `approve wpf sqlite`.

**Integration (extends the existing "dotnet-test the generated output" test):**
- Add a SQLite variant that generates console/webapi/worker apps with
  `PersistenceKind.Sqlite` and runs `dotnet test` on each generated companion
  test project. The generated app's own round-trip and seed tests then execute
  against a real SQLite database — end-to-end proof it builds, creates its
  schema, seeds, and round-trips. (The test project references the app, so EF
  Core flows transitively — no extra wiring.)

## Alternatives considered

- **`EfStore.SaveAll` as per-row upsert** instead of replace-all: more efficient
  and more idiomatic EF, but diverges from the `JsonStore` overwrite contract and
  would force behavioral changes in `Repository`/`SeedData`. Rejected for this
  feature; a faithful drop-in keeps the seam honest and the diff small. Can be
  revisited if efficiency ever matters for generated apps.
- **Dedicated EF generation target** (a new `ITargetScaffolder`): duplicates
  front-end scaffolding and does not help existing targets persist relationally.
- **Replace JSON entirely:** discards working behavior and the offline/no-DB
  story; highest churn.
- **EF Migrations instead of `EnsureCreated`:** needs `dotnet-ef`, a design-time
  factory, and generated migration code — heavy for a fresh scaffold with no
  schema-evolution requirement.

## Out of scope

- WPF SQLite persistence (would require reseating `EntityViewModel` off direct
  `JsonStore` onto the shared seam).
- Schema migrations / evolution.
- Query/index tuning, async EF APIs, `DbContext` pooling.
- Type-inference accuracy of mined fields (a separate deferred axis).

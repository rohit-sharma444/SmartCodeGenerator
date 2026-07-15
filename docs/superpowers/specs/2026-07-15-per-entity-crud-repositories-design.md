# Per-Entity CRUD Repositories for the Scaffolded App

**Date:** 2026-07-15
**Status:** Approved — ready for implementation planning
**Branch:** `FixesAndEnhancements`

## Problem

The `build app:` scaffolder maps `model.Entities` to POCO entity classes and emits a
runnable WPF app whose CRUD is entirely **generic**: `EntityViewModel<T>` (Add/Delete/Save
commands) over `JsonStore<T>` (`GetAll`/`SaveAll`/`NextId`/`NewItem`). There is no
per-entity, named, typed CRUD API a developer can code against.

The enhancement: for each entity in `model.Entities`, also generate CRUD **functions** — a
typed repository per entity exposing `GetAll`/`GetById`/`Create`/`Update`/`Delete`.

## Decisions (settled)

1. **Shape:** a typed per-entity repository class (`<Entity>Repository`), not methods on the
   entity POCO and not the engine's `add class` path. The POCO entities stay clean.
2. **Wiring:** a **standalone** additive API surface. The working `EntityViewModel<T>` /
   `JsonStore<T>` UI path is left **untouched**; the app runs exactly as it does today. The
   repositories are extra code to build against.

## Architecture

Emit the repository as **string templates** in `AppScaffolding` — the same pattern the
scaffolder already uses for `JsonStore`, `RelationshipResolver`, and the ViewModels.
Do **not** route this through `DomainModelMapper` / `MethodBodyGenerator`: that path emits
TODO-stub bodies for Update/Delete/Get and would put CRUD on the POCO, which both decisions
above rule out.

Structure is a single generic base plus thin typed subclasses (mirrors how the codebase
already does the generic `EntityViewModel<T>`):

- `Data/Repository.cs` — one generic `public abstract class Repository<T> where T : class, new()`
  wrapping a private `JsonStore<T>`, implementing the five CRUD functions once.
- `Data/<Entity>Repository.cs` — per entity, `public sealed class <Entity>Repository : Repository<<Entity>> { }`.
  This is the named, typed, discoverable class, and a natural home for entity-specific
  finders later.

Both files live in the `<App>.Data` namespace. The typed subclass file needs
`using <App>.Models;` for the entity type.

### The five functions

Built **only** on `JsonStore<T>`'s existing public surface (`GetAll()`, `SaveAll(List<T>)`,
`NextId(IEnumerable<T>)`), so `JsonStore.cs` is not modified:

| Function | Behavior |
|----------|----------|
| `List<T> GetAll()` | delegates to `_store.GetAll()` |
| `T? GetById(int id)` | first record whose Id equals `id`, else `null` |
| `T Create(T item)` | assigns `_store.NextId(all)` to the item, appends, `SaveAll`, returns the item |
| `bool Update(T item)` | replaces the record with the same Id; `false` if no match |
| `bool Delete(int id)` | removes the record with that Id; `false` if no match |

`Update`/`Delete` return the result of `SaveAll` (which is `false` when the file could not be
written), matching `JsonStore`'s existing best-effort write contract.

### Id access

`Repository<T>` reads and writes the `Id` property by reflection **internally** — a small
private helper (case-insensitive `Id` lookup, `int`) mirroring `JsonStore`'s own private
`IdProperty`/`GetId`. This is deliberate minor duplication so that `JsonStore.cs` stays
untouched per the standalone decision. `where T : class, new()` keeps `T?` and
`FirstOrDefault` null semantics clean (generated entities are always classes).

## Wiring in `AppScaffolder.Scaffold`

Add the repository files to the emitted set, driven by the same `entityNames` list that
already drives `MainViewModel.Build`. Because that list is `classes.Select(c => c.Name)`,
the repositories use **class** names (post-rename): a `Type`→`TypeItem` rename yields
`TypeItemRepository`.

- One `Data/Repository.cs` (base, once).
- One `Data/<Entity>Repository.cs` per class.

These are appended to the existing best-effort write loop, so `expected` and `Success`
account for them exactly like the other support files. A repo file that cannot be written is
skipped and the rest continue, consistent with the current behavior.

## Collision guard

Add `"Repository"` to `DomainModelMapper.ReservedNames`. An entity literally named
*Repository* would otherwise produce `RepositoryRepository : Repository<Repository>`, where
the type argument binds to the base `Repository` type in the same namespace rather than the
entity. Reserving the name renames such an entity to `RepositoryItem`, exactly as the set
already does for `JsonStore`, `EntityViewModel`, `Data`, and the other in-scope identifiers.

## Testing

- **`CodeGenerator.Tests/App/RepositoryTemplateTests.cs`** (new, MSTest, matching the other
  template tests):
  - Base source contains `abstract class Repository<T>`, the `JsonStore<T>` field, and all
    five function signatures (`GetAll`, `GetById`, `Create`, `Update`, `Delete`).
  - Base source names the correct namespace (`<App>.Data`).
  - Typed source contains `class <Entity>Repository : Repository<<Entity>>` and
    `using <App>.Models;`.
- **`DomainModelMapperTests`**: add a case that an entity named `Repository` is renamed to
  `RepositoryItem`.
- **Scaffolder write test** (`AppScaffolderWriteTests`): confirm `Repository.cs` and one
  `<Entity>Repository.cs` per entity are written into `Data/`, and are counted in
  `FilesWritten` / `Success`.
- **Manual compile smoke:** generate an app, `dotnet build` it, confirm it still builds with
  the repositories present (they are unused by the app but must compile).

## Out of scope

- No change to `EntityViewModel<T>`, `JsonStore<T>`, or the UI persistence flow.
- No entity-specific query methods beyond the five CRUD functions (the typed subclass is the
  extension point for those later).
- No async, no interfaces, no DI container — the repositories are `new()`-constructed, same
  as `JsonStore<T>` and `EntityViewModel<T>`.

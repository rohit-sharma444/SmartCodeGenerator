# AppScaffolding

Turns an **approved domain model** into a **runnable app** on disk. It is the
last stage of the `build app:` path: once
[`DomainDiscovery`](../DomainDiscovery/README.md) has proposed entities and the
user has approved them in the CLI's review loop, this project writes the project
file, the model classes, and a JSON-backed store with a typed repository per
entity — then hands off to the chosen **target** for the project type and front
end.

Targets are an `ITargetScaffolder` strategy, and five ship today:

| Target | `Key` | What it produces |
| --- | --- | --- |
| WPF (default) | `wpf` | A windowed CRUD app — one tab per entity. |
| Console | `console` | A reflection-driven CRUD menu over the repositories. |
| Web API | `webapi` | A minimal API with REST CRUD endpoints per entity. |
| Class library | `classlib` | The shared core only — no entry point. |
| Worker | `worker` | A `BackgroundService` that exercises the repositories. |

The shared core is identical across all of them; only the front end differs.

This is a class library (`net10.0`) with no console I/O. The
[`CodeGenerator`](../CodeGenerator/README.md) CLI drives it from
`BuildAppCommand` when the user types `approve` (optionally `approve <target>`).

## Where it sits

```
build app: a library that lends books to members
        │
        ▼
DomainDiscovery          →  CandidateDomainModel (entities, fields, relationships)
        │
        ▼
review loop (CLI)        →  approve / add / remove / rename
        │
        ▼  approve <target>
DomainModelMapper        →  CandidateDomainModel → ClassDefinition[]  (the shaping stage)
        │
        ▼
AppScaffolder.Scaffold   →  dotnet new <target> + shared core + target front-end
        │
        ▼
ScaffoldResult           →  project path + every file written
```

## What it generates

`Scaffold(model, appName, workspaceRoot, target)` creates (or reuses) a project
at `<workspaceRoot>/<appName><target.FolderSuffix>` and writes two parts: a
**shared core** every target gets, and the **front end** the target supplies. A
three-arg overload, `Scaffold(model, appName, workspaceRoot)`, defaults the
target to WPF, so existing callers are unchanged.

The shared core:

```
<Project>/
  <Project>.csproj          from DotNetProjectFactory (target.ProjectType, net10.0)
  Models/
    <Entity>.cs             one per entity, via Roslyn
  Data/
    JsonStore.cs            generic JSON list store, keyed by int Id
    Repository.cs           generic base — GetAll/GetById/Create/Update/Delete
    <Entity>Repository.cs   one typed repository per entity
    SaveResult.cs           what a validating write returns
  Validation/
    IValidator.cs           the seam: IReadOnlyList<string> Validate(T)
    IEntityLookup.cs        existence checks a validator needs for foreign keys
    StoreEntityLookup.cs    the real lookup, reading through the store
    ValidatorFactory.cs     type → validator, mirroring StoreFactory
    <Entity>Validator.Generated.cs   the inferred rules — REGENERATED
    <Entity>Validator.Custom.cs      hand-written rules — written ONCE
```

Then the target adds its front end and reconciles the `dotnet new` output:

| Target | Folder suffix | Front end it writes |
| --- | --- | --- |
| WPF | *(none)* | `Data/RelationshipResolver.cs`, `ViewModels/` (ObservableObject, RelayCommand, IEntityViewModel, EntityViewModel, MainViewModel — one tab per entity), `Views/MainWindow.xaml(.cs)`; repoints `App.xaml`'s `StartupUri` to the foldered window. |
| Console | `.Console` | `EntityMenu.cs` (a reflection-driven CRUD menu) and `Program.cs` (registers one menu per entity). |
| Web API | `.Api` | `Program.cs` mapping REST CRUD endpoints per entity (minimal API). |
| Class library | `.Lib` | *(nothing)* — the core is the deliverable; `FixupProject` deletes the template's `Class1.cs`. |
| Worker | `.Worker` | `Worker.cs` (a `BackgroundService` logging each repository's record count) and `Program.cs`. |

`RelationshipResolver.cs` lands under `Data/` but is **WPF-only**, not shared
core: it depends on `IEntityViewModel`, so `WpfTarget` contributes it rather than
`AppScaffolder`.

The generated app persists to **one JSON file per type**, written beside the
built executable (`AppContext.BaseDirectory`). There is no database.

## Seeding from the user's own data

`Scaffold(model, appName, workspaceRoot, target, persistence, importedData)` takes
one more input: an `ImportedData` — entity name → rows, keyed by field name —
produced by [`DataIngestion`](../DataIngestion/README.md) when the user built the
app from a CSV, TXT or JSON file instead of a description. Every other overload
passes `null`, so a description-built app is byte-for-byte what it always was.

When rows are present the scaffolder adds:

```
<Project>/
  Data/
    Seed/
      <Class>.json          one per entity that contributed rows
```

registered in the `.csproj` as `Content` with `CopyToOutputDirectory=PreserveNewest`
so the app finds them beside its binary at run time, and `SeedData.cs` gains a
second mode: **if a seed file exists, deserialize it and `store.SaveAll(rows)`;
otherwise emit the three reflective sample rows exactly as before.** It goes
through the `IStore<T>` seam, so JSON and SQLite persistence both work with no
further change.

Three details that are load-bearing:

- **`SeedFileWriter` walks each class's FINAL property list**, not the data's keys.
  That is what absorbs the review loop: a field the user removed simply stops being
  written, a field they added is omitted and keeps the class's default, and no seed
  file can ever name a property the generated class does not declare.
- **Ids are assigned 1..N in file order at generation time**, and foreign keys
  produced by a normalization split are already rewritten to match — so the emitted
  JSON holds correct ints and nothing resolves keys at run time.
- **Everything goes through `Utf8JsonWriter`**, never string concatenation, so a
  cell containing quotes and braces stays one string instead of becoming syntax.

A value that will not parse as its declared type is **omitted** rather than emitted
as a broken literal: the user may have retyped the field in the review loop, and
that row should still load.

## Validation

Each model gets its own validator, split across two files:

- **`<Entity>Validator.Generated.cs`** is rewritten on every build. It carries the
  rules `DomainModelMapper.AddValidation` already recorded on the model —
  required, max length, email and phone shape — plus a presence check per
  foreign key. Nothing is re-inferred here, so a model's attributes and its
  validator cannot disagree. A `[Key]` `Id` gets no check (the store assigns it,
  so validating it would reject every new record), and navigation properties get
  none either. Each property's checks sit in their own `try`/`catch`, so one
  throwing rule cannot abort the rest.
- **`<Entity>Validator.Custom.cs`** is written **once** and never overwritten —
  it is where your own cross-field and business rules live, and regenerating the
  app must not discard them. It implements a `partial void ValidateCustom` hook
  the generated half calls; because that hook is a partial method, deleting the
  file entirely still compiles.

`ValidatorFactory.Create<T>()` resolves a type to its validator, deliberately
mirroring `StoreFactory.Create<T>()` — the front-end templates are generic over
`T` and so cannot name `CustomerValidator` directly. A type with no generated
validator degrades to a no-op `NullValidator<T>` rather than throwing.

`Repository<T>.Create` and `Update` validate **before** touching the store and
return `SaveResult<T>`, which keeps `NotFound` distinct from `Errors`: an
`Update` naming an unknown Id is a different failure from one carrying an
invalid record, and the Web API front end maps them to 404 and 400 respectively.
`GetAll`, `GetById` and `Delete` are unchanged.

Two things worth knowing:

- **The WPF save path bypasses `Repository`.** `EntityViewModel<T>.Save()` writes
  through the store directly, so repository-level enforcement never sees those
  records; it calls `ValidatorFactory.Create<T>()` itself. This is also why
  `EntityViewModel<T>` is constrained `where T : class, new()`.
- **`SeedData` is deliberately not validated.** It writes through
  `StoreFactory`/`SaveAll` rather than `Repository`, so seeding cannot be blocked
  by a rule inferred from a model it is trying to populate.
- **A foreign-key check reads the whole store.** `StoreEntityLookup.Exists<T>`
  goes through `StoreFactory.Create<T>().GetAll()`, which deserializes the entire
  JSON file (or loads the whole table). The WPF `Save()` validates every record in
  a loop, so an app with *R* records and *F* foreign keys does *R×F* store loads
  per save. Acceptable at the scale these apps are generated for — but if you do
  cache, **scope the cache to a single `Validate` call**. A longer-lived cache goes
  stale and starts rejecting valid keys: the Web API constructs each
  `Repository<T>` once at startup, so its validator and lookup live for the whole
  process.

The generated test project gets a `<Entity>ValidatorTests.cs` per model, using a
fake `IEntityLookup` so no store is needed, plus a shared `TestSupport` helper
that fills an entity with values the generated rules accept.

## Shaping: why the model is not used verbatim

`DomainModelMapper` is where a *discovered* model becomes a *compilable* one.
Most of its work exists because real discovery output breaks the build:

| Rule | Why |
| --- | --- |
| Reserved names get an `Item` suffix (`Data` → `DataItem`) | A class may not share a name with a namespace in scope. `MainViewModel` opens with `using <App>.Data;`, so an entity called `Data` fails with CS0118 — and discovering *"point of sale"* really does yield that entity. The list also covers the app's own types and common `System` names. |
| Every class is guaranteed an `int Id` | CRUD needs a key. An entity that already declares an `Id` keeps it; otherwise one is prepended. |
| `CreatedOn` / `ModifiedOn` are added here | What a running app needs and the domain never said. Kept out of discovery so the line between "what Wikipedia says" and "what a CRUD app needs" stays put. |
| Navigation properties are `[JsonIgnore]` | The nav property is rebuilt from the foreign key at load time. Serializing it too would make `Order.Customer → Customer.Orders → Order` a reference cycle, which `System.Text.Json` throws on. |
| A relationship whose target didn't survive is skipped | A property typed as a class that is never generated does not compile. |
| Names are deduplicated case-insensitively | Class names double as file names, and Windows file names are case-insensitive. |

`Pluralize` is deliberately not a linguistics engine — worst case an inverse
collection is called `Companys`, which compiles, reads oddly, and can be renamed
in the review loop.

## Key types

| Type | Role |
| --- | --- |
| `AppScaffolder` | The entry point. `Scaffold(model, appName, workspaceRoot, target)` writes the shared core and delegates the front end to `target`; a three-arg overload defaults to WPF, and a six-arg one takes `ImportedData` to seed from the user's own rows. `WriteClasses(...)` writes just the model classes. |
| `ImportedData` | Entity name → rows → field name → raw string value, plus `Notes` the caller must print (above all, that the `MaxSeedRows` cap dropped rows). Lives here rather than in `DataIngestion` because it is an *input to the scaffolder*, alongside `PersistenceKind` — and because that keeps it free of any `TabularTable` dependency, so a future SQL reader feeds the same carrier. |
| `SeedFileWriter` | Imported rows → `(fileName, json)` pairs, projected through each class's final property list. `SeedFolderName` is the one spelling of the `Seed` folder that both this and the generated seeder use. |
| `ITargetScaffolder` | The strategy a target implements: `Key`, `Description`, `ProjectType`, `FolderSuffix`, `FrontEndFiles(projectName, entityNames)`, and an optional `FixupProject(outputFolder)` (default no-op). |
| `TargetScaffolders` | The registry — ordered `Keys` (`wpf, console, webapi, classlib, worker`), case-insensitive `TryResolve`, and `Default` (WPF). |
| `GeneratedFile` | One file a target contributes: `RelativeFolder` (relative to the project root, `""` for the root), `Name`, `Source`. |
| `WpfTarget`, `ConsoleTarget`, `WebApiTarget`, `ClassLibraryTarget`, `WorkerTarget` | The five shipped targets, under `Targets/`. |
| `DomainModelMapper` | `CandidateDomainModel` → `ClassDefinition[]`: renaming, `Id`, audit fields, navigation properties. The interesting stage. An overload also yields the entity-name → class-name map, which `SeedFileWriter` needs because rows are keyed by entity while files are named for the class, and `ToUniqueClassName` can rename one (`Type` → `TypeItem`). |
| `DomainAppName` | Free text → PascalCase project name (`"a library that lends books"` → `ALibraryThatLendsBooks`). Falls back to `GeneratedApp`; prefixes `App` when it would start with a digit. |
| `ScaffoldResult` | `Success`, the project path, every file written, and a message. |
| `WpfProjectFixup` | Reconciles what `dotnet new wpf` emits with the foldered layout: deletes the template's root `MainWindow` (it declares the same `x:Class`) and repoints `App.xaml`. Invoked via `WpfTarget.FixupProject`. |
| `*Template` | The generated sources, as raw strings — shared (`JsonStore`, `Repository`) and per-target (`ConsoleMenu`/`ConsoleProgram`, `WebApiProgram`, `Worker`/`WorkerProgram`, `RelationshipResolver`, `MainViewModel`, `MainWindow`, and the view-model base types). |

## Behaviour worth knowing

- **Writes are best-effort.** A file that cannot be written is skipped and the
  rest continue; `ScaffoldResult.Success` is false and the message says how many
  of how many landed. Nothing throws at the caller.
- **An existing project is reused, not recreated.** If `<Project>.csproj`
  already exists, project creation *and* `target.FixupProject` are both skipped —
  so a project the user has since edited is left alone. Generated sources are
  still overwritten.
- **The generated app is defensive by design.** A missing or corrupt JSON file
  reads as empty rather than throwing: a GUI has nowhere to print a stack trace,
  and starting empty beats dying before the window appears.

## Dependencies

Project references to [`DomainDiscovery`](../DomainDiscovery/README.md) (for
`CandidateDomainModel`) and
[`CodeGenerationEngine`](../CodeGenerationEngine/README.md) (for
`DotNetProjectFactory` and the Roslyn class generator).

## Build & test

```bash
# from the repository root
dotnet build AppScaffolding/AppScaffolding.csproj
```

Covered by [`CodeGenerator.Tests`](../CodeGenerator.Tests/README.md) under `App/`
rather than by a dedicated test project: `AppScaffolderWriteTests` and
`DomainAppNameTests`, a test per target (`WpfTargetTests`, `ConsoleTargetTests`,
`WebApiTargetTests`, `ClassLibraryTargetTests`, `WorkerTargetTests`),
`TargetScaffoldersTests` for the registry, and `GeneratedProjectBuildTests` — an
integration test that runs `dotnet build` on the generated console, web API,
class-library, and worker projects.

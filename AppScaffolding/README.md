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
| `AppScaffolder` | The entry point. `Scaffold(model, appName, workspaceRoot, target)` writes the shared core and delegates the front end to `target`; a three-arg overload defaults to WPF. `WriteClasses(...)` writes just the model classes. |
| `ITargetScaffolder` | The strategy a target implements: `Key`, `Description`, `ProjectType`, `FolderSuffix`, `FrontEndFiles(projectName, entityNames)`, and an optional `FixupProject(outputFolder)` (default no-op). |
| `TargetScaffolders` | The registry — ordered `Keys` (`wpf, console, webapi, classlib, worker`), case-insensitive `TryResolve`, and `Default` (WPF). |
| `GeneratedFile` | One file a target contributes: `RelativeFolder` (relative to the project root, `""` for the root), `Name`, `Source`. |
| `WpfTarget`, `ConsoleTarget`, `WebApiTarget`, `ClassLibraryTarget`, `WorkerTarget` | The five shipped targets, under `Targets/`. |
| `DomainModelMapper` | `CandidateDomainModel` → `ClassDefinition[]`: renaming, `Id`, audit fields, navigation properties. The interesting stage. |
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

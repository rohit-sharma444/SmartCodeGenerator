# AppScaffolding

Turns an **approved domain model** into a **runnable WPF CRUD app** on disk. It
is the last stage of the `build app:` path: once
[`DomainDiscovery`](../DomainDiscovery/README.md) has proposed entities and the
user has approved them in the CLI's review loop, this project writes the
project file, the model classes, a JSON-backed store, view models, and a window
that can create, edit, and delete every entity.

This is a class library (`net10.0`) with no console I/O. The
[`CodeGenerator`](../CodeGenerator/README.md) CLI drives it from
`BuildAppCommand` when the user types `approve`.

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
        ▼  approve
DomainModelMapper        →  CandidateDomainModel → ClassDefinition[]  (the shaping stage)
        │
        ▼
AppScaffolder.Scaffold   →  dotnet new wpf + generated sources
        │
        ▼
ScaffoldResult           →  project path + every file written
```

## What it generates

`Scaffold(model, appName, workspaceRoot)` creates (or reuses) a WPF project at
`<workspaceRoot>/<appName>` and writes:

```
<App>/
  <App>.csproj              from DotNetProjectFactory (WPF, net10.0)
  App.xaml                  StartupUri repointed to Views/MainWindow.xaml
  Models/
    <Entity>.cs             one per entity, via Roslyn
  Data/
    JsonStore.cs            generic JSON list store, keyed by int Id
    Repository.cs           generic base
    <Entity>Repository.cs   one typed repository per entity
    RelationshipResolver.cs wires navigation properties up from foreign keys at load
  ViewModels/
    ObservableObject.cs, RelayCommand.cs
    IEntityViewModel.cs, EntityViewModel.cs
    MainViewModel.cs        built from the entity list — one tab per entity
  Views/
    MainWindow.xaml, MainWindow.xaml.cs
```

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
| `AppScaffolder` | The entry point. `Scaffold(model, appName, workspaceRoot)` writes the whole app; `WriteClasses(...)` writes just the model classes. |
| `DomainModelMapper` | `CandidateDomainModel` → `ClassDefinition[]`: renaming, `Id`, audit fields, navigation properties. The interesting stage. |
| `DomainAppName` | Free text → PascalCase project name (`"a library that lends books"` → `ALibraryThatLendsBooks`). Falls back to `GeneratedApp`; prefixes `App` when it would start with a digit. |
| `ScaffoldResult` | `Success`, the project path, every file written, and a message. |
| `WpfProjectFixup` | Reconciles what `dotnet new wpf` emits with the foldered layout: deletes the template's root `MainWindow` (it declares the same `x:Class`) and repoints `App.xaml`. |
| `*Template` | The generated sources, as raw strings. `JsonStore`, `Repository`, `RelationshipResolver`, `MainViewModel`, `MainWindow`, and the view-model base types. |

## Behaviour worth knowing

- **Writes are best-effort.** A file that cannot be written is skipped and the
  rest continue; `ScaffoldResult.Success` is false and the message says how many
  of how many landed. Nothing throws at the caller.
- **An existing project is reused, not recreated.** If `<App>.csproj` already
  exists, project creation *and* `WpfProjectFixup` are both skipped — so a
  project the user has since edited is left alone. Generated sources are still
  overwritten.
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

Covered by [`CodeGenerator.Tests`](../CodeGenerator.Tests/README.md) under
`App/` (`AppScaffolderWriteTests`, `DomainAppNameTests`) rather than by a
dedicated test project.

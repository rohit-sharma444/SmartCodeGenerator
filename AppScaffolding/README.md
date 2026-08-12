# AppScaffolding

Turns an **approved domain model** into a **runnable app** on disk. It is the
last stage of the `build app:` path: once
[`DomainDiscovery`](../DomainDiscovery/README.md) has proposed entities and the
user has approved them in the CLI's review loop, this project writes the project
file, the model classes, and a JSON-backed store with a typed repository per
entity — then hands off to the chosen **target** for the project type and front
end.

Targets are an `ITargetScaffolder` strategy, and six ship today:

| Target | `Key` | What it produces |
| --- | --- | --- |
| WPF (default) | `wpf` | A windowed CRUD app — one tab per entity. |
| Windows Desktop | `winforms` | A windowed CRUD app — one tab per entity, WinForms instead of WPF. |
| Console | `console` | A reflection-driven CRUD menu over the repositories. |
| Web API | `webapi` | A minimal API with REST CRUD endpoints per entity. |
| Class library | `classlib` | The shared core only — no entry point. |
| Worker | `worker` | A `BackgroundService` that exercises the repositories. |

The shared core is identical across all of them; only the front end differs.

`WinFormsTarget` — a runnable Windows Forms CRUD app. Shares its whole presentation layer with
`WpfTarget` through `DesktopViewModelFiles`; the only differences are the view (`Views/MainForm.cs`
plus its own `Program.cs`, instead of `MainWindow.xaml`) and which `RelayCommand` variant it
emits, since WPF's requeries through `CommandManager` and WinForms has no PresentationCore.

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

Three parts of that layout are **optional**, driven by `AppCustomization` (see
[Customization](#customization) below):

| Part | Absent when | What the emitted code looks like without it |
| --- | --- | --- |
| `Validation/` (the whole folder) | `Validation = false` | `Repository` loses its using, its `IValidator<T>` field and both `Validate` calls, and `EntityViewModel.Save` loses its validator loop — both call `ValidatorFactory.Create<T>()` otherwise, so the app would not compile. `SaveResult<T>` stays: every caller still reads `Success` and `Errors`, and a store failure still fills them. |
| `Data/SeedData.cs` | `SampleSeedData = false` **and** no imported rows | The four entry points that call `SeedData.EnsureSeeded()` — console, Web API, worker `Program.cs` and `MainViewModel` — lose that call, and the worker also loses the `Data` using it only needed for it. Imported rows arrive *through* `SeedData`, so a build with a data source keeps the file whatever the toggle says. |
| `Logging/AppLog.cs` | `Logging = false` (the default) | Nothing is emitted and nothing references it. With logging on, `Repository` logs a line per write and a warning per rejected write, and the project gains the three `Microsoft.Extensions.Logging` packages — except the Web API, whose framework reference already carries them. |

`Assets/app.<ext>` appears only when an icon was supplied, and `Views/Theme.xaml`
only when a theme or accent was.

Then the target adds its front end and reconciles the `dotnet new` output:

| Target | Folder suffix | Front end it writes |
| --- | --- | --- |
| WPF | *(none)* | `Data/RelationshipResolver.cs`, `ViewModels/` (ObservableObject, RelayCommand, IEntityViewModel, EntityViewModel, MainViewModel — one tab per entity), `Views/MainWindow.xaml(.cs)`; repoints `App.xaml`'s `StartupUri` to the foldered window. |
| Windows Desktop | `.WinForms` | The same `Data/RelationshipResolver.cs` and `ViewModels/` as WPF (via `DesktopViewModelFiles`), built with a portable `RelayCommand` that has no `CommandManager` requery; `Views/MainForm.cs` and its own `Program.cs`. |
| Console | `.Console` | `EntityMenu.cs` (a reflection-driven CRUD menu) and `Program.cs` (registers one menu per entity). |
| Web API | `.Api` | `Program.cs` mapping REST CRUD endpoints per entity (minimal API). |
| Class library | `.Lib` | *(nothing)* — the core is the deliverable; `FixupProject` deletes the template's `Class1.cs`. |
| Worker | `.Worker` | `Worker.cs` (a `BackgroundService` logging each repository's record count) and `Program.cs`. |

`RelationshipResolver.cs` lands under `Data/` but is **not shared core**: it
depends on `IEntityViewModel`, so the two desktop targets contribute it — via the
shared `DesktopViewModelFiles` — rather than `AppScaffolder`.

The generated app persists to **one JSON file per type**, written beside the
built executable (`AppContext.BaseDirectory`). There is no database.

## Customization

`Scaffold(ScaffoldRequest)` is the full entry point, and `ScaffoldRequest`
carries an `AppCustomization` describing what the user asked the generated app
to look like and contain. `AppCustomization.Default` is **byte-for-byte
neutral**: with it, every file written is identical to what this scaffolder
wrote before customization existed — that is the invariant to check any change
here against, and the reason every fragment below is empty by default.

| Setting | Reaches |
| --- | --- |
| `Company` | `<Company>`/`<Authors>`/`<Copyright>` in the project file, a `// Copyright (c) <year> <company>.` banner on every generated `.cs` (only `.cs` — `MainWindow.xaml` and `Data/Seed/*.json` go through the same writer), and the window/page title through `TitlePattern`. |
| `TitlePattern` | The WPF window's `Title`, the WinForms form's `Text`, a console banner, and a Web API root endpoint returning the title. `{company}` and `{app}` are substituted; with no company, the placeholder and its adjoining separator run are cut out, so `{company} - {app}` renders as `Shop`, not ` - Shop`. |
| `Theme`, `Accent` | `Views/Theme.xaml` (merged into `App.xaml`) for WPF, and `ColorTranslator.FromHtml` assignments in `Views/MainForm.cs` for WinForms. `ThemePalette.Resolve` returns null when nothing was themed, which is what suppresses both. An accent alone themes over the light base; an invalid accent is ignored rather than rejected. |
| `IconPath` | Copied to `Assets/app.<ext>`. A `.ico` also becomes `<ApplicationIcon>`, the icon the built .exe carries; MSBuild rejects any other extension there. WPF resolves the window's relative `Icon` attribute through the pack URI, so the file is registered as a `<Resource>`; the WinForms form opens it off disk, so its copy is content copied to the output directory. |
| `NamespacePrefix` | The namespace root every emitted file declares: `RootNamespaceFor(projectName)` puts the project name under the prefix, so `Shop.Console` becomes `CodisLtd.Shop.Console`. The project name, folder, assembly, `<AssemblyName>` and the SQLite `<PROJECT>_DB` override are deliberately untouched — the prefix is a namespace only. Four things outside the emitted set are reconciled to match, all on every run so a changed or dropped prefix cannot leave a stale one behind: `<RootNamespace>` is written into the app and test project files, WPF's template-generated `App.xaml`/`App.xaml.cs` are repointed at it, and an already-written write-once `<Entity>Validator.Custom.cs` stub has its namespace header (and any `using` rooted at the old one) repointed too — only the header moves; the hand-written body below it is never touched. |
| `Validation`, `SampleSeedData`, `Logging` | The three optional parts in the table above. |
| `Year` (on the request, not the record) | The copyright banner and `<Copyright>`. Supplied by the caller, never read from the clock inside a template, or golden-output tests would redden on 1 January. |

A target that varies its front end with any of this overrides the
customization-aware `FrontEndFiles(projectName, entityNames, customization)`
overload; the default interface method forwards to the two-argument one, so the
targets that do not care are untouched.

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
| `AppScaffolder` | The entry point. `Scaffold(ScaffoldRequest)` writes the shared core and delegates the front end to the request's target; the older overloads (`Scaffold(model, appName, workspaceRoot, target[, persistence[, importedData]])`, and a three-arg one defaulting to WPF) are thin wrappers over it with neutral customization. `WriteClasses(...)` writes just the model classes. |
| `ScaffoldRequest` | Everything one run needs: model, app name, workspace root, target, `PersistenceKind`, `ImportedData?`, `AppCustomization` and the copyright `Year`. A parameter object because the overload chain already stood at four overloads and six arguments. `For(...)` builds one with today's defaults. |
| `AppCustomization` | The settings record — company, theme, accent, title pattern, icon, namespace prefix, and the three layer toggles — plus `Default` (neutral), `TitleFor`/`TitleLiteralFor`/`TitleAttributeFor` (the title escaped for plain text, a C# literal and an XML attribute) and `IconFileName`. |
| `NamespacePrefix` | Whether a prefix can be written after `namespace` — Roslyn's `SyntaxFacts.IsValidIdentifier` plus a reserved-keyword check per dot-separated segment, so contextual keywords like `var` stay legal — and `FromCompany`, which reduces a company name to an identifier for the wizard to offer, or null when it cannot. |
| `ThemePalette` | Theme + accent → six named colours, or **null** when nothing was themed, which is what suppresses every themed fragment. `IsValidHexColour` is the one spelling of `#rrggbb` this assembly and the CLI share. |
| `FileHeader` | The one-line copyright banner, or empty with no company. Takes the year as a parameter. |
| `ThemeXamlTemplate`, `LoggingTemplate` | The two optional emitted files: `Views/Theme.xaml` (a `ResourceDictionary` of brushes) and `Logging/AppLog.cs` (a static logger factory). `LoggingTemplate.Packages` pins the package versions a logging build restores. |
| `ImportedData` | Entity name → rows → field name → raw string value, plus `Notes` the caller must print (above all, that the `MaxSeedRows` cap dropped rows). Lives here rather than in `DataIngestion` because it is an *input to the scaffolder*, alongside `PersistenceKind` — and because that keeps it free of any `TabularTable` dependency, so a future SQL reader feeds the same carrier. |
| `SeedFileWriter` | Imported rows → `(fileName, json)` pairs, projected through each class's final property list. `SeedFolderName` is the one spelling of the `Seed` folder that both this and the generated seeder use. |
| `ITargetScaffolder` | The strategy a target implements: `Key`, `Description`, `ProjectType`, `FolderSuffix`, `FrontEndFiles(projectName, entityNames)`, and two optional default interface methods — `FixupProject(outputFolder)` (no-op) and `FrontEndFiles(projectName, entityNames, customization)` (forwards to the two-argument overload). |
| `TargetScaffolders` | The registry — ordered `Keys` (`wpf, console, webapi, classlib, worker, winforms`; `winforms` is appended rather than inserted, so every pre-existing key keeps its position), case-insensitive `TryResolve`, and `Default` (WPF). |
| `GeneratedFile` | One file a target contributes: `RelativeFolder` (relative to the project root, `""` for the root), `Name`, `Source`. |
| `WpfTarget`, `ConsoleTarget`, `WebApiTarget`, `ClassLibraryTarget`, `WorkerTarget`, `WinFormsTarget` | The six shipped targets, under `Targets/`. |
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
`WebApiTargetTests`, `ClassLibraryTargetTests`, `WorkerTargetTests`,
`WinFormsTargetTests`, plus `WinFormsProjectFixupTests` and
`WinFormsTemplateTests`), `TargetScaffoldersTests` for the registry, and
`GeneratedProjectBuildTests` — a `DataRow`-driven integration test that runs
`dotnet build` on the generated project for every target, skipping `wpf` and
`winforms` on non-Windows since they need the desktop targeting pack.

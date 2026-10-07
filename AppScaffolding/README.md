# AppScaffolding

Turns an **approved domain model** into a **runnable app** on disk. It is the
last stage of the `build app:` path: once
[`DomainDiscovery`](../DomainDiscovery/README.md) has proposed entities and the
user has approved them in the CLI's review loop, this project writes the project
file, the model classes, and a JSON- or SQLite-backed store with a typed repository per
entity — then hands off to the chosen **target** for the project type and front
end.

Targets are an `ITargetScaffolder` strategy, and seven ship today:

| Target | `Key` | What it produces |
| --- | --- | --- |
| WPF (default) | `wpf` | A windowed CRUD app in the Business layout by default (see [Layouts](#layouts-screens-and-sign-in)). |
| Windows Desktop | `winforms` | The same, in WinForms instead of WPF. |
| Console | `console` | A reflection-driven CRUD menu over the repositories. |
| Web API | `webapi` | A minimal API with REST CRUD endpoints per entity. |
| Web Application | `blazor` | A Blazor Server app — one page per entity, editable in the browser. |
| Class library | `classlib` | The shared core only — no entry point. |
| Worker | `worker` | A `BackgroundService` that exercises the repositories. |

The shared core is identical across all of them; only the front end differs.

`WinFormsTarget` — a runnable Windows Forms CRUD app. Shares its whole presentation layer with
`WpfTarget` through `DesktopViewModelFiles`; the only differences are the view (`Views/MainForm.cs`
plus its own `Program.cs`, instead of `MainWindow.xaml`) and which `RelayCommand` variant it
emits, since WPF's requeries through `CommandManager` and WinForms has no PresentationCore.

`BlazorTarget` — the browser equivalent of the desktop targets rather than a second `webapi`: the
same list-edit-save surface, reached with a URL instead of a window. It is the only target that
emits no `Program.cs` of its own — `dotnet new blazor` on .NET 10 already registers interactive
server components and the render mode, so the pages only declare `@rendermode InteractiveServer`.
`BlazorProjectFixup` does still **edit** the template's `Program.cs`, best-effort and
idempotently: it repoints the components import at the app's root namespace and, when the app has
a login, adds cookie authentication. Two consequences follow from the missing startup hook of its
own. Pages are emitted as `<Entity>Page.razor`, because a Razor file compiles to a class named
after it and `Book.razor` would shadow `Models.Book`. And seeding cannot happen at startup, so the
first page view runs it through `BlazorSeedTemplate`'s locked one-shot gate instead.

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
    SaveResult.cs           what a validating write returns (Delete included)
    EfStore.cs, AppDbContext.cs      instead of JsonStore.cs, with SQLite persistence
  Validation/
    ValidationContext.cs    the context, ValidationOperation, ValidationError, ValidationOutcome
    ValidationPipeline.cs   runs a validator's stages in order
    IValidator.cs           the seam: ValidationOutcome Validate(ValidationContext<T>)
    IEntityLookup.cs        existence checks a validator needs for foreign keys
    StoreEntityLookup.cs    the real lookup, reading through the store
    ValidatorFactory.cs     type → validator, mirroring StoreFactory
    Permissions.cs          who may do what — emitted even with `novalidation`
    ActingUser.cs           the signed-in user's role, for Permissions
    <Entity>Validator.Generated.cs   the inferred rules — REGENERATED
    <Entity>Validator.Custom.cs      hand-written rules — written ONCE
  Auth/, Models/User.cs     the sign-in core — only when the app has a login (see below)
```

Three parts of that layout are **optional**, driven by `AppCustomization` (see
[Customization](#customization) below):

| Part | Absent when | What the emitted code looks like without it |
| --- | --- | --- |
| `Validation/` (most of the folder) | `Validation = false` | `Repository` loses its using, its `IValidator<T>` field and its `Validate` calls, and `EntityViewModel.Save` loses its validator loop — both call `ValidatorFactory.Create<T>()` otherwise, so the app would not compile. `SaveResult<T>` stays: every caller still reads `Success` and `Errors`, and a store failure still fills them. `ValidationContext.cs`, `IEntityLookup.cs`, `Permissions.cs` and `ActingUser.cs` are still emitted: turning validation off must not turn permissions off. |
| `Data/SeedData.cs` | `SampleSeedData = false` **and** no imported rows | The five entry points that call `SeedData.EnsureSeeded()` — console, Web API, worker `Program.cs`, `MainViewModel`, and the Blazor target's `Data/AppSeed.cs` (which is not emitted at all in that case, and whose call disappears from every page with it) — lose that call, and the worker also loses the `Data` using it only needed for it. Imported rows arrive *through* `SeedData`, so a build with a data source keeps the file whatever the toggle says. |
| `Logging/AppLog.cs` | `Logging = false` (the default) | Nothing is emitted and nothing references it. With logging on, `Repository` logs a line per write and a warning per rejected write, and the project gains the three `Microsoft.Extensions.Logging` packages — except the Web API, whose framework reference already carries them. |

`Assets/app.<ext>` appears only when an icon was supplied, and `Views/Theme.xaml`
only when a theme or accent was.

The scaffolder also writes `.codegen/generated-files.txt` into the project folder: every file it
wrote, each with a hash. `GeneratedFilesManifest` uses it on the next run to remove a file an
earlier run generated and this one did not (a class renamed or removed in the review loop) — but
only while the file still holds exactly what was written. A file the user has changed is kept and
reported, and a file the scaffolder never wrote is never touched. The SDK leaves dot-folders out of
every item glob, so the record is never compiled or published.

Then the target adds its front end and reconciles the `dotnet new` output:

| Target | Folder suffix | Front end it writes |
| --- | --- | --- |
| WPF | *(none)* | `Data/RelationshipResolver.cs`, `ViewModels/` (ObservableObject, RelayCommand, IEntityViewModel, EntityViewModel, MainViewModel), `Views/MainWindow.xaml(.cs)` shaped by the chosen layout, plus the screen and sign-in files below; repoints `App.xaml`'s `StartupUri` to the foldered window. |
| Windows Desktop | `.WinForms` | The same `Data/RelationshipResolver.cs` and `ViewModels/` as WPF (via `DesktopViewModelFiles`), built with a portable `RelayCommand` that has no `CommandManager` requery; `Views/MainForm.cs` and its own `Program.cs`. |
| Console | `.Console` | `EntityMenu.cs` (a reflection-driven CRUD menu) and `Program.cs` (registers one menu per entity). |
| Web Application | `.Web` | One list page and one editor page per entity, the nav menu and home page, the seed gate, and — with a login — `Login.razor`, `Routes.razor`, `Auth/AuthEndpoints.cs` and `UsersPage.razor`. |
| Web API | `.Api` | `Program.cs` mapping REST CRUD endpoints per entity (minimal API). |
| Class library | `.Lib` | *(nothing)* — the core is the deliverable; `FixupProject` deletes the template's `Class1.cs`. |
| Worker | `.Worker` | `Worker.cs` (a `BackgroundService` logging each repository's record count) and `Program.cs`. |

`RelationshipResolver.cs` lands under `Data/` but is **not shared core**: it
depends on `IEntityViewModel`, so the two desktop targets contribute it — via the
shared `DesktopViewModelFiles` — rather than `AppScaffolder`.

By default the generated app persists to **one JSON file per type**, written beside the
built executable (`AppContext.BaseDirectory`). With `PersistenceKind.Sqlite` it emits
`Data/EfStore.cs` and `Data/AppDbContext.cs` instead (EF Core + SQLite, with the
`Microsoft.EntityFrameworkCore.Sqlite` package added to the project); both stores implement the same
`IStore<T>` seam, so `Repository`, `SeedData` and the front ends do not change. The CLI refuses
SQLite for the WPF and Windows Forms targets (`BuildAppCommand`).

## Layouts, screens and sign-in

These three apply to the targets with screens (`ITargetScaffolder.HasScreens`: WPF, WinForms and
Blazor), and all three come from `AppCustomization`.

**Layouts.** `AppLayout` picks the shape of a desktop front end: `Tabs` (a tab per entity — what
the generator emitted before layouts existed, and the value every layout mechanism must reproduce
byte for byte), `Sidebar`, `MasterDetail`, `Dashboard` and `Business`. A null `Layout` resolves to
**`Business`** through `EffectiveLayout`, not to tabs: a dashboard home, a rail grouped into masters
and transactions, and an editor that replaces the content area. `AppLayoutCatalog` holds the keys
the CLI offers (`tabs|sidebar|master-detail|dashboard`); `Layouts/Wpf/` and `Layouts/WinForms/`
hold one emitter per layout.

**Roles and entry templates.** `EntityRoleMap` classifies each entity as a `Master` (reference
data; the safe default), a `Transaction` (references reference data, may hold lines) or a
`DocumentLine` (no screen of its own; edited inside its parent). `customization.Transactions` and
`Masters` override the guess, and `ScreenPlan` settles roles, classes and the login in one call so
the Review page's summary and the emitted files cannot disagree. In the Business layout an
`EntryTemplate` arranges an entry form: `Classic`, `Grouped` (the default), `Tabbed`, `TwoColumn`,
`Split` or `Ribbon`.

**Sign-in.** `Login` is **true** in `AppCustomization.Default`, so the Default profile is not
neutral: the byte-for-byte invariant above holds for the targets without screens, and a screen
target gets the auth core unless the user says `nologin`. `ScreenPlan` computes
`login = customization.Login && target.HasScreens`, so the console, Web API, worker and class
library never get it. The core is `Models/User.cs`, `Auth/AppRole.cs` (`Admin`, `User`),
`PasswordHasher`, `AuthService` and `UserSeed`; it occupies the entity name `User`, so a discovered
entity of that name is renamed around it. WPF and WinForms add a login view, `Session` and a users
screen; Blazor uses cookie authentication and reads its user from the authentication state.
`Permissions.Allows` enforces the role matrix in the validator's `Security` stage (an Admin may do
anything; a User may not delete a master record or reach the Users screen).

**Adding to an existing app.** `IncrementalScaffolder.Add` scaffolds the whole app into a
throwaway root and copies across only the files `IncrementalFiles.ShouldCopy` allows, so every
other file of the app is untouched by construction. It reports validator stubs still written in the
pre-pipeline shape (`IncrementalResult.OutdatedValidatorStubs`).

## Customization

`Scaffold(ScaffoldRequest)` is the full entry point, and `ScaffoldRequest`
carries an `AppCustomization` describing what the user asked the generated app
to look like and contain. `AppCustomization.Default` is neutral for **branding**:
with it, no company, theme, icon or prefix reaches any file — the reason every fragment below is
empty by default. It is **not** byte-for-byte what the scaffolder wrote before customization
existed: `Login` is true and the layout resolves to Business, so a WPF, WinForms or Blazor app
built with the Default profile carries the sign-in core and the business shell.

| Setting | Reaches |
| --- | --- |
| `Company` | `<Company>`/`<Authors>`/`<Copyright>` in the project file, a `// Copyright (c) <year> <company>.` banner on every generated `.cs` (only `.cs` — `MainWindow.xaml` and `Data/Seed/*.json` go through the same writer), and the window/page title through `TitlePattern`. |
| `TitlePattern` | The WPF window's `Title`, the WinForms form's `Text`, a console banner, and a Web API root endpoint returning the title. `{company}` and `{app}` are substituted; with no company, the placeholder and its adjoining separator run are cut out, so `{company} - {app}` renders as `Shop`, not ` - Shop`. |
| `Theme`, `Accent` | `Views/Theme.xaml` (merged into `App.xaml`) for WPF, and `ColorTranslator.FromHtml` assignments in `Views/MainForm.cs` for WinForms. `ThemePalette.Resolve` returns null when nothing was themed, which is what suppresses both. An accent alone themes over the light base; an invalid accent is ignored rather than rejected. |
| `IconPath` | Copied to `Assets/app.<ext>`. A `.ico` also becomes `<ApplicationIcon>`, the icon the built .exe carries; MSBuild rejects any other extension there. WPF resolves the window's relative `Icon` attribute through the pack URI, so the file is registered as a `<Resource>`; the WinForms form opens it off disk, so its copy is content copied to the output directory. |
| `NamespacePrefix` | The namespace root every emitted file declares: `RootNamespaceFor(projectName)` puts the project name under the prefix, so `Shop.Console` becomes `CodisLtd.Shop.Console`. The project name, folder, assembly, `<AssemblyName>` and the SQLite `<PROJECT>_DB` override are deliberately untouched — the prefix is a namespace only. Four things outside the emitted set are reconciled to match, all on every run so a changed or dropped prefix cannot leave a stale one behind: `<RootNamespace>` is written into the app and test project files, WPF's template-generated `App.xaml`/`App.xaml.cs` are repointed at it, and an already-written write-once `<Entity>Validator.Custom.cs` stub has its namespace header (and any `using` rooted at the old one) repointed too — only the header moves; the hand-written body below it is never touched. |
| `Validation`, `SampleSeedData`, `Logging` | The three optional parts in the table above. |
| `Login`, `Transactions`, `Masters`, `Layout`, `EntryTemplate` | The sign-in core, the role overrides, the desktop layout and the Business entry form — see [Layouts, screens and sign-in](#layouts-screens-and-sign-in). |
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

Each model gets its own validator, split across two files. A validator is a **staged
pipeline**: `Validate(ValidationContext<T>)` runs its stages through `ValidationPipeline.Run` and
returns a `ValidationOutcome` of `ValidationError`s. The context carries the item, the
`ValidationOperation` (`Insert`, `Update`, `Delete`, `Post`), the stored version, the entity lookup
and the acting user's role and id. The stages run in this order:

1. **Security** — refuses an operation the user's role may not perform, before any field is
   examined, so an unauthorised caller is never told which fields were wrong.
2. **Field** — the inferred per-property rules, then the hand-written hook.
3. **Entity** — foreign-key existence; only when the model declares a foreign key.
4. **Business** — rules inferred from the model, when it has any.
5. **Workflow** — a status column's transitions, forward-only through a stated order; only when the
   model has one. It runs last and reads `context.Stored`.

The two files:

- **`<Entity>Validator.Generated.cs`** is rewritten on every build. It carries the
  rules `DomainModelMapper.AddValidation` already recorded on the model —
  required, max length, email and phone shape — plus the stages above. Nothing is
  re-inferred here, so a model's attributes and its
  validator cannot disagree. A `[Key]` `Id` gets no check (the store assigns it,
  so validating it would reject every new record), and navigation properties get
  none either. Each property's checks sit in their own `try`/`catch`, so one
  throwing rule cannot abort the rest.
- **`<Entity>Validator.Custom.cs`** is written **once** and never overwritten —
  it is where your own cross-field and business rules live, and regenerating the
  app must not discard them. It implements the hook
  `private static partial void ValidateCustom(T item, List<ValidationError> errors)`
  that the generated half calls. **Deleting the file does not compile any more:** the hook is a
  `private static partial` method, which needs an implementing half, so with no file
  the build fails with CS8795. Keep the stub, even empty. A stub written before the pipeline
  (an instance `partial void ValidateCustom(T, List<string>)`) fails with CS0759/CS8795 too, and
  `IncrementalResult.OutdatedValidatorStubs` names such files.

`ValidatorFactory.Create<T>()` resolves a type to its validator, deliberately
mirroring `StoreFactory.Create<T>()` — the front-end templates are generic over
`T` and so cannot name `CustomerValidator` directly. A type with no generated
validator degrades to a no-op `NullValidator<T>` rather than throwing.

`Repository<T>.Create`, `Update` and `Delete` validate **before** touching the store and
return `SaveResult<T>`, which keeps `NotFound` distinct from `Errors`: an
`Update` naming an unknown Id is a different failure from one carrying an
invalid record, and the Web API front end maps them to 404 and 400 respectively.
`Delete` judges the record that is about to go (it is both the item and the stored version) and
returns a `SaveResult<T>`. `GetAll` and `GetById` are unchanged.

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

### The generated test project

Each app also gets a sibling `<App>.Tests` xUnit project (`ScaffoldTestProject`) referencing it, with
`RepositoryRoundTripTests.cs`, `SeedDataTests.cs` when the app seeds, and a
`<Entity>ValidatorTests.cs` per model, using a fake `IEntityLookup` so no store is needed, plus a
shared `TestSupport` helper that fills an entity with values the generated rules accept. It is
best-effort: if it cannot be made, the app is left intact and `ScaffoldResult.TestProjectProblem`
carries one sentence saying why. Its target framework mirrors the app's, so a WPF app's tests
target `net10.0-windows`.

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
| `ImportedData` | Entity name → rows → field name → raw string value, plus `Notes` the caller must print (above all, that the `MaxSeedRows` cap dropped rows). Lives here rather than in `DataIngestion` because it is an *input to the scaffolder*, alongside `PersistenceKind` — and because that keeps it free of any `TabularTable` dependency, so every reader (CSV, TXT, JSON, Excel, SQL Server) feeds the same carrier. |
| `SeedFileWriter` | Imported rows → `(fileName, json)` pairs, projected through each class's final property list. `SeedFolderName` is the one spelling of the `Seed` folder that both this and the generated seeder use. |
| `ITargetScaffolder` | The strategy a target implements: `Key`, `Description`, `ProjectType`, `FolderSuffix`, `FrontEndFiles(projectName, entityNames)`, and two optional default interface methods — `FixupProject(outputFolder)` (no-op) and `FrontEndFiles(projectName, entityNames, customization)` (forwards to the two-argument overload). |
| `TargetScaffolders` | The registry — ordered `Keys` (`wpf, console, webapi, classlib, worker, winforms, blazor`; new targets are appended rather than inserted, so every pre-existing key keeps its position and `Default` stays `All[0]`), case-insensitive `TryResolve`, and `Default` (WPF). |
| `AppLayout`, `AppLayoutCatalog` | The five desktop layouts and the keys the CLI offers; `Layouts/` holds the emitters. |
| `ScreenPlan`, `EntityRoleMap`, `EntryTemplate` | Roles, classes and the login decided in one call; the entry-form arrangements; under `Screens/`. |
| `GeneratedFilesManifest` | The hashed record of what a run wrote, used to sweep stale files. |
| `IncrementalScaffolder` | Adds new classes to an existing app, writing only what is missing. |
| `GeneratedFile` | One file a target contributes: `RelativeFolder` (relative to the project root, `""` for the root), `Name`, `Source`. |
| `WpfTarget`, `ConsoleTarget`, `WebApiTarget`, `ClassLibraryTarget`, `WorkerTarget`, `WinFormsTarget`, `BlazorTarget` | The seven shipped targets, under `Targets/`. |
| `BlazorPageTemplate`, `BlazorShellTemplate`, `BlazorSeedTemplate` | What the Blazor target emits: one page per class (columns and editors reflected over the model's public properties), the nav menu and home page that replace the template's own, and the locked one-shot gate that seeds on the first page view — this is the one target that emits no `Program.cs` of its own, so it has no startup hook to seed from. |
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
`WinFormsTargetTests`, `BlazorTargetTests`, plus `WinFormsProjectFixupTests` and
`WinFormsTemplateTests`), `TargetScaffoldersTests` for the registry, and
`GeneratedProjectBuildTests` — a `DataRow`-driven integration test that runs
`dotnet build` on the generated project for every target, skipping `wpf` and
`winforms` on non-Windows since they need the desktop targeting pack.

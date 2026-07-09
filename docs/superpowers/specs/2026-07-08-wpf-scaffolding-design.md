# `build app:` WPF Application + Foldered Project Layout — Design Spec

**Date:** 2026-07-08
**Status:** Approved (design)
**Branch:** `feat/build-app-wpf`

## Goal

On the `build app:` **approve** action, generate a **WPF desktop application**
instead of a console application, and lay its files out in a conventional WPF
folder structure (`Models/`, `Data/`, `ViewModels/`, `Views/`) with namespaces
that match the folders.

`dotnet run` on the generated project opens a window with one tab per entity.
Each tab shows a `DataGrid` over that entity's records with Add / Delete / Save.
Records persist to per-entity JSON files that survive restarts, exactly as they
do today.

This supersedes the console host delivered by
`docs/superpowers/specs/2026-07-08-build-app-runnable-crud-design.md`. It builds
on the model generation from
`docs/superpowers/specs/2026-07-07-build-app-scaffolding-design.md`.

## Background

`AppScaffolder.Scaffold` currently creates a `dotnet new console` project and
writes every file flat into the project root: one `<Entity>.cs` per entity, plus
`JsonStore.cs` and a reflection-driven console-menu `Program.cs`. Everything —
entities, store, menu — sits in the single fixed namespace `GeneratedCode`,
which `RoslynClassGenerator.Generate` hardcodes.

Two problems, and they are the two the user raised:

1. The host is a console app. A generated CRUD app is a line-of-business tool;
   a window with a grid is the natural shape for it.
2. A flat root folder is not a project layout. Folders alone would not be either:
   folders whose names do not match namespaces are a cosmetic gesture that trips
   every analyzer and every developer who opens the project.

## Approach

Switch the project template to `wpf` and let **one static XAML view** render any
entity set. `MainWindow.xaml` binds a `TabControl` to a collection of
non-generic `IEntityViewModel`, with a `ContentTemplate` containing a single
auto-column `DataGrid`. Entity names therefore appear in exactly **one generated
file**, `MainViewModel.cs` — one construction line each. No per-entity XAML, no
per-entity view model, no reflection in the view.

Light hand-rolled MVVM (`ObservableObject`, `RelayCommand`) — no
CommunityToolkit.Mvvm, so a scaffolded app needs no NuGet restore beyond what
`DotNetProjectFactory` already does.

`AppScaffolding` continues to own generation via C# **string templates**, not
Roslyn syntax factory and not the engine's `RepositoryGenerator` /
`ServiceGenerator` stubs.

## Project template

`DotNetProjectFactory` already maps `DotNetProjectType.Wpf` → `dotnet new wpf`.
Verified: `dotnet new wpf -f net10.0` succeeds and emits a csproj with
`<TargetFramework>net10.0-windows</TargetFramework>`, `<UseWPF>true</UseWPF>`,
`<OutputType>WinExe</OutputType>`. The existing `-f {Framework}` argument needs
no change; `AppScaffolder` only switches `ProjectType` from `Console` to `Wpf`.

The template emits five files: `App.xaml`, `App.xaml.cs`, `AssemblyInfo.cs`,
`MainWindow.xaml`, `MainWindow.xaml.cs`, with root namespace = project name.
The WPF SDK's default globs include `**/*.cs` as `Compile` and `**/*.xaml` as
`Page`, so subfolders need no csproj edits.

The generated app is **Windows-only**, a direct consequence of choosing WPF.

## What gets generated

Into `<workspaceRoot>/<AppName>/`:

```
<AppName>.csproj          dotnet new wpf                        [template, untouched]
App.xaml                  StartupUri rewritten                  [template, edited]
App.xaml.cs                                                     [template, untouched]
AssemblyInfo.cs                                                 [template, untouched]
Models/<Entity>.cs        namespace <AppName>.Models            [generated per entity]
Data/JsonStore.cs         namespace <AppName>.Data              [static template]
ViewModels/
  ObservableObject.cs     namespace <AppName>.ViewModels        [static template]
  RelayCommand.cs         namespace <AppName>.ViewModels        [static template]
  IEntityViewModel.cs     namespace <AppName>.ViewModels        [static template]
  EntityViewModel.cs      namespace <AppName>.ViewModels        [static template]
  MainViewModel.cs        namespace <AppName>.ViewModels        [generated]
Views/
  MainWindow.xaml         x:Class="<AppName>.Views.MainWindow"  [static template]
  MainWindow.xaml.cs      namespace <AppName>.Views             [static template]
```

"Static template" means the source varies only by `<AppName>` in its namespace
and `using` lines — not by entity.

Three edits to what `dotnet new wpf` produced:

- **`App.xaml`**: `StartupUri="MainWindow.xaml"` → `StartupUri="Views/MainWindow.xaml"`.
  Rewritten by string replacement on the template's own output, not regenerated,
  so the template's `x:Class` and `xmlns` survive.
- **Root `MainWindow.xaml` and `MainWindow.xaml.cs`: deleted.** Not shadowed —
  deleted. Leaving them alongside `Views/MainWindow.*` declares
  `x:Class="<AppName>.MainWindow"` twice and fails the build.
- Nothing else. `AssemblyInfo.cs` and the csproj are left exactly as emitted.

## Namespaces

`RoslynClassGenerator.Generate(ClassDefinition model)` gains an optional second
parameter:

```csharp
public static string Generate(ClassDefinition model, string namespaceName = "GeneratedCode")
```

Purely additive. The three call sites are
`AppScaffolding/AppScaffolder.cs:26`,
`CodeGenerationEngine/Service/CommandHandlingService.cs:486`, and
`CodeGenerator/App/CodeGeneratorStarter.cs:543`; only the first passes an
argument (`$"{appName}.Models"`), and the other two keep their current output
byte-for-byte. Every call site is checked, not assumed.

The generated app therefore has one namespace root, `<AppName>`, and every
folder corresponds to a namespace under it. `MainViewModel` declares
`using <AppName>.Models;` and `using <AppName>.Data;`.

`<AppName>` is produced by the existing `DomainAppName.FromDescription`, which
already guarantees a PascalCase identifier that does not start with a digit —
so it is safe as a namespace root.

## Components

### `MainViewModel` (generated)

The only file whose shape depends on the entity set:

```csharp
public sealed class MainViewModel
{
    public ObservableCollection<IEntityViewModel> Entities { get; } = new()
    {
        new EntityViewModel<Recipe>("Recipe"),
        new EntityViewModel<Ingredient>("Ingredient"),
    };
}
```

One line per entity. Entity names arrive already de-conflicted by
`DomainModelMapper.ToClassDefinitions`.

### `IEntityViewModel` / `EntityViewModel<T>` (static)

```csharp
public interface IEntityViewModel
{
    string Title { get; }
    IEnumerable Items { get; }
    object? Selected { get; set; }
    ICommand AddCommand { get; }
    ICommand DeleteCommand { get; }
    ICommand SaveCommand { get; }
    string Status { get; }
}
```

`EntityViewModel<T> : ObservableObject, IEntityViewModel where T : new()` holds
`ObservableCollection<T> Items` and a `JsonStore<T>`, loading on construction.

`DataGrid` derives its columns from the **generic type argument** of the bound
`IEnumerable<T>`, via the collection view's item properties. Columns therefore
appear **even when the collection is empty** — which is the state every freshly
scaffolded app starts in. This is why the non-generic `Items` property still
returns the `ObservableCollection<T>` instance rather than, say, a `List<object>`.

### `MainWindow.xaml` (static)

`TabControl ItemsSource="{Binding Entities}"`, `DisplayMemberPath="Title"`,
`ContentTemplate` → `DataGrid ItemsSource="{Binding Items}"`
`SelectedItem="{Binding Selected}"` `AutoGenerateColumns="True"`
`CanUserAddRows="False"`, plus Add / Delete / Save buttons and a status bar bound
to `Status`.

`CanUserAddRows="False"`: the grid's built-in new-row placeholder would create a
`T` with `Id = 0`, bypassing `NextId()`. The explicit `Add` command is the single
path that mints an `Id`.

### `MainWindow.xaml.cs` (static)

`DataContext = new MainViewModel();` and one `AutoGeneratingColumn` handler that
sets `IsReadOnly` on the `Id` column. That handler is the **only** code-behind —
it is a view concern (column presentation), so it belongs here rather than in a
view model.

### `JsonStore<T>` (static, reshaped)

Surface becomes `GetAll()`, `SaveAll(List<T>)`, `NextId(IEnumerable<T>)`. The
per-record `Add` / `Update` / `Delete` are **removed**: a `DataGrid` edits in
place and commits as a batch, so those three methods would ship as dead code in
every scaffolded app. The reflection-based `Id` get/set is retained and still
confined to this one file.

### `AppScaffolder.Scaffold` (changed)

`ProjectType = DotNetProjectType.Wpf`. `WriteClasses` takes a subfolder. A
`Directory.CreateDirectory` per subfolder precedes its writes; a folder that
cannot be created fails its own files and not the run, preserving the existing
best-effort contract. `ScaffoldResult.FilesWritten` now carries paths containing
subfolders.

### Deleted

`AppScaffolding/ProgramTemplate.cs` and
`CodeGenerator.Tests/App/ProgramTemplateTests.cs`. The console host is replaced,
not retained behind a flag — no caller asked for one.

## Data flow

```
build app: <desc>
  → DomainDiscoveryPipeline.Discover → review loop → approve
    → AppScaffolder.Scaffold(model, appName, workspaceRoot)
        → DomainModelMapper.ToClassDefinitions(model)      # + guaranteed Id
        → DotNetProjectFactory.CreateProject (Wpf; reuse if csproj exists)
        → delete root MainWindow.xaml + MainWindow.xaml.cs
        → rewrite App.xaml StartupUri → Views/MainWindow.xaml
        → WriteClasses("Models", ...)                      # <Entity>.cs
        → write Data/JsonStore.cs
        → write ViewModels/{ObservableObject,RelayCommand,
                            IEntityViewModel,EntityViewModel}.cs
        → write ViewModels/MainViewModel.cs                # entity names here
        → write Views/MainWindow.xaml + .xaml.cs
    → print project path + files written (paths relative to project root)
```

At runtime: `App.xaml` → `Views/MainWindow.xaml` → `DataContext = MainViewModel`
→ one `EntityViewModel<T>` per entity, each loading `store.GetAll()` into `Items`.

| Command | Behavior |
|---|---|
| `Add` | `Items.Add(new T { Id = store.NextId(Items) })`; the grid shows the blank row |
| `Delete` | removes `Selected`; `CanExecute` false when `Selected is null` |
| `Save` | `store.SaveAll(Items.ToList())` → writes `<T>.json` at `AppContext.BaseDirectory` |

Edits live in memory until `Save`. That is the honest contract for a `DataGrid`
and avoids a file write per keystroke. `Save` sets `Status` so the user sees that
something persisted.

## Error handling

**Scaffolding-time** (our code): unchanged best-effort contract. Each file write
swallows `IOException` / `UnauthorizedAccessException` and the run reports
`n of m files written`. New: because `FilesWritten` now contains subfolder paths,
`BuildAppCommand` prints each path **relative to the project root** instead of
`Path.GetFileName(file)` — otherwise `Models/Recipe.cs` and a root `Recipe.cs`
would print identically.

**Runtime** (generated app): a corrupt or hand-edited `<T>.json` currently throws
out of `JsonSerializer.Deserialize`. In a console app that printed a stack trace;
in WPF it takes the process down with an unhandled-exception dialog before the
window appears. Therefore:

- `GetAll()` catches `JsonException` and returns an empty list.
- `SaveAll()` catches `IOException` / `UnauthorizedAccessException` and surfaces
  the failure through `Status` rather than crashing.

## Testing

Unit (MSTest, `CodeGenerator.Tests`):

- `RoslynClassGeneratorTests` (new): `Generate(model)` still emits
  `namespace GeneratedCode`; `Generate(model, "X.Models")` emits `namespace X.Models`.
- `JsonStoreTemplateTests` (rewritten): source contains `class JsonStore`,
  `GetAll`, `SaveAll`, `NextId`; declares the namespace passed in; contains no
  `public T Add(` / `public bool Update(` / `public bool Delete(`.
- `WpfTemplateTests` (new): `MainViewModel` source names each entity exactly once
  and declares `using <AppName>.Models;`; `MainWindow.xaml` contains a `TabControl`
  bound to `Entities` and a `DataGrid` with `CanUserAddRows="False"`;
  `x:Class` matches `<AppName>.Views.MainWindow`.
- `AppScaffolderWriteTests` (extended): entity files land in `Models/`;
  `Data/JsonStore.cs` and the five `ViewModels/*.cs` exist; no root
  `MainWindow.xaml` or `MainWindow.xaml.cs` remains; `App.xaml` contains
  `StartupUri="Views/MainWindow.xaml"`; best-effort partial-write behavior
  still holds.
- `ProgramTemplateTests`: deleted.
- `DomainModelMapperTests`, `DomainAppNameTests`: unchanged. The `ReservedNames`
  set in `DomainModelMapper` is re-examined against the new templates' simple
  names (`MainViewModel`, `EntityViewModel`, `RelayCommand`, `ObservableObject`,
  `MainWindow`, `App`) and extended if an entity could collide.

**These tests assert on strings, not compilability.** XAML compilation requires
MSBuild, so no unit test proves the scaffolded app builds or launches. That gap
is closed by a manual smoke step, and its actual output is reported rather than
assumed:

- `build app: recipe management` → approve → `dotnet build` the generated project
  and report what it prints.
- Launch it; confirm a tab per entity, Add a row, Save, close, relaunch, and
  confirm the row is still there (proving `<T>.json` round-trips).

Note for whoever runs that smoke step: **rebuild `CodeGenerator` before verifying
through the CLI.** `dotnet test` leaves the CLI's `bin` stale, so a working change
can look dead.

## Scope boundaries (YAGNI)

- No console host retained, and no `--console` flag.
- No relationships / foreign keys between entities; no navigation between tabs.
- No validation attributes surfaced in the UI.
- No search, filter, paging, or sorting.
- No theming, styling, or window-size persistence. Default WPF chrome.
- JSON files only — no database, EF Core, or migrations.
- No undo. `Delete` + `Save` is destructive, as it was in the console app.
- **Not touched (pre-existing, orthogonal):** `DotNetProjectFactory`'s
  unconditional `dotnet add package System.ComponentModel.Annotations`, and
  `DomainAppName`'s ability to emit very long project names.

## Risks

- **Windows-only generated app.** Inherent to WPF, and accepted. `dotnet build`
  of a `net10.0-windows` WPF project fails on non-Windows hosts. This repo is
  developed on Windows; the *generator* stays cross-platform, only its output
  does not.
- **`DataGrid` auto-columns depend on the `IEnumerable<T>` type argument.** If a
  future refactor exposes `Items` as a non-generic collection, columns silently
  vanish for empty tabs. The `WpfTemplateTests` binding assertions plus the
  manual launch step are what catch this.
- **Exotic property types** (anything `DataGrid` cannot auto-generate an editor
  for) render as read-only text. The console app had the same limit via
  `TypeDescriptor`. Acceptable for a starter scaffold.
- **Deleting the template's `MainWindow.*`** touches files inside the generated
  app folder only, and only immediately after this scaffolder created them. It
  never runs against a pre-existing project whose csproj was already present —
  that path skips `CreateProject` and would skip the deletion too. This is called
  out because "delete a file the user might have edited" deserves a guard, and
  the reuse path is exactly that guard.
- **`dotnet new` / `dotnet build` require the SDK on PATH** and are slow; build
  verification stays a manual smoke step, consistent with prior phases.
- **JSON store concurrency**: single-process, single-user, whole-file read/write,
  no locking. Unchanged, and fine for the scope.

# build app: application customization

Date: 2026-08-12

## Problem

`build app` produces the same application every time. The only choices a user
makes are the target (WPF, WinForms, Console, Web API, Worker, Class Library)
and the persistence backend. Everything else — the window title, the colours,
which optional layers are scaffolded, who the application belongs to — is
fixed by the generator.

Two requirements follow from that:

1. `build app` should be able to produce a *customized* application: branding,
   look and feel, and a choice of which optional pieces get generated.
2. `build app` should take a **company name**.

The company name is one instance of the first requirement, not a separate
feature, so both are designed together.

## Scope

In scope, agreed with the requester:

| Category | Settings |
| --- | --- |
| Branding & metadata | company name |
| Look & feel | light/dark theme, accent colour, window title & header text, app icon |
| Feature toggles | validation layer, logging, sample seed data |

Explicitly **out** of scope:

- **Namespace prefix.** The company name will eventually prefix the root
  namespace (`CodisLtd.Shop`), but that is deferred to its own follow-up. It
  forces `projectName` to split into two concepts — project/folder name versus
  root namespace — and that split propagates to the XAML `x:Class`, the csproj
  `<RootNamespace>`, the WPF/WinForms fixups, the generated test project's
  `using` lines and the SQLite environment-variable name. It is also the only
  item on the list that can break the generated app's compile if a single
  template is missed. Everything in this spec is additive by comparison.
- **Code shape** generally: project/folder naming, output location, and making
  the sibling test project optional.

## Approach

Three were considered.

**A. A settings record threaded through the scaffolder.** One
`AppCustomization` record carried in a `ScaffoldRequest` parameter object, with
`ITargetScaffolder` gaining a customization-aware overload. Widest diff, but
the values are visible in every signature that uses them and each template
stays a pure function of its arguments — the property that makes the existing
Roslyn compile-tests cheap to write.

**B. Customization as scaffolder state.** `new AppScaffolder(customization)`,
templates become instance methods reading fields. Small diff, but a template's
output stops being a function of its arguments. Rejected.

**C. A post-generation branding pass.** Generate as today, then rewrite the
output. Almost no change to the generator, but it cannot express the toggles at
all — you cannot un-generate a validation folder by rewriting it — and
text-patching generated XAML is fragile. Rejected.

**Approach A is chosen.**

## Architecture

### New types

`AppScaffolding/AppCustomization.cs` — an immutable record holding the eight
settings (company, theme, accent, title pattern, icon path, and the three
toggles), with a static
`Default` whose values reproduce today's output exactly. That neutrality is
load-bearing: every existing caller and test keeps passing unchanged, and the
existing piped end-to-end tests are the proof.

`AppScaffolding/ScaffoldRequest.cs` — a parameter object (model, appName,
workspaceRoot, target, persistence, importedData, customization) replacing the
`Scaffold` overload chain, which already stands at four overloads and six
arguments. The existing overloads remain as thin delegating wrappers passing
`AppCustomization.Default`.

`AppScaffolding/ThemePalette.cs` — resolves theme + accent to named colours
(background, surface, foreground, gridline, accent, accent-foreground).

`AppScaffolding/LoggingTemplate.cs` — the new `Logging/AppLog.cs` emitter.

`CodeGenerator/App/InlineCustomization.cs` — parses the inline options off a
`build app` line.

`CodeGenerator/App/CustomizationWizard.cs` — the interactive prompts, reading
and writing through a `TextReader`/`TextWriter` exactly as `BuildAppWizard`
does, so the flow is drivable from a test.

`CodeGenerator/App/CustomizationDefaults.cs` — load and save the remembered
profile.

### Changed types

- `AppScaffolder.Scaffold` gains the `ScaffoldRequest` entry point; the
  validation and seed toggles branch inside it.
- `ITargetScaffolder.FrontEndFiles` gains a customization-aware overload as a
  **default interface method**, mirroring the existing `FixupProject` default:

  ```csharp
  IReadOnlyList<GeneratedFile> FrontEndFiles(
      string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
      => FrontEndFiles(projectName, entityNames);
  ```

  Only `WpfTarget`, `WinFormsTarget`, `ConsoleTarget` and `WebApiTarget`
  override it. `ClassLibraryTarget` and `WorkerTarget` have no UI and no title
  and are untouched.
- `CsprojEditing` gains `InjectProperties`, following the idempotent
  insert-before-`</Project>` pattern of `InjectPackageReference` and
  `InjectSeedContent`.
- `MainWindowTemplate`, `WinFormsMainFormTemplate`, `ConsoleMenuTemplate`,
  `WebApiProgramTemplate`, `RepositoryTemplate`, `ViewModelTemplates`,
  `SeedDataTemplate`, `TestProjectTemplates`, `WpfProjectFixup`.

### Data flow

```
build app: a shop company "Codis Ltd" theme dark from csv orders.csv nologging
   |
   |- BuildAppCommand.Tail          strips "build app:"
   |- InlineCustomization.Split     -> (overrides, remaining text)          NEW
   |- InlineDataSource.Parse        -> description + DataSourceSpec    unchanged
   |- CustomizationWizard.Run       interactive only; merges saved profile  NEW
   |- RunReviewLoop(... customization) -> Generate -> ScaffoldRequest -> AppScaffolder
```

`InlineCustomization.Split` runs **before** `InlineDataSource.Parse`. The
ordering is not incidental: `Parse` treats everything after `from <format>` as
the location, to the end of the line, because Windows paths contain spaces. The
options must be lifted out first or the location would swallow them.

Customization is settled before discovery runs, exactly as the target already
is, and the review loop prints it beside the existing `Target:` line so
`approve` produces nothing the user has not already seen.

## Command syntax

Options are `key value` pairs and bare flags, placed last on the line, after
the data source if there is one.

```
build app: a shop company "Codis Ltd" theme dark accent #0B6E4F
build app: a shop from csv orders.csv company "Codis Ltd" nologging noseed
```

| Token | Effect |
| --- | --- |
| `company <name>` | company name; quote it if it contains spaces |
| `theme light\|dark` | theme for the WPF and WinForms front ends |
| `accent <#rrggbb>` | accent colour |
| `icon <path>` | `.ico` or `.png` |
| `title <pattern>` | window/page title; `{company}` and `{app}` are the only placeholders |
| `novalidation` | omit the Validation layer |
| `logging` | add logging (off by default, so the flag is positive) |
| `noseed` | omit sample seed rows |

Parsing scans right to left and stops at the first token that is not part of a
recognized option — generalizing the rule `InlineDataSource` already uses to
strip a trailing `normalize`.

**Known limitation, accepted.** A description or path whose final words happen
to spell a valid option is misread: `build app: track theme dark` yields
description `track` and `theme=dark`. Quoting the path, or reordering, avoids
it. An explicit `--` separator is the fix if it is ever hit in practice; it is
not worth paying for now. This behaviour is asserted in a test as documented,
not pretended away.

## Wizard

One new step, asked last — after description, application type and data source,
and still before discovery:

```
Customize appearance and features? [y/N]>
```

`n`, blank, or Esc reproduces today's flow exactly. `y` runs the eight prompts, each
pre-filled from the saved profile so Enter accepts the shown value. A blank
answer keeps the default; `c` cancels the whole build, consistent with
`BuildAppWizard.AskForDescription`.

```
Customize appearance and features? [y/N]> y

  Company name [Codis Ltd]>
  Theme [Dark]>
  Accent colour [#0B6E4F]>
  Icon path (none)>
  Window title [{company} - {app}]>
  Validation layer [yes]>
  Logging [no]>
  Sample seed data [yes]> no

Saved as defaults for next time.
```

## Remembered defaults

The profile lives at `.codegen/app-profile.json` under the workspace root,
beside the existing `.codegen/learned.json`, so there is one convention rather
than a second invented here. It is written when the wizard completes and read
only to pre-fill prompts.

Precedence: inline options beat the profile, which beats built-in defaults.

Two deliberate exclusions, both following precedent already in the code:

- **Redirected stdin never reads the profile.** Piped scripts and the E2E tests
  take inline options only. `BuildAppCommand.HandleFromData` already reasons
  this way about `normalize` — a scripted run applies everything or nothing so
  that it stays deterministic — and a profile file silently changing a scripted
  build's output would break that. It also guarantees existing piped tests are
  byte-identical.
- **A missing or corrupt profile yields defaults, never an error**, as
  `AiConfig.Load` does: a broken config must leave the CLI working exactly as it
  does without one.

## What each setting emits

### Branding

| Setting | Where it lands |
| --- | --- |
| Company | `CsprojEditing.InjectProperties` writes `<Company>`, `<Authors>`, `<Copyright>(c) {year} {company}</Copyright>` |
| Title | `MainWindowTemplate.Xaml` `Title=`, `WinFormsMainFormTemplate` `Text=`, a `ConsoleProgramTemplate` banner, and a Web API root endpoint returning the title |
| File header | prepended in `AppScaffolder.WriteSource`, gated to `.cs` |

**Correction, found while planning and confirmed in the code:** this table first
said the title lands in "the Web API OpenAPI title". **The generated Web API has
no OpenAPI document** — `WebApiProgramTemplate.Build` emits
`WebApplication.CreateBuilder(args)` then `builder.Build()` with no
`AddOpenApi()`/`AddSwaggerGen()` anywhere, and adding one would be a new package
and a new surface. What shipped instead is a root endpoint,
`app.MapGet("/", () => "Codis Ltd - Shop");`, emitted only when the title differs
from the bare project name — so an unbranded Web API is byte-identical to
today's. The console banner is gated the same way, and for the same reason: with
the default pattern the title *is* the app name, so an ungated banner would add a
`Console.WriteLine` no unbranded console app has ever had.

The file-header gate matters: `MainWindow.xaml` and the `Data/Seed/*.json` files
are written through the same `WriteSource` method, and a `//` banner would
corrupt both.

The copyright year is passed in as a value, never read from `DateTime.Now`
inside a template. Otherwise every golden-output test in `CodeGenerator.Tests`
turns red on 1 January.

### Look and feel

Theme and accent resolve to a `ThemePalette`. WPF gets a generated
`Views/Theme.xaml` resource dictionary merged into `App.xaml` —
`WpfProjectFixup.RepointStartupUri` already rewrites `App.xaml`, so the merge
goes in there, and `MainWindow.xaml` stays the static, entity-agnostic file it
is today. WinForms sets `BackColor`/`ForeColor` in `WinFormsMainFormTemplate`.
Console and Web API take the title only.

The icon is copied into `Assets/`. `<ApplicationIcon>` is set **only for
`.ico`** — MSBuild rejects a `.png` there — while both formats are usable as the
WPF `Window.Icon` and the WinForms `Form.Icon`.

### Toggles

The two removal toggles are not a simple "skip these files". The generated code
has hard dependencies on both folders:

- `RepositoryTemplate.cs:35` — `private readonly IValidator<T> _validator = ValidatorFactory.Create<T>();`
- `ViewModelTemplates.cs:214` — `var validator = ValidatorFactory.Create<T>();`
- `SeedData.EnsureSeeded()` is called from `ConsoleProgramTemplate:22`,
  `MainViewModelTemplate:26`, `WebApiProgramTemplate:21`,
  `WorkerProgramTemplate:15` and `TestProjectTemplates:171,175`

Dropping the folders without touching those produces an app that scaffolds
cleanly and then fails to compile. Therefore:

- `novalidation` also branches `RepositoryTemplate.Base` and
  `ViewModelTemplates` to omit validation, and suppresses the generated
  `ValidatorTests`.
- `noseed` also drops the four `EnsureSeeded()` call sites and `SeedDataTests`.

**Interaction:** `noseed` with an imported CSV/Excel source still emits
`SeedData.cs`, because that is the mechanism that loads the imported rows.
`noseed` suppresses *sample* rows only. That is what it means when there is no
data source, and the only reading that is not self-contradictory when there is
one.

### Logging

New scaffolding, deliberately small: `Logging/AppLog.cs`, a static
`ILoggerFactory` with the Console and Debug providers; three `PackageReference`
entries injected through the existing idempotent
`CsprojEditing.InjectPackageReference`; log calls in `RepositoryTemplate`
(create, update, delete, validation failures) and `StoreFactory` (which backend
was chosen). No config file, no log-level UI.

## Testing

Emitted-code changes are verified by **compiling the output with
`Compiler.Compile`**, not by substring assertions. A `novalidation` build that
still calls `ValidatorFactory.Create<T>()` passes a `Contains` check happily and
fails to build.

Compile tests cover light and dark themes, `novalidation`, `noseed`, `logging`,
and all three toggles together, for each of the WPF, WinForms, Console and Web
API targets.

Pure-function tests cover `InlineCustomization.Split` — including the
`track theme dark` misread — the profile round-trip, and corrupt-profile falling
back to defaults. The wizard is driven through its `TextReader`/`TextWriter`.

The regression proof is the existing piped end-to-end tests: with
`AppCustomization.Default` they must produce byte-identical output. If any one
of them moves, the default is not neutral and the change is wrong.

Tests live in `CodeGenerator.Tests/App/`, where the scaffolding tests already
are.

## Error handling

Following the habits already in `BuildAppCommand` and `AppScaffolder`: an
unreadable profile, an unwritable icon, or a malformed `#rrggbb` each print one
line and continue with the default. None of them aborts a build the user has
already answered ten questions for.

## Follow-up

Namespace prefix (`CodisLtd.Shop`), specified separately once this lands and is
green.

# Namespace prefix for `build app` — design

**Date:** 2026-08-12
**Branch:** `namespace-prefix`, off `main` at `f6cb89f`
**Follows:** `2026-08-12-build-app-customization-design.md` (PR #78), which deliberately deferred this item

## The requirement

`build app` should be able to put the generated code under a company's own
namespace root — `CodisLtd.Shop.Models` rather than `Shop.Models` — so a
generated project can be dropped into a solution that already has a namespace
convention.

## Scope decision: namespace only, not the project name

The prefix changes **namespaces**. It does not change the project name, the
folder, the assembly name, `ScaffoldResult.ProjectPath`, or the SQLite
connection-string environment override.

The alternative — folding the prefix into the project name so the folder,
assembly and namespaces all become `CodisLtd.Shop` — was considered and
rejected. It is materially less work (`dotnet new wpf -n CodisLtd.Shop` emits a
consistent `x:Class`, `clr-namespace` and code-behind namespace on its own,
verified by probe), but it renames what the user sees on disk and changes the
`SHOP_DB` override to `CODISLTD_SHOP_DB`. Keeping the project identity stable
was chosen instead.

Consequence to accept: `RootNamespace` and `AssemblyName` diverge in the
generated project. That is ordinary MSBuild practice, not a defect.

## What makes this cheap: the templates already take a namespace root

Every emitted `namespace` declaration and every `using` in the templates comes
from the template's `projectName` / `appName` / `namespaceName` argument, and
**no template uses that argument for anything else** — not a type name, not a
label, not a file name. Verified across all 30 declaration sites.

So the prefix needs no new template parameter. `AppScaffolder` passes the *root
namespace* where it passes the project name today, and every emitted file
follows. The project name continues to name the folder, the csproj, the
assembly and the env var.

## The setting

`AppCustomization` gains one member:

```csharp
string NamespacePrefix   // "" by default
```

and one derived accessor:

```csharp
/// <summary>The namespace root emitted code declares: the project name, under the
/// prefix when there is one.</summary>
public string RootNamespaceFor(string projectName)
    => NamespacePrefix.Length == 0 ? projectName : $"{NamespacePrefix}.{projectName}";
```

`AppCustomization.Default` keeps `NamespacePrefix = ""`, so
`RootNamespaceFor(projectName) == projectName` and **every file the scaffolder
writes is byte-for-byte what it writes today**. That is the same invariant PR
#78 was built against and the check any change here must survive.

Note the project name passed in already carries the target's folder suffix, so a
console build's root becomes `CodisLtd.Shop.Console` — the prefix sits above the
whole project identity, not inside it.

## Three reconciliation points

Emitted files declare their own namespace, so they need nothing beyond the
argument change. Three things outside the emitted set do:

| Where | Change | Note |
| --- | --- | --- |
| app `.csproj` | add `<RootNamespace>` | so a file the user adds later lands in the right namespace, and the IDE agrees with the generated code |
| test `.csproj` | add `<RootNamespace>`, set to the app's root namespace plus `.Tests` | matches the namespace the generated test files declare (`CodisLtd.Shop.Console.Tests`), while the test assembly stays `Shop.Console.Tests` |
| `App.xaml` + `App.xaml.cs` (WPF only) | rewrite `x:Class`, `xmlns:local="clr-namespace:…"` and the code-behind `namespace` | these two files come from `dotnet new wpf` and are neither overwritten nor deleted by the existing fixup |

**Correction, found during execution and confirmed in the code:** this section
originally counted three reconciliation points. Execution found a fourth: the
write-once `<Entity>Validator.Custom.cs` stub. Its namespace header — and any
`using` line rooted at the app's old namespace — has to be repointed the same
way, because the generated `.Generated.cs` half of each validator moves under
the new prefix on every run while the hand-written stub, deliberately never
overwritten, stays behind still declaring the old one. `ValidatorTemplate
.ReconcileCustomStubNamespace` does this, matching the SHAPE the same way
`WpfProjectFixup.ReconcileNamespace` does, and it runs from the same place in
`AppScaffolder.Scaffold` as the other three, on every run.

One more consequence worth stating rather than leaving implicit: removing the
prefix removes the `<RootNamespace>` element from the app and test project
files entirely — including one a user added there by hand, since the
reconciler cannot tell the two apart. WPF's reconciler makes the same
trade-off consciously (it too matches the shape, never a remembered value),
and the choice here is the same one for the same reason.

**WPF is the only target that needs the third row.** Probed against every
template: `winforms` emits `Form1.cs`/`Form1.Designer.cs` (deleted by
`WinFormsProjectFixup`) and a `Program.cs` we overwrite; `worker` emits a
`Worker.cs` we overwrite; `classlib` emits `Class1.cs` (deleted by its fixup);
`console` and `webapi` emit top-level `Program.cs` files with no namespace at
all, which we also overwrite.

### Both must run on every scaffold, and update rather than skip

`WpfProjectFixup.Apply` runs only inside the project-creation branch today,
which is correct for what it does — a project the user has since edited is left
alone. The namespace reconciliation cannot live there:

- Re-scaffolding an existing project with a **different** prefix would leave
  `App.xaml`'s `x:Class` naming a namespace no generated file declares any more.
  That is a broken build, not a cosmetic drift.
- The same argument applies to `<RootNamespace>`: an injection with
  skip-if-present semantics (as `CsprojEditing.InjectProperties` uses for
  branding) would leave a stale root namespace behind.

So both are **idempotent updates driven by the current value**, and both run
unconditionally after the files are written — the position and precedent
`WpfProjectFixup.MergeTheme` already established. Neither may assume what the
previous value was: the rewrite matches the *shape* (`x:Class="….App"`,
`clr-namespace:…`, `namespace …;`) rather than a remembered string.

## Validation is stricter here than elsewhere in customization

The rest of customization ignores an unusable value: a malformed accent is
dropped rather than rejected, because a mistyped colour must not fail a build
the user has already answered ten questions for. **A malformed namespace cannot
be treated that way — it breaks the compile.**

`NamespacePrefix.IsValid` requires:

- one or more dot-separated segments, no empty segment (so no leading, trailing
  or doubled dot)
- each segment starts with a letter or `_` and continues with letters, digits or
  `_`
- no segment is a C# keyword

An invalid value is refused rather than sanitised, following the precedent each
surface already sets:

- **inline**: the option is not consumed and the scan stops, so the words stay
  part of the description — exactly what `theme mauve` does today
- **wizard**: the prompt keeps its current value, as a malformed accent does

Collisions need no special handling: `CodisLtd.Shop` for an app called `Shop`,
or a prefix ending in `Data`, are legal C# and resolve correctly because every
emitted `using` is rooted at the full namespace.

## Surfaces

**Inline**, last on the line with the other options:

```
build app: a shop that sells books company "Codis Ltd" namespace "CodisLtd" theme dark
```

The keyword is `namespace`, joining `company` / `theme` / `accent` / `icon` /
`title` in `InlineCustomization`. It inherits the documented limitation those
share: a description whose final words spell an option is read as one.

**Wizard**: a ninth prompt, "Namespace prefix". When no prefix is set yet and a
company has just been named, the prompt offers the company name reduced to an
identifier — every character that cannot appear in one removed, so `Codis Ltd`
becomes `CodisLtd` — the same courtesy as naming a company adopting the company
title pattern. If the reduction is not a valid prefix (a company starting with a
digit, like `3M`, reduces to `3M`, which no namespace may be called), the prompt
offers nothing rather than something that would be refused. Blank keeps what is
shown; `none` clears it.

**Review loop**: `CustomizationSummary` gains a `Namespace prefix: CodisLtd`
line when one is set. The *bare prefix*, deliberately, not the resolved root:
the root ends in the target's folder suffix (`Shop.Console`, `Shop.WinForms`),
and the target can still be changed at `approve`, so a resolved root printed
here could be wrong by the time anything is generated.

**Profile**: `.codegen/app-profile.json` carries `namespacePrefix`, loaded and
saved with the rest.

**Help**: the existing customization catalog entry gains the option; the
`docs/commands.md` regeneration its test demands follows.

## Testing

| Check | Why it is the check |
| --- | --- |
| `RootNamespaceFor` with and without a prefix | the whole mechanism in one function |
| `IsValid` over keywords, empty segments, leading digits, doubled dots | the rejection rule, stated once |
| every default-customization emitted file, byte-for-byte against the previous commit | the neutrality invariant; substring assertions cannot see a lost CR, as PR #78 learned |
| a prefixed scaffold: every emitted namespace under the prefix, `<RootNamespace>` set, `App.xaml`/`App.xaml.cs` agreeing | the reconciliation points, together |
| re-scaffold with a **changed** prefix | the staleness trap above — the one bug this design exists to prevent |
| re-scaffold with the prefix **removed** | the same trap in reverse |
| integration: a prefixed WPF app builds | XAML `x:Class` resolution is a real MSBuild/XAML-compiler behaviour, invisible to any string assertion |
| integration: a prefixed console app builds and its generated test project passes | the generated suite's own usings are rooted at the prefix too |

## Out of scope

- Renaming the project, folder, assembly or env var (the rejected alternative above).
- A per-folder namespace convention (`Models` → `Domain.Models`). The folder
  names are the generator's own layout, not something to parameterise.
- Rewriting an existing project's namespaces beyond the three reconciliation
  points — regenerating over a project the user has edited is already
  documented as overwrite-generated-sources-only.

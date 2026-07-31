# `build app` guided wizard and application type — design

**Date:** 2026-07-31
**Status:** approved, not yet implemented

## Problem

Three things are wrong with `build app` today.

**Bare `build app` asks the wrong question first.** It goes straight to
`DataSourceInterview` — "Do you have existing data? [y/N]" — and answering `n` lands in
`HandleFromDescription` with an empty description, which prints a usage hint and gives up. The
user is interrogated about data and then told they should have typed a description, which was
never asked for.

**It only ever builds WPF apps in practice.** Five targets exist (`wpf`, `console`, `webapi`,
`classlib`, `worker`), but the choice is made at `approve`, after discovery has run and the
model is on screen. `ResolveTarget` prompts there, defaulting to WPF, so a user who wants a
Web API discovers that late or not at all. There is no Windows Forms target at all.

**The guided flow is inconsistent.** Whether questions are asked depends on which form of the
command was typed. `build app: <description>` skips straight to discovery; bare `build app`
gets the data interview only.

## Goal

One guided wizard entered by every interactive `build app`, asking in the order a person
thinks: what are we building, what kind of app, is there data. Plus a Windows Forms target, so
the application-type menu covers the six kinds of app the product should offer.

## Scope

**This branch:** the wizard, the arrow-key selection prompt, the WinForms target, and the
review-loop changes that follow from choosing a target up front.

**Deliberately not this branch:**

- **Piped and redirected input.** The wizard is for a person at a terminal. Redirected stdin
  keeps today's behaviour exactly: `InlineDataSource` parsing, no prompts, target resolved at
  `approve` defaulting to WPF. The existing piped E2E tests must stay green without edits —
  if one needs changing, the boundary has leaked.
- **The VS Code panel.** `engineHost.ts` spawns the engine with piped stdin, so the panel is on
  the redirected side and gets no wizard. This is not a regression: the panel already skips the
  data-source interview for the same reason. Recorded here so it is a known limitation rather
  than a surprise.
- **SQLite for desktop targets.** WinForms inherits WPF's JSON-only restriction, for the
  reason given under "Persistence" below.

## Flow

```
build app  |  build app: <description>  |  build app from csv <path>
   |
   +- stdin redirected --> unchanged: InlineDataSource only, no prompts,
   |                       target resolved at `approve` (default wpf)
   |
   +- interactive --> WIZARD
                        1. description   (skipped if typed inline)
                        2. application type   (arrow-key menu, default WPF)
                        3. data source    (DataSourceInterview, skipped if inline)
                             |
                             +- no data  --> Wikipedia discovery path
                             +- source   --> read schema, normalization check
                             |
                        4. review loop, target already known
```

What the user sees:

```
CodeGenerator >> build app

Describe the application you want to build.
(Enter to skip if you'll point at a data file instead, 'c' to cancel)
> a library lending system

Application type (↑↓ then Enter, Esc to cancel):

  ❯ WPF (Windows desktop)
    Windows Desktop (WinForms)
    ASP.NET Web API
    Console
    Class Library
    Windows Service

Do you have existing data? [y/N]: n

Discovering domain (this may fetch from Wikipedia the first time)...
```

### Step 1 — description

Asked first, always, unless text followed the command. Inline text is accepted silently and the
wizard opens at step 2; typing a description stays a shortcut, not something to re-confirm.

Blank Enter is allowed and means "I will point at data instead" — the data-only path
(`build app from csv <path>` with no description) is a supported flow, and the prompt says so.
`c` or EOF cancels.

Blank description *and* no data is the one case that still ends in the existing usage hint:
there is genuinely nothing to build from.

### Step 2 — application type

An arrow-key menu, default WPF, in this order:

| Menu label               | CLI key    | Target               |
| ------------------------ | ---------- | -------------------- |
| WPF (Windows desktop)    | `wpf`      | `WpfTarget`          |
| Windows Desktop (WinForms) | `winforms` | `WinFormsTarget` (new) |
| ASP.NET Web API          | `webapi`   | `WebApiTarget`       |
| Console                  | `console`  | `ConsoleTarget`      |
| Class Library            | `classlib` | `ClassLibraryTarget` |
| Windows Service          | `worker`   | `WorkerTarget`       |

Labels are display-only. The keys are unchanged, so `approve webapi` keeps working, and the
label-to-key mapping lives in one table beside `TargetScaffolders`.

Esc or EOF cancels.

### Step 3 — data source

`DataSourceInterview` unchanged, skipped when `from <format> <location>` was inline.

### Step 4 — review loop

Three consequences of settling the target before discovery:

- The loop prints the chosen target in its header, so what `approve` will build is visible.
- `approve` no longer prompts for a target; it uses the wizard's. `approve <target>` still
  overrides, keeping existing scripts and tests working.
- `approve sqlite` is read correctly. Today `ResolveTarget` reads `sqlite` as a target key and
  rejects it as unknown; with the target already settled, a lone `json`/`sqlite` token is read
  as persistence. A pre-existing wart, fixed in passing because this change is what exposes it.
  Being read correctly is not the same as being permitted: with a desktop target chosen it now
  reaches `IsPersistenceAllowed` and gets the existing refusal message, which must be widened
  from naming WPF to naming both desktop targets.

Cancelling at any step prints the existing `Cancelled. Nothing was generated.` and generates
nothing.

## Components

### `BuildAppWizard` (new, `CodeGenerator/App/`)

Returns `WizardOutcome(Decision, Description, Target, Source, Normalize)`, where `Decision` is
`Proceed` or `Cancelled` — two cases, not three. `DataSourceInterview`'s own three-way
`InterviewDecision` collapses here: its `NoData` and `Source` both mean proceed, differing only
in whether `Source` is null, which is what `BuildAppCommand` already branches on.

Reads and writes through injected `TextReader`/`TextWriter`, the
pattern `DataSourceInterview` and `BuildAppCommand.ResolveTarget` already established, so the
whole flow is drivable from a test.

`BuildAppCommand.Handle` calls it once at the top when `!Console.IsInputRedirected`, replacing
the inline `DataSourceInterview` call.

### Selection prompt (new, `CodeGenerator/App/`)

Split the way this codebase already splits console work — decidable logic out of the console:

- **`SelectionState`** — pure. Items plus a current index; `Move(ConsoleKey)` handles ↑/↓ with
  clamping at both ends, `Lines()` returns exactly the text to paint. Fully unit-tested,
  touches no console.
- **`SelectionPrompt`** — thin driver: `Console.ReadKey`, hand to `SelectionState`, repaint the
  block in place. Enter commits, Esc cancels. Not unit-tested, as `CommandLineEditor` is not.
- **Numbered fallback** — when `Console.IsOutputRedirected` is true the cursor APIs throw
  ("handle is invalid"), the reason `CommandLineEditor` guards on the same condition. Falls
  back to `Choose [1-6] (default 1):`, which is `TextReader`-driven and therefore testable.

### `WinFormsTarget` (new, `AppScaffolding/Targets/`)

Key `winforms`, `FolderSuffix` `.WinForms`, `DotNetProjectType.WinForms` — which
`DotNetProjectFactory` already maps to `dotnet new winforms`. The plumbing exists; the front
end is what is new.

**It reuses the existing view models rather than growing a second presentation layer.**
`ObservableObject`, `RelayCommand`, `IEntityViewModel`, `EntityViewModel`, `MainViewModel` and
`RelationshipResolver` are listed today in `WpfTarget.FrontEndFiles` and commented as WPF-only,
but nothing in them is: they use `System.ComponentModel` and `System.Windows.Input.ICommand`,
both available to any .NET app. They become a shared set that both desktop targets include.
This is the one refactor the design asks for, and it is the difference between one presentation
layer and two.

**The form** is `Views/MainForm.cs`, laid out in code with no designer file, matching how every
other template here emits plain C#: a `TabControl` with one tab per `IEntityViewModel`, each
holding a `DataGridView` plus Add/Delete/Save buttons bound to the view model's commands, and a
status label. The same shape and behaviours as the WPF window.

**Collection change notification.** `EntityViewModel.Records` is an `ObservableCollection<T>`
and `DataGridView` ignores `INotifyCollectionChanged`. Each grid binds through a
`BindingSource` that calls `ResetBindings` on `CollectionChanged` — a few lines in the
template. The alternative, reshaping the view models around `BindingList<T>`, would disturb the
working WPF app for no gain.

**Columns.** Id read-only, and a foreign key (`int <Nav>Id` whose sibling `<Nav>` is another
entity) rendered as a combo box over that entity's rows. The same two rules
`MainWindowTemplate.OnAutoGeneratingColumn` applies, ported to
`DataGridView.AutoGenerateColumns`.

**Fixup.** `dotnet new winforms` emits `Program.cs`, `Form1.cs`, `Form1.Designer.cs` and
`Form1.resx`. Our own `Program.cs` overwrites the template's, as `ConsoleTarget` and
`WorkerTarget` already do; a `WinFormsProjectFixup` deletes the three `Form1` files, mirroring
`WpfProjectFixup`.

**Persistence.** WinForms keeps WPF's JSON-only restriction. WPF is restricted because its view
models read `JsonStore` directly rather than going through the store seam; since WinForms shares
those view models, routing one through the seam means routing both. Lifting the restriction for
both desktop targets is separate work.

`IsPersistenceAllowed` currently hardcodes the single key `wpf`, so it must be widened to cover
both desktop targets — otherwise `winforms` + `sqlite` is accepted and generates an app whose
view models bypass the store it was told to use.

## Testing

Everything decidable is tested without a console — the point of the split above.

- **`SelectionState`** — ↑/↓ clamp at both ends, default index honoured, `Lines()` marks
  exactly one row.
- **Numbered fallback** — through `TextReader`/`TextWriter`: valid pick, out-of-range re-asks,
  blank takes the default, EOF cancels.
- **`BuildAppWizard`** — the description prompt is asserted to come *before* the data question.
  This is the bug in problem 1, so the test goes red against today's code, before the wizard
  exists.
- Inline description skips step 1; cancel at each of the three steps returns `Cancelled` and
  generates nothing; blank description plus a data source proceeds.
- **`BuildAppCommand`** — the wizard's target reaches `AppScaffolder`; `approve <target>` still
  overrides; `approve sqlite` is read as persistence; `winforms` + `sqlite` is refused, the
  assertion that `IsPersistenceAllowed` was widened rather than left naming WPF alone.
- **Redirected stdin** — the existing piped E2E tests are the guard, green without edits.
- **`WinFormsTarget`** — the generated project is compiled, not string-matched, following
  `GeneratedProjectBuildTests`, under a short output path (long paths trip MSB3030).

Every assertion goes red before it goes green. Verification is the full
`SmartCodeGenerator.slnx` in Release with `-warnaserror`: the CodeGenerator-only Debug build
hides analyzer errors and runs one of the four test projects.

## Sequencing

WinForms first, deliberately — if the wizard shipped first, its menu would offer "Windows
Desktop" against a target that does not exist.

1. `WinFormsTarget`, the shared view-model extraction, and its compile test. `approve winforms`
   works.
2. `SelectionState`, `SelectionPrompt`, numbered fallback.
3. `BuildAppWizard` wired into `BuildAppCommand`; review loop shows the target; `approve
   sqlite` reads as persistence.
4. Docs: the `CommandDictionary` help entry for `build app`, `docs/commands.md`,
   `CodeGenerator/README.md`, `AppScaffolding/README.md`.

# Multi-target app generation — design

**Date:** 2026-07-18
**Status:** Approved (design), pending implementation plan

## Problem

`build app: <description>` discovers a domain model and, on `approve`, always
scaffolds a **WPF CRUD desktop app** over that model. There is no way to emit any
other kind of project. Users want to generate the same data-centric app as other
**app kinds / platforms** — a console tool, a Web API, a class library, or a
background service — without disturbing the existing WPF path.

## Scope

**In scope:** selectable output *target* for the existing domain-model → app
pipeline. The entity/domain-model core is unchanged; only the emitted project
type, entry point, and front-end vary.

**Out of scope:** non-CRUD "functional" utilities (calculators, converters,
etc.); changes to domain discovery, the NLE, or the review-loop editing commands
(`add`/`remove`/`rename`/`field`/`unfield`).

**Targets:** WPF (existing, default), Console, Web API, Class library,
Background service.

## Key constraint: do not affect existing functionality

- The WPF path must remain **byte-for-byte unchanged**. Existing WPF-generating
  code moves behind a `WpfTarget` strategy verbatim; it is not rewritten.
- Existing **piped / redirected** CLI usage must behave exactly as today. Scripts
  and tests that pipe `build app: ...` then `approve` currently produce a WPF app
  and consume exactly one line for `approve`. The new prompt must not consume an
  extra line or change that outcome (see Selection UX).

## Architecture — target-strategy interface

Chosen over (B) a big `switch` in `Scaffold` and (C) one standalone scaffolder
per target. (A) is the only option that keeps the WPF path untouched *and*
isolates each target as a small, independently testable unit.

```
AppScaffolder.Scaffold(model, appName, workspaceRoot, target)
  │
  ├─ maps model → ClassDefinitions                       (shared, unchanged)
  ├─ writes Models/<Entity>.cs                            (shared, unchanged)
  ├─ writes Data/{JsonStore,RelationshipResolver,         (shared, unchanged;
  │         Repository,<Entity>Repository}.cs              already UI-agnostic)
  └─ delegates to ITargetScaffolder for:
        • DotNetProjectType (wpf/console/webapi/classlib/worker)
        • entry point + front-end files
```

### `ITargetScaffolder`

```csharp
public interface ITargetScaffolder
{
    /// Stable key used in the CLI prompt and folder suffix (e.g. "console").
    string Key { get; }

    /// The .NET project type this target creates.
    DotNetProjectType ProjectType { get; }

    /// Folder-name suffix appended after appName (empty for WPF, ".Console" etc.).
    string FolderSuffix { get; }

    /// Target-specific files to write, given the app name and entity names.
    /// Shared Models/ and Data/ are written by AppScaffolder before this runs.
    IReadOnlyList<GeneratedFile> FrontEndFiles(string appName, IReadOnlyList<string> entityNames);

    /// Optional per-target csproj fixup (e.g. WpfProjectFixup). No-op by default.
    void FixupProject(string outputFolder);
}
```

`GeneratedFile` is a small record `(string RelativeFolder, string Name, string Source)`.
`AppScaffolder` owns the shared-core writing and the write-and-count bookkeeping
(so the `expected`/`written`/`allWritten` logic stays in one place); the strategy
only supplies files and project metadata.

### Target registry

A small lookup (`TargetScaffolders.ByKey`) maps a key → `ITargetScaffolder`.
`BuildAppCommand` uses it to validate the user's choice and to default to `wpf`.
Unknown keys are rejected with the list of valid keys.

## The targets

| Key        | ProjectType   | Folder suffix | Front-end / entry point |
|------------|---------------|---------------|--------------------------|
| `wpf`      | Wpf           | *(none)*      | Existing ViewModels/ + Views/ (unchanged, default) |
| `console`  | Console       | `.Console`    | `Program.cs`: menu loop (list/add/edit/delete per entity) over typed repositories |
| `webapi`   | WebApi        | `.Api`        | `Program.cs`: minimal API mapping CRUD endpoints per entity (`GET/POST/PUT/DELETE`) |
| `classlib` | ClassLibrary  | `.Lib`        | *(none)* — Models + Data only |
| `worker`   | WorkerService | `.Worker`     | `Worker.cs : BackgroundService` that on startup exercises the repositories (seed a sample row, log counts) |

Notes:
- **Console** and **Web API** both drive the shared typed repositories + `JsonStore`,
  so their CRUD behaves identically to the WPF app's data layer.
- **Class library** writes no entry point; `ITargetScaffolder.FrontEndFiles`
  returns empty and `ProjectType = ClassLibrary`.
- **Background service** is the weakest conceptual fit: a worker is not naturally
  CRUD. It is an honest **demonstration harness** that proves the headless core
  runs — it seeds/logs via the repositories. Kept per user request; if it proves
  awkward it can be cut without affecting the other targets.

## Selection UX — prompt on approve (backward-compatible)

On `approve` (or `a`) in the review loop:

1. If the user typed `approve <target>`, use that target directly (explicit,
   non-interactive friendly).
2. Else if input is **interactive** (`Console.IsInputRedirected == false`),
   prompt:
   `Target [wpf/console/webapi/classlib/worker] (default wpf): `
   Blank / Enter ⇒ `wpf`. An unrecognized entry re-prompts.
3. Else (input **redirected / piped**): **do not prompt.** Default to `wpf`.
   This guarantees piped scripts consume no extra line and produce exactly the
   same result as today.

Invalid inline target (`approve nonsense`) ⇒ message listing valid keys; the
model is not scaffolded and the loop continues.

## Output folders

- WPF: `<workspaceRoot>/<AppName>` — unchanged.
- Others: `<workspaceRoot>/<AppName><FolderSuffix>` (e.g. `MyApp.Console`).

The suffix keeps a non-WPF target from clobbering a WPF app of the same name and
lets a user generate several targets from one model side by side.

## Error handling

- Reuses the existing best-effort write model in `AppScaffolder`: a file that
  cannot be written is skipped; `ScaffoldResult` reports partial success with a
  count (`Generated N of M file(s)…`).
- Empty model on approve: unchanged (`Nothing to generate — the model is empty.`).
- Project-creation failure: unchanged (`Project creation failed: …`).
- `ScaffoldResult.Message` names the target
  (`Generated 3 class(es) + a runnable Console CRUD app.`).

## Testing

- **Unit test per target scaffolder**: asserts the expected file set and key
  content markers (e.g. Console `Program.cs` contains a menu entry per entity;
  Web API `Program.cs` maps an endpoint group per entity; classlib emits no
  entry point).
- **Shared-core test**: Models/ and Data/ are written identically regardless of
  target (guards the "unchanged core" claim).
- **Selection test**: piped `approve` ⇒ wpf and consumes one line; `approve
  console` ⇒ console; unknown key ⇒ error, no scaffold.
- **Integration (compile) test**: `dotnet build` each generated console / webapi
  / classlib / worker output and assert success (all cross-platform; WPF build
  is already exercised elsewhere and is Windows-only).

## Backward compatibility summary

- WPF code path and output: unchanged.
- Piped/redirected `approve`: unchanged (defaults to wpf, no extra line).
- Review-loop editing commands: unchanged.
- New surface is purely additive: one interface, four new target strategies, a
  registry, and the target parameter threaded through `Scaffold`.

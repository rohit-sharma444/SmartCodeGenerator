# VS Code Project Sync + prompt-free repackage — design

Date: 2026-07-23
Branch: `feat/vscode-project-sync`

Two fixes, both delivered through the VS Code extension.

## Fix #1 — the packaged extension is stale (packaging, not code)

### Problem
Reported: "the prompt is not free while the Wikipedia store updates" — observed in the
**installed `.vsix`** panel.

### Root cause (evidenced, not assumed)
The source on `main` already keeps the prompt free. Driving the real `EngineHost`
(`out/engineHost.js`) against the CLI exactly as the extension does:

- input becomes **enabled at 4.14s**, the moment `CodeGenerator >>` reaches stdout;
- the store update (`Wikipedia store updating 50%…54%`) arrives **entirely on stderr**
  and never flips the state back — the input stays enabled throughout.

In a piped CLI run, stdout carries only banner + command output + prompt; all
store-update text is on stderr. Commit `36768ef` ("keep the prompt free during the
Wikipedia store update") did this.

`smartcodegen-vscode.vsix` was built at **15:43**, one minute *before* `36768ef`
(15:44). `out/` and `*.vsix` are git-ignored build artifacts, so the packaged
extension the user installed predates the fix.

### Resolution
No lock-related code change. Rebuild the `.vsix` from current source
(`npm run compile && npm run package`) — which also bundles Fix #2 — and reinstall
(`code --install-extension smartcodegen-vscode.vsix --force`). Verify the rebuilt
package carries the proven-free state machine and the stderr `info`-styling from
`36768ef`.

## Fix #2 — Project Sync: auto-load & live-reflect generated projects

### Requirement
When a project is created by the tool it should be loaded in VS Code (open folder +
key files), and changes to the project should reflect in VS Code as each change
happens (auto-open/refresh each change).

### Constraints discovered
- Generated projects are subfolders of the open workspace: `AppScaffolder` writes
  under `workspaceRoot`, which the extension sets to `PROJECTPATH` = the open VS Code
  folder. So the project folder is already *in* the workspace — no `openFolder` needed
  (and `openFolder` would reload the window and kill the running CLI).
- There are **18+ scattered write sites** across `CodeGenerationEngine` (`FileWriter`,
  `ClassMemberEditor`, `ClassPropertyService`, `ControllerGenerator`,
  `RepositoryGenerator`, `AppScaffolder.WriteSource`, …). Instrumenting each in the CLI
  to emit path events would be invasive and easy to leave incomplete.
- The extension already distinguishes **a command running** (`EngineHost` state
  `busy`, set on `send()`) from **idle** (`ready`, set when the prompt returns). That
  is exactly the signal that separates *tool writes* from *the user's own edits*.

### Approach (chosen): extension-side FileSystemWatcher, gated by engine state
No C# / CLI changes. A new module `vscode-extension/src/projectSync.ts`:

- Owns a `vscode.FileSystemWatcher` over the workspace for
  `**/*.{cs,csproj,xaml,json}`, excluding `**/bin/**`, `**/obj/**`, `**/.git/**`,
  `**/node_modules/**`, `**/Dump/**`, `**/TestResults/**`, `**/.codegen/**`,
  `**/.vs/**`, and history/dotfiles.
- Subscribes to `host.onStateChange`. **Only filesystem events that occur while the
  engine is `busy` are acted on.** Idle-time events are the user's own edits — VS Code
  already reflects those, and we must not steal focus on a save.
- Buffers create/change events from the moment a command starts (`busy`) until it
  finishes (`ready`) plus a short grace window (≈400 ms) to absorb watcher lag, then
  flushes and classifies:
  - **A new `.csproj` appeared** → a project was created: `revealInExplorer` on its
    folder and open its key files — the `.csproj`, the entry point
    (`Program.cs`, else WPF `MainWindow.xaml` / `App.xaml.cs`), and up to a couple of
    `Models/*.cs` — capped at 5 tabs total, newest focused.
  - **Otherwise** → open/focus the created/changed source files (cap 5, focus newest),
    so a regeneration such as "add property … to Customer" surfaces immediately.
- Files already open are revealed/focused, not reopened.
- Rejected alternatives:
  - *Per-write CLI event channel (sentinel JSON on stdout, parsed by `EngineHost`).*
    Precise, but requires threading events through 18+ write sites — high blast radius
    on the generation engine and CI-critical C#, and easy to miss paths. Not worth it
    when the state-gated watcher already tells tool-writes from user-edits.
  - *Naive watcher with no state gate.* Simpler, but fires on the user's own saves and
    steals focus; cannot tell tool writes from user writes.

### Components
- `vscode-extension/src/projectSync.ts` — new. Two parts:
  - A **pure core** (`ProjectSyncCore`) holding the buffer + classification + cap
    logic, depending only on an injected `EditorOps` interface
    (`reveal(path)`, `open(path, focus)`) and an injected clock/flush trigger. No
    `vscode` import — unit-testable under `node --test`.
  - A **thin adapter** that constructs the `FileSystemWatcher`, maps
    `host.onStateChange` to `core.commandStarted()/commandFinished()`, and implements
    `EditorOps` with `vscode.window.showTextDocument` /
    `vscode.commands.executeCommand('revealInExplorer', uri)`.
- `vscode-extension/src/extension.ts` — construct `ProjectSync` with the host +
  workspace path; push to `context.subscriptions`; dispose on deactivate.
- `vscode-extension/package.json` — add setting `smartcodegen.autoOpenGenerated`
  (boolean, default `true`).

### Data flow
`command sent` → host `busy` → `core.commandStarted()` → watcher events buffered →
prompt returns → host `ready` → grace window → `core.commandFinished()` → classify →
`EditorOps.reveal` / `EditorOps.open`.

### Error handling
- No workspace folder (untitled window): watcher is not created; `ProjectSync` no-ops.
- `autoOpenGenerated=false`: buffer discarded on flush, nothing opened.
- Opening a file that fails (deleted between event and flush): swallow per-file, keep
  the rest — best-effort, exactly like the scaffolder's own writes.
- Cap prevents a large scaffold from opening dozens of tabs.

### Testing
- `vscode-extension/test/projectSync.test.ts` (`node --test`), against `ProjectSyncCore`
  with a fake `EditorOps` and manual flush:
  - new `.csproj` in a burst → reveal folder once + open csproj + entry + models,
    capped at 5;
  - single `.cs` change during `busy` → that file opened/focused;
  - change while `ready` (idle) → nothing opened;
  - excluded paths (`bin/`, `obj/`, `Dump/`) → ignored;
  - already-buffered duplicate paths de-duped;
  - `autoOpenGenerated=false` → nothing opened.
- Manual verification: rebuild `.vsix`, install, run `build app: …` in the panel,
  confirm the new project reveals and key files open; run an edit command, confirm the
  changed file surfaces; confirm the prompt stays free during a store update.

### Out of scope
- Multi-root / `openFolder` semantics (deliberately avoided — see Constraints).
- Any change to the C# generation engine or the CLI.
- Restructuring the 18 write sites.

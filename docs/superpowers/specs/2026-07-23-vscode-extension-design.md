# SmartCodeGenerator VS Code Extension — Design Spec

**Date:** 2026-07-23
**Status:** Draft for review
**Scope:** A VS Code extension that acts as an in-editor front-end for the existing `CodeGenerator` CLI, as a developer tool used against this repository.

## 1. Goal & non-goals

### Goal
Let a developer drive the existing natural-language `CodeGenerator` REPL from inside VS Code through an interactive, REPL-like panel — typing commands such as `create class Customer with properties Name string` or `build app: a library that lends books to members` and seeing responses in a transcript, with the multi-turn `build app:` review loop working naturally and all session state preserved. Generated files land in the open workspace and appear in the Explorer.

### Non-goals (v1)
The following are explicitly deferred, not designed here:
- Clickable file links from transcript output.
- Syntax highlighting / autocomplete / IntelliSense for the command grammar.
- Buttons for the `build app:` review-loop actions (approve/add/remove/…). The loop is driven by typing, same as the terminal.
- Bundling a published, self-contained engine binary for use outside this repo.
- Marketplace publishing / packaging pipeline.

### Assumptions
- The extension runs against **this repository** as a developer tool. The user has the .NET SDK installed and the SmartCodeGenerator source present.
- The engine is launched via `dotnet run --project CodeGenerator`; v1 hardcodes this (a configurable engine path is a later enhancement).

## 2. Architecture overview

A TypeScript VS Code extension living in a new `vscode-extension/` folder at the **repository root** (`D:\SmartCodeGenerator-Repository\vscode-extension\`), a sibling to `CodeGenerator/` and outside the .NET solution (`SmartCodeGenerator.slnx`).

Two collaborating pieces:

- **Webview panel** (`SmartCodeGen` view) — an input box plus a scrolling append-only transcript, rendering only; all logic lives in the host.
- **Engine host** — a module in the extension host process that owns the child engine process. On first panel open it spawns the CLI **once** via `dotnet run --project CodeGenerator` and keeps it alive. Typed commands are written to the child's **stdin**; the child's **stdout/stderr** stream back into the transcript. Because it is a single long-lived process, session state (`use project X`, the `build app:` review loop) is preserved with no extra work.

Generated files are written to disk by the engine into the workspace (the host sets `PROJECTPATH` to the workspace root), so they appear in the Explorer through normal file-watching — no extra wiring.

```
┌─────────────────────────────────────────────┐
│ VS Code                                       │
│                                               │
│  ┌───────────────┐   postMessage  ┌────────┐ │
│  │ Webview panel │ <────────────> │  Ext   │ │
│  │ (transcript + │                │  host  │ │
│  │  input box)   │                │        │ │
│  └───────────────┘                └───┬────┘ │
│                                        │ stdin/stdout/stderr
└────────────────────────────────────────┼─────┘
                                          v
                            dotnet run --project CodeGenerator
                            (long-lived child process)
                                          │ writes files
                                          v
                              workspace ($PROJECTPATH) ── Explorer
```

## 3. Components

### 3.1 Engine host (`src/engineHost.ts`)
The core logic. Responsibilities:
- **Spawn**: launch `dotnet run --project <repoRoot>/CodeGenerator` lazily on first panel open. Working directory = workspace root; environment inherits the parent plus `PROJECTPATH = <workspace root>`.
- **Send**: `send(text: string)` writes `text + "\n"` to the child's stdin and transitions state to `busy`.
- **Receive**: subscribe to stdout and stderr; forward chunks to listeners tagged by stream (`stdout` / `stderr`).
- **Idle detection**: recognize when the engine is awaiting input again and emit a `ready` event (see §5).
- **Lifecycle**: expose `state` (`starting` | `ready` | `busy` | `stopped`), a `restart()` that kills and re-spawns, and `dispose()` for deactivation. On unexpected child exit, transition to `stopped`, surface it in the transcript, and offer restart.

Interface (indicative):
```ts
interface EngineHost {
  readonly state: EngineState;
  onOutput(cb: (chunk: string, stream: 'stdout' | 'stderr') => void): Disposable;
  onStateChange(cb: (state: EngineState) => void): Disposable;
  send(text: string): void;
  restart(): void;
  dispose(): void;
}
```

### 3.2 Webview panel (`src/panelView.ts` + `media/`)
- Registered as a **Webview View** in the **bottom panel** container (REPL-shaped, next to the integrated terminal). An activity-bar container is an acceptable alternative if the bottom panel proves awkward.
- Renders: transcript (append-only, auto-scroll), input box (single line, Enter submits, up/down history), and a toolbar (Restart engine, Clear transcript, status dot reflecting engine state).
- Pure rendering: it posts user input to the host and appends whatever the host sends. No engine knowledge.
- Transcript styling distinguishes three kinds of line: user command, engine stdout, engine stderr/error.

### 3.3 Extension entry (`src/extension.ts`)
- `activate()`: register the webview view provider and commands (`smartcodegen.restartEngine`, `smartcodegen.clearTranscript`, `smartcodegen.focusInput`). Wire the host's output/state events to the panel and the panel's submit events to the host.
- `deactivate()`: dispose the host (kill the child process).

## 4. Message flow

Webview ↔ extension host communicate via VS Code's `postMessage`:

1. User presses Enter → webview posts `{ type: 'submit', text }`.
2. Host calls `engine.send(text)`; state → `busy`; posts `{ type: 'state', state: 'busy' }` so the input disables with a "working…" affordance.
3. Child stdout/stderr chunks arrive → host posts `{ type: 'output', chunk, stream }` → webview appends.
4. Host detects idle (§5) → state → `ready` → posts `{ type: 'state', state: 'ready' }` → webview re-enables input.

The `build app:` review loop needs no special handling — it is simply further prompts and further typed replies over the same channel.

## 5. The primary technical risk (de-risk first)

The whole long-lived-process design rests on one behavior that must be proven before building on it: **can the CLI's stdin be held open and fed commands one at a time, reading output between each?**

Two specific unknowns:
- **End-of-input semantics.** `CodeGenerator/README.md` states piped input "runs in order and stops at end-of-input." With a pipe held open, EOF never arrives, so the read loop *should* keep consuming lines. But if its redirected-mode path treats input as batch-and-exit after the first read, the long-lived model breaks.
- **Output buffering & prompt emission.** When stdout is redirected, .NET may buffer it, and the interactive `CodeGenerator >>` prompt may or may not be emitted under redirection. Idle detection depends on observing that prompt.

Prior project notes establish that piped stdin works and that the CLI's `CommandLineEditor` falls back to `ReadLine` when input is redirected — which is encouraging but does not by itself confirm multi-command interactive feeding with an open pipe.

**Mitigation — plan step 1 is a throwaway spike:** a tiny Node script spawns the CLI, writes two commands separated by a delay, and confirms (a) both execute and (b) we can tell where one command's output ends and the next begins. Outcomes:
- **Spike passes** → the design stands unchanged; idle detection watches for the `CodeGenerator >>` prompt marker (and the review-loop prompt) in the stream, and that transition is the `ready` signal. If output proves buffered, force line-buffered/`AutoFlush` reads on the Node side or an environment/console flush on the engine side.
- **Spike fails** → the spec's fallback applies: a minimal, opt-in engine-side change that emits a known sentinel line after each command completes (giving reliable framing), or, as a last resort, one process per command (which sacrifices in-memory session state and is undesirable). The fallback choice is made in the plan after the spike, based on what actually failed.

## 6. Testing strategy

- **Engine host unit tests** — the bulk of the logic. Drive the host against a **fake engine process**: a small stub script (Node or a shell) that echoes known markers on stdin lines, so tests exercise spawn, stdin write, output framing, idle detection, and restart with no dependency on a real .NET build. Cover: a command produces expected transcript output; state transitions busy→ready; stderr is tagged separately; unexpected child exit → `stopped`; restart re-spawns cleanly.
- **One integration test** — drives the *real* CLI through the host for a couple of commands (e.g. `create class Foo` then `show class Foo`), asserting the second command's output reflects the first. Gated so it can be skipped where the .NET SDK/build is unavailable.
- **Webview UI** — kept thin (rendering only) and verified manually.

## 7. Repository layout

```
D:\SmartCodeGenerator-Repository\
├── CodeGenerator\               (existing engine)
├── vscode-extension\            (new)
│   ├── package.json             (extension manifest: views, commands, activation)
│   ├── tsconfig.json
│   ├── src\
│   │   ├── extension.ts
│   │   ├── engineHost.ts
│   │   └── panelView.ts
│   ├── media\                   (webview HTML/CSS/JS)
│   └── test\                    (host unit + integration tests, fake-engine stub)
└── ...
```

The extension has its own `package.json`/npm toolchain, independent of the .NET solution. It is not added to `SmartCodeGenerator.slnx`.

## 8. Open questions / later enhancements
- Configurable `smartcodegen.enginePath` and a way to point at a published binary (enables non-repo use).
- Clickable generated-file links in the transcript.
- Command-grammar syntax highlighting / autocomplete.
- Review-loop action buttons.
- Marketplace packaging.

## 9. Success criteria (v1)
1. Opening the SmartCodeGen panel starts the engine and shows a ready prompt.
2. Typing a command (e.g. `create console project Demo`) runs it, shows the engine's output in the transcript, and the created files appear in the Explorer.
3. Session state persists across commands (`use project Demo` then `create class X` targets Demo).
4. The `build app:` review loop is usable end to end by typing replies.
5. Restarting the engine from the toolbar works; an engine crash is surfaced and recoverable.
6. Engine-host unit tests (against the fake engine) and the one real-CLI integration test pass.

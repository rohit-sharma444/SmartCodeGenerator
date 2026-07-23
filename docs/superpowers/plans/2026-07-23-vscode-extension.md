# SmartCodeGenerator VS Code Extension Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A VS Code extension with a REPL-like panel that drives the existing `CodeGenerator` CLI as a single long-lived child process, run as a developer tool against this repository.

**Architecture:** A TypeScript extension in a new `vscode-extension/` folder at the repo root. An `EngineHost` module (plain Node, no `vscode` dependency) spawns `dotnet run --project CodeGenerator` once and talks to it over stdin/stdout/stderr; a Webview panel renders a transcript + input box and exchanges `postMessage`s with the extension host. Because the engine is one persistent process, CLI session state (`use project X`, the `build app:` review loop) is preserved for free.

**Tech Stack:** TypeScript, Node.js (`node:child_process`), VS Code Extension API (Webview View), `tsc` for the extension build, `tsx` + `node:test` for unit tests (no VS Code instance needed because `EngineHost` is decoupled from `vscode`).

## Global Constraints

- Extension lives at `D:\SmartCodeGenerator-Repository\vscode-extension\` — repo root, sibling to `CodeGenerator/`, NOT added to `SmartCodeGenerator.slnx`.
- `EngineHost` MUST NOT import the `vscode` module (keeps it unit-testable outside a VS Code instance).
- Engine launch is hardcoded in v1: command `dotnet`, args `['run', '--project', 'CodeGenerator']`, cwd = workspace root, env inherits parent plus `PROJECTPATH` = workspace root.
- Extension compiles to CommonJS (`"module": "commonjs"`, `"moduleResolution": "node"`); use extensionless relative imports (`./engineHost`).
- Ready/idle detection default marker: `/CodeGenerator >>\s*$/` — CONFIRM or ADJUST via Task 1's spike before relying on it against the real CLI.
- Frequent commits: every task ends with a commit. TDD: test first, watch it fail, implement, watch it pass.
- Toolchain present and assumed: Node v24, npm v11, dotnet 10.

---

### Task 1: De-risk the long-lived stdin/stdout model (spike)

This is a throwaway investigation, not production code. It exists because the entire design assumes the CLI will accept commands one at a time over a stdin pipe we hold open, and emit a detectable idle marker. Prove that before building anything.

**Files:**
- Create (throwaway): `vscode-extension/spike/probe.mjs`

**Interfaces:**
- Consumes: nothing.
- Produces: a recorded decision — (a) whether interactive multi-command feeding works, and (b) the exact idle marker string the real CLI emits under redirection. Both feed the `readyMarker` default and Task 5.

- [ ] **Step 1: Write the probe script**

```js
// vscode-extension/spike/probe.mjs
// Run from the repo root: node vscode-extension/spike/probe.mjs
import { spawn } from 'node:child_process';

const child = spawn('dotnet', ['run', '--project', 'CodeGenerator'], {
  cwd: process.cwd(),
  env: { ...process.env, PROJECTPATH: process.cwd() + '/.spike-workspace' },
});

child.stdout.setEncoding('utf8');
child.stderr.setEncoding('utf8');
child.stdout.on('data', (d) => process.stdout.write('[OUT] ' + JSON.stringify(d) + '\n'));
child.stderr.on('data', (d) => process.stdout.write('[ERR] ' + JSON.stringify(d) + '\n'));
child.on('exit', (code) => process.stdout.write(`[EXIT ${code}]\n`));

// Feed two commands with a gap; if the second runs, an open pipe keeps the REPL alive.
setTimeout(() => child.stdin.write('create console project SpikeProj\n'), 8000);
setTimeout(() => child.stdin.write('list projects\n'), 12000);
setTimeout(() => { child.stdin.write('exit\n'); }, 16000);
```

- [ ] **Step 2: Run it and observe**

Run: `node vscode-extension/spike/probe.mjs` (from `D:\SmartCodeGenerator-Repository`)
Expected observations to record:
- Do BOTH commands execute (does `list projects` show `SpikeProj`)? → confirms interactive feeding over an open pipe.
- What exact bytes precede each idle wait? Copy the literal prompt string from the `[OUT]` lines (e.g. `"CodeGenerator >> "`). This is the `readyMarker`.
- Is output line-buffered/flushed promptly, or does it arrive only at exit (buffering problem)?

- [ ] **Step 3: Decide and record**

Write findings as a short note in the plan's task or a scratch file. Decision gate:
- **PASS** (both commands ran, prompt observed): keep the design; set `readyMarker` default to the observed prompt regex. Proceed to Task 2.
- **FAIL — batch-and-exit** (only the first command ran, or the process exited early): the fallback is a minimal engine-side change emitting a sentinel line after each command; note this for a follow-up and set `readyMarker` to that sentinel. Still proceed to Task 2 (the host design is unchanged; only the marker source differs).
- **FAIL — output buffered until exit:** plan to read with `child.stdout` in flowing mode (already the case) and, if needed, add a small engine-side `Console.Out.Flush()`/`AutoFlush` note as a follow-up.

- [ ] **Step 4: Clean up the spike artifacts**

Run: `rm -rf vscode-extension/spike .spike-workspace SpikeProj`
The probe was disposable; nothing from it ships. (The `vscode-extension/` folder proper is created fresh in Task 2.)

- [ ] **Step 5: Commit the recorded decision**

Record the decision in the commit message so it is not lost.

```bash
git add -A
git commit -m "chore: spike results for VS Code extension engine host (interactive stdin, ready marker)"
```

---

### Task 2: Scaffold the extension project and test toolchain

**Files:**
- Create: `vscode-extension/package.json`
- Create: `vscode-extension/tsconfig.json`
- Create: `vscode-extension/.gitignore`
- Create: `vscode-extension/test/smoke.test.ts`

**Interfaces:**
- Consumes: Task 1's go decision.
- Produces: a working `npm run compile` and `npm test` in `vscode-extension/`; proves `tsx` + `node:test` execute TypeScript tests.

- [ ] **Step 1: Write `package.json`**

```json
{
  "name": "smartcodegen-vscode",
  "displayName": "SmartCodeGen",
  "description": "Drive the SmartCodeGenerator CLI from an in-editor REPL panel.",
  "version": "0.0.1",
  "publisher": "smartcodegen-dev",
  "private": true,
  "engines": { "vscode": "^1.85.0" },
  "main": "./out/extension.js",
  "activationEvents": ["onView:smartcodegen.panel"],
  "contributes": {
    "viewsContainers": {
      "panel": [
        { "id": "smartcodegen", "title": "SmartCodeGen", "icon": "media/icon.svg" }
      ]
    },
    "views": {
      "smartcodegen": [
        { "id": "smartcodegen.panel", "name": "SmartCodeGen", "type": "webview" }
      ]
    },
    "commands": [
      { "command": "smartcodegen.restartEngine", "title": "SmartCodeGen: Restart Engine" },
      { "command": "smartcodegen.clearTranscript", "title": "SmartCodeGen: Clear Transcript" },
      { "command": "smartcodegen.focusInput", "title": "SmartCodeGen: Focus Input" }
    ]
  },
  "scripts": {
    "compile": "tsc -p ./",
    "watch": "tsc -watch -p ./",
    "test": "node --import tsx --test test/*.test.ts"
  },
  "devDependencies": {
    "@types/node": "^24.0.0",
    "@types/vscode": "^1.85.0",
    "tsx": "^4.19.0",
    "typescript": "^5.6.0"
  }
}
```

- [ ] **Step 2: Write `tsconfig.json`**

```json
{
  "compilerOptions": {
    "module": "commonjs",
    "moduleResolution": "node",
    "target": "ES2022",
    "lib": ["ES2022"],
    "outDir": "out",
    "rootDir": ".",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "sourceMap": true
  },
  "include": ["src/**/*.ts"],
  "exclude": ["node_modules", "test", ".vscode-test"]
}
```

- [ ] **Step 3: Write `.gitignore`**

```
node_modules/
out/
*.vsix
.vscode-test/
```

- [ ] **Step 4: Write a smoke test**

```ts
// vscode-extension/test/smoke.test.ts
import { test } from 'node:test';
import assert from 'node:assert';

test('toolchain runs TypeScript tests', () => {
  assert.equal(1 + 1, 2);
});
```

- [ ] **Step 5: Install and run the smoke test**

Run (from `vscode-extension/`): `npm install && npm test`
Expected: test run passes, `1 tests` / `0 fail`.

- [ ] **Step 6: Verify compile works**

Run (from `vscode-extension/`): `npm run compile`
Expected: exits 0 (no `src/` files yet is fine — `tsc` succeeds with an empty include match). If `tsc` errors on no inputs, that resolves once Task 3 adds `src/`; acceptable to defer this check to Task 3.

- [ ] **Step 7: Commit**

```bash
git add vscode-extension/package.json vscode-extension/tsconfig.json vscode-extension/.gitignore vscode-extension/test/smoke.test.ts vscode-extension/package-lock.json
git commit -m "chore: scaffold vscode-extension project and test toolchain"
```

---

### Task 3: EngineHost — spawn, output streaming, idle detection

**Files:**
- Create: `vscode-extension/src/engineHost.ts`
- Create: `vscode-extension/test/fakeEngine.mjs`
- Create: `vscode-extension/test/engineHost.test.ts`

**Interfaces:**
- Consumes: Task 2 toolchain.
- Produces:
  - `type EngineState = 'starting' | 'ready' | 'busy' | 'stopped'`
  - `interface Disposable { dispose(): void }`
  - `interface EngineHostOptions { command: string; args: string[]; cwd: string; env?: NodeJS.ProcessEnv; readyMarker?: RegExp }`
  - `class EngineHost` with: `get state(): EngineState`, `onOutput(cb: (chunk: string, stream: 'stdout'|'stderr') => void): Disposable`, `onStateChange(cb: (state: EngineState) => void): Disposable`, `start(): void`, `send(text: string): void`, `restart(): void`, `dispose(): void`.

- [ ] **Step 1: Write the fake engine stub**

```js
// vscode-extension/test/fakeEngine.mjs
// Emits an initial prompt, then for each stdin line echoes a result and re-prompts.
process.stdout.write('CodeGenerator >> ');
let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (d) => {
  buf += d;
  let i;
  while ((i = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, i).trim();
    buf = buf.slice(i + 1);
    handle(line);
  }
});
function handle(line) {
  if (line === 'boom') {
    process.stderr.write('error: boom\n');
  } else if (line === 'crash') {
    process.exit(1);
    return;
  } else {
    process.stdout.write(`ran: ${line}\n`);
  }
  process.stdout.write('CodeGenerator >> ');
}
```

- [ ] **Step 2: Write the failing tests (happy path + idle detection)**

```ts
// vscode-extension/test/engineHost.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import { EngineHost, EngineState } from '../src/engineHost';

function makeHost() {
  return new EngineHost({
    command: process.execPath,
    args: ['test/fakeEngine.mjs'],
    cwd: process.cwd(),
    readyMarker: /CodeGenerator >>\s*$/,
  });
}

function waitForState(host: EngineHost, target: EngineState): Promise<void> {
  return new Promise((resolve) => {
    if (host.state === target) return resolve();
    const d = host.onStateChange((s) => {
      if (s === target) { d.dispose(); resolve(); }
    });
  });
}

function collect(host: EngineHost) {
  const chunks: { c: string; stream: 'stdout' | 'stderr' }[] = [];
  host.onOutput((c, stream) => chunks.push({ c, stream }));
  return chunks;
}

test('reaches ready after the startup prompt', async () => {
  const host = makeHost();
  host.start();
  await waitForState(host, 'ready');
  assert.equal(host.state, 'ready');
  host.dispose();
});

test('send runs a command and returns to ready', async () => {
  const host = makeHost();
  const out = collect(host);
  host.start();
  await waitForState(host, 'ready');
  host.send('create class Foo');
  assert.equal(host.state, 'busy');
  await waitForState(host, 'ready');
  const text = out.filter((o) => o.stream === 'stdout').map((o) => o.c).join('');
  assert.match(text, /ran: create class Foo/);
  host.dispose();
});
```

- [ ] **Step 3: Run to verify failure**

Run (from `vscode-extension/`): `npm test`
Expected: FAIL — cannot find module `../src/engineHost`.

- [ ] **Step 4: Implement `EngineHost`**

```ts
// vscode-extension/src/engineHost.ts
import { spawn, ChildProcess } from 'node:child_process';

export type EngineState = 'starting' | 'ready' | 'busy' | 'stopped';

export interface Disposable {
  dispose(): void;
}

export interface EngineHostOptions {
  command: string;
  args: string[];
  cwd: string;
  env?: NodeJS.ProcessEnv;
  readyMarker?: RegExp;
}

type OutputListener = (chunk: string, stream: 'stdout' | 'stderr') => void;
type StateListener = (state: EngineState) => void;

export class EngineHost {
  private child: ChildProcess | undefined;
  private _state: EngineState = 'stopped';
  private readonly outputListeners = new Set<OutputListener>();
  private readonly stateListeners = new Set<StateListener>();
  private readonly readyMarker: RegExp;
  private tail = '';

  constructor(private readonly opts: EngineHostOptions) {
    this.readyMarker = opts.readyMarker ?? /CodeGenerator >>\s*$/;
  }

  get state(): EngineState {
    return this._state;
  }

  onOutput(cb: OutputListener): Disposable {
    this.outputListeners.add(cb);
    return { dispose: () => this.outputListeners.delete(cb) };
  }

  onStateChange(cb: StateListener): Disposable {
    this.stateListeners.add(cb);
    return { dispose: () => this.stateListeners.delete(cb) };
  }

  start(): void {
    if (this.child) return;
    this.setState('starting');
    this.tail = '';
    const child = spawn(this.opts.command, this.opts.args, {
      cwd: this.opts.cwd,
      env: this.opts.env ?? process.env,
    });
    this.child = child;
    child.stdout!.setEncoding('utf8');
    child.stderr!.setEncoding('utf8');
    child.stdout!.on('data', (d: string) => this.handleOutput(d, 'stdout'));
    child.stderr!.on('data', (d: string) => this.handleOutput(d, 'stderr'));
    child.on('exit', () => {
      if (this.child === child) {
        this.child = undefined;
        this.setState('stopped');
      }
    });
  }

  send(text: string): void {
    if (!this.child) throw new Error('engine not running');
    this.setState('busy');
    this.child.stdin!.write(text + '\n');
  }

  restart(): void {
    this.dispose();
    this.start();
  }

  dispose(): void {
    const child = this.child;
    this.child = undefined;
    if (child) child.kill();
    this.setState('stopped');
  }

  private handleOutput(chunk: string, stream: 'stdout' | 'stderr'): void {
    for (const cb of this.outputListeners) cb(chunk, stream);
    if (stream === 'stdout') {
      this.tail = (this.tail + chunk).slice(-200);
      if (this.readyMarker.test(this.tail)) {
        this.tail = '';
        this.setState('ready');
      }
    }
  }

  private setState(s: EngineState): void {
    if (this._state === s) return;
    this._state = s;
    for (const cb of this.stateListeners) cb(s);
  }
}
```

- [ ] **Step 5: Run to verify pass**

Run (from `vscode-extension/`): `npm test`
Expected: PASS — both new tests plus the smoke test.

- [ ] **Step 6: Verify compile picks up `src/`**

Run (from `vscode-extension/`): `npm run compile`
Expected: exits 0, produces `out/engineHost.js`.

- [ ] **Step 7: Commit**

```bash
git add vscode-extension/src/engineHost.ts vscode-extension/test/fakeEngine.mjs vscode-extension/test/engineHost.test.ts
git commit -m "feat: EngineHost spawn, output streaming and idle detection"
```

---

### Task 4: EngineHost — stderr tagging, crash-to-stopped, restart

**Files:**
- Modify: `vscode-extension/test/engineHost.test.ts` (append tests)

**Interfaces:**
- Consumes: `EngineHost` from Task 3 (unchanged signatures).
- Produces: verified lifecycle behavior — no new public API.

- [ ] **Step 1: Write the failing lifecycle tests**

Append to `vscode-extension/test/engineHost.test.ts`:

```ts
test('stderr is tagged separately from stdout', async () => {
  const host = makeHost();
  const out = collect(host);
  host.start();
  await waitForState(host, 'ready');
  host.send('boom');
  await waitForState(host, 'ready');
  const err = out.filter((o) => o.stream === 'stderr').map((o) => o.c).join('');
  assert.match(err, /error: boom/);
  host.dispose();
});

test('an unexpected child exit transitions to stopped', async () => {
  const host = makeHost();
  host.start();
  await waitForState(host, 'ready');
  host.send('crash');
  await waitForState(host, 'stopped');
  assert.equal(host.state, 'stopped');
});

test('restart re-spawns and reaches ready again', async () => {
  const host = makeHost();
  host.start();
  await waitForState(host, 'ready');
  host.restart();
  await waitForState(host, 'ready');
  assert.equal(host.state, 'ready');
  host.dispose();
});
```

- [ ] **Step 2: Run to verify they pass (behavior already implemented in Task 3)**

Run (from `vscode-extension/`): `npm test`
Expected: PASS for all three. If `restart` or `crash` fails, the bug is in Task 3's exit-guard or `dispose` ordering — fix `engineHost.ts` so the old child's `exit` handler no-ops after `this.child` is reassigned (the `if (this.child === child)` guard), then re-run.

- [ ] **Step 3: Commit**

```bash
git add vscode-extension/test/engineHost.test.ts vscode-extension/src/engineHost.ts
git commit -m "test: EngineHost stderr tagging, crash-to-stopped, restart"
```

---

### Task 5: Real-CLI integration test (gated)

**Files:**
- Create: `vscode-extension/test/engineHost.integration.test.ts`

**Interfaces:**
- Consumes: `EngineHost` from Task 3; the real `CodeGenerator` project at repo root; the `readyMarker` confirmed in Task 1.
- Produces: one end-to-end proof that a real command's effect is visible to a later command through the same host.

- [ ] **Step 1: Write the gated integration test**

Use the marker confirmed by Task 1's spike. If the spike found a different prompt, substitute it in `readyMarker` below.

```ts
// vscode-extension/test/engineHost.integration.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import * as os from 'node:os';
import * as path from 'node:path';
import * as fs from 'node:fs';
import { EngineHost, EngineState } from '../src/engineHost';

const RUN = !!process.env.RUN_ENGINE_INTEGRATION;
const repoRoot = path.resolve(process.cwd(), '..'); // vscode-extension/ -> repo root

function waitForState(host: EngineHost, target: EngineState, ms = 120000): Promise<void> {
  return new Promise((resolve, reject) => {
    if (host.state === target) return resolve();
    const timer = setTimeout(() => reject(new Error(`timeout waiting for ${target}`)), ms);
    const d = host.onStateChange((s) => {
      if (s === target) { clearTimeout(timer); d.dispose(); resolve(); }
    });
  });
}

test('real CLI: a class created by one command is visible to the next', { skip: !RUN }, async () => {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'scg-e2e-'));
  const out: string[] = [];
  const host = new EngineHost({
    command: 'dotnet',
    args: ['run', '--project', 'CodeGenerator'],
    cwd: repoRoot,
    env: { ...process.env, PROJECTPATH: workspace },
    readyMarker: /CodeGenerator >>\s*$/,
  });
  host.onOutput((c, stream) => { if (stream === 'stdout') out.push(c); });

  host.start();
  await waitForState(host, 'ready');
  host.send('create class IntegrationFoo');
  await waitForState(host, 'ready');
  host.send('show class IntegrationFoo');
  await waitForState(host, 'ready');
  host.dispose();

  assert.match(out.join(''), /class IntegrationFoo/);
});
```

- [ ] **Step 2: Run it (gated on)**

Run (from `vscode-extension/`): `RUN_ENGINE_INTEGRATION=1 npm test`
Expected: PASS. First run compiles the .NET project, so allow the 120s timeout. If it times out waiting for the first `ready`, the `readyMarker` is wrong — return to Task 1's finding and correct it.

- [ ] **Step 3: Run it (gated off, default)**

Run (from `vscode-extension/`): `npm test`
Expected: the integration test is skipped; unit tests pass.

- [ ] **Step 4: Commit**

```bash
git add vscode-extension/test/engineHost.integration.test.ts
git commit -m "test: gated real-CLI integration test through EngineHost"
```

---

### Task 6: Webview panel and message protocol

**Files:**
- Create: `vscode-extension/src/protocol.ts`
- Create: `vscode-extension/src/panelView.ts`
- Create: `vscode-extension/media/main.css`
- Create: `vscode-extension/media/main.js`
- Create: `vscode-extension/media/icon.svg`

**Interfaces:**
- Consumes: `EngineState` from `./engineHost`.
- Produces:
  - `type InboundMessage = { type: 'submit'; text: string } | { type: 'ready' }`
  - `type OutboundMessage = { type: 'output'; chunk: string; stream: 'stdout' | 'stderr' } | { type: 'state'; state: EngineState } | { type: 'system'; text: string }`
  - `class SmartCodeGenPanel implements vscode.WebviewViewProvider` with static `viewType = 'smartcodegen.panel'`, a constructor `(extensionUri: vscode.Uri, handlers: { onSubmit: (text: string) => void })`, and methods `post(msg: OutboundMessage): void`, `clear(): void`, `focusInput(): void`.

- [ ] **Step 1: Write the message protocol**

```ts
// vscode-extension/src/protocol.ts
import { EngineState } from './engineHost';

export type InboundMessage =
  | { type: 'submit'; text: string }
  | { type: 'ready' };

export type OutboundMessage =
  | { type: 'output'; chunk: string; stream: 'stdout' | 'stderr' }
  | { type: 'state'; state: EngineState }
  | { type: 'system'; text: string };
```

- [ ] **Step 2: Write the webview provider**

```ts
// vscode-extension/src/panelView.ts
import * as vscode from 'vscode';
import { InboundMessage, OutboundMessage } from './protocol';

export class SmartCodeGenPanel implements vscode.WebviewViewProvider {
  public static readonly viewType = 'smartcodegen.panel';
  private view: vscode.WebviewView | undefined;

  constructor(
    private readonly extensionUri: vscode.Uri,
    private readonly handlers: { onSubmit: (text: string) => void }
  ) {}

  resolveWebviewView(view: vscode.WebviewView): void {
    this.view = view;
    view.webview.options = {
      enableScripts: true,
      localResourceRoots: [vscode.Uri.joinPath(this.extensionUri, 'media')],
    };
    view.webview.html = this.getHtml(view.webview);
    view.webview.onDidReceiveMessage((msg: InboundMessage) => {
      if (msg.type === 'submit') this.handlers.onSubmit(msg.text);
    });
  }

  post(msg: OutboundMessage): void {
    this.view?.webview.postMessage(msg);
  }

  clear(): void {
    this.post({ type: 'system', text: '__clear__' });
  }

  focusInput(): void {
    this.view?.show?.(true);
    this.post({ type: 'system', text: '__focus__' });
  }

  private getHtml(webview: vscode.Webview): string {
    const nonce = getNonce();
    const cssUri = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'media', 'main.css'));
    const jsUri = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'media', 'main.js'));
    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy"
    content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';" />
  <link href="${cssUri}" rel="stylesheet" />
</head>
<body>
  <div id="transcript" role="log" aria-live="polite"></div>
  <form id="inputform">
    <input id="input" type="text" autocomplete="off" placeholder="Type a command…" />
  </form>
  <script nonce="${nonce}" src="${jsUri}"></script>
</body>
</html>`;
  }
}

function getNonce(): string {
  let text = '';
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  for (let i = 0; i < 32; i++) text += chars.charAt(Math.floor(Math.random() * chars.length));
  return text;
}
```

- [ ] **Step 3: Write the webview client script**

```js
// vscode-extension/media/main.js
const vscode = acquireVsCodeApi();
const transcript = document.getElementById('transcript');
const form = document.getElementById('inputform');
const input = document.getElementById('input');
const history = [];
let histIndex = -1;

function append(text, cls) {
  const line = document.createElement('div');
  line.className = 'line ' + cls;
  line.textContent = text;
  transcript.appendChild(line);
  transcript.scrollTop = transcript.scrollHeight;
}

form.addEventListener('submit', (e) => {
  e.preventDefault();
  const text = input.value;
  if (text.trim() === '') return;
  append('> ' + text, 'cmd');
  history.push(text);
  histIndex = history.length;
  vscode.postMessage({ type: 'submit', text });
  input.value = '';
});

input.addEventListener('keydown', (e) => {
  if (e.key === 'ArrowUp' && histIndex > 0) {
    histIndex--; input.value = history[histIndex];
    e.preventDefault();
  } else if (e.key === 'ArrowDown' && histIndex < history.length - 1) {
    histIndex++; input.value = history[histIndex];
    e.preventDefault();
  }
});

window.addEventListener('message', (event) => {
  const msg = event.data;
  if (msg.type === 'output') {
    append(msg.chunk.replace(/\s+$/, ''), msg.stream === 'stderr' ? 'err' : 'out');
  } else if (msg.type === 'state') {
    input.disabled = msg.state === 'busy' || msg.state === 'starting' || msg.state === 'stopped';
    input.placeholder = msg.state === 'busy' ? 'working…'
      : msg.state === 'stopped' ? 'engine stopped'
      : 'Type a command…';
  } else if (msg.type === 'system') {
    if (msg.text === '__clear__') transcript.replaceChildren();
    else if (msg.text === '__focus__') input.focus();
    else append(msg.text, 'sys');
  }
});

vscode.postMessage({ type: 'ready' });
```

- [ ] **Step 4: Write the stylesheet**

```css
/* vscode-extension/media/main.css */
body {
  display: flex; flex-direction: column; height: 100vh; margin: 0;
  font-family: var(--vscode-editor-font-family, monospace);
  font-size: var(--vscode-editor-font-size, 13px);
  color: var(--vscode-foreground);
}
#transcript { flex: 1; overflow-y: auto; padding: 8px; white-space: pre-wrap; }
.line { padding: 1px 0; }
.line.cmd { color: var(--vscode-textLink-foreground); }
.line.out { color: var(--vscode-foreground); }
.line.err { color: var(--vscode-errorForeground); }
.line.sys { color: var(--vscode-descriptionForeground); font-style: italic; }
#inputform { border-top: 1px solid var(--vscode-panel-border); padding: 4px; }
#input {
  width: 100%; box-sizing: border-box; padding: 4px;
  background: var(--vscode-input-background); color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
}
#input:disabled { opacity: 0.6; }
```

- [ ] **Step 5: Write the panel icon**

```svg
<!-- vscode-extension/media/icon.svg -->
<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3">
  <path d="M4 4l3 4-3 4" stroke-linecap="round" stroke-linejoin="round"/>
  <line x1="8.5" y1="12" x2="12" y2="12" stroke-linecap="round"/>
</svg>
```

- [ ] **Step 6: Verify it compiles**

Run (from `vscode-extension/`): `npm run compile`
Expected: exits 0, produces `out/protocol.js` and `out/panelView.js`. (`media/` is not compiled by `tsc` — it is shipped as-is.)

- [ ] **Step 7: Commit**

```bash
git add vscode-extension/src/protocol.ts vscode-extension/src/panelView.ts vscode-extension/media/
git commit -m "feat: webview panel and host<->webview message protocol"
```

---

### Task 7: Extension entry — register panel, wire host, add commands

**Files:**
- Create: `vscode-extension/src/extension.ts`
- Create: `vscode-extension/.vscode/launch.json`
- Create: `vscode-extension/README.md`

**Interfaces:**
- Consumes: `EngineHost` (Task 3), `SmartCodeGenPanel` (Task 6), `OutboundMessage` (Task 6).
- Produces: `activate(context)` / `deactivate()`; a runnable extension (F5 in the extension folder).

- [ ] **Step 1: Write the extension entry point**

```ts
// vscode-extension/src/extension.ts
import * as vscode from 'vscode';
import { EngineHost } from './engineHost';
import { SmartCodeGenPanel } from './panelView';

let host: EngineHost | undefined;

export function activate(context: vscode.ExtensionContext): void {
  const workspace = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? process.cwd();

  host = new EngineHost({
    command: 'dotnet',
    args: ['run', '--project', 'CodeGenerator'],
    cwd: workspace,
    env: { ...process.env, PROJECTPATH: workspace },
  });

  const panel = new SmartCodeGenPanel(context.extensionUri, {
    onSubmit: (text) => {
      if (host!.state === 'stopped') {
        panel.post({ type: 'system', text: 'Engine is stopped. Run "SmartCodeGen: Restart Engine".' });
        return;
      }
      host!.send(text);
    },
  });

  host.onOutput((chunk, stream) => panel.post({ type: 'output', chunk, stream }));
  host.onStateChange((state) => {
    panel.post({ type: 'state', state });
    if (state === 'stopped') panel.post({ type: 'system', text: 'Engine stopped.' });
  });

  context.subscriptions.push(
    vscode.window.registerWebviewViewProvider(SmartCodeGenPanel.viewType, panel, {
      webviewOptions: { retainContextWhenHidden: true },
    }),
    vscode.commands.registerCommand('smartcodegen.restartEngine', () => {
      panel.post({ type: 'system', text: 'Restarting engine…' });
      host!.restart();
    }),
    vscode.commands.registerCommand('smartcodegen.clearTranscript', () => panel.clear()),
    vscode.commands.registerCommand('smartcodegen.focusInput', () => panel.focusInput()),
    { dispose: () => host?.dispose() }
  );

  // Start the engine once the panel is first resolved by the user opening the view.
  host.start();
}

export function deactivate(): void {
  host?.dispose();
  host = undefined;
}
```

- [ ] **Step 2: Write the debug launch config**

```json
// vscode-extension/.vscode/launch.json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Run Extension",
      "type": "extensionHost",
      "request": "launch",
      "args": ["--extensionDevelopmentPath=${workspaceFolder}"],
      "outFiles": ["${workspaceFolder}/out/**/*.js"],
      "preLaunchTask": "npm: compile"
    }
  ]
}
```

- [ ] **Step 3: Write the extension README**

```markdown
# SmartCodeGen VS Code Extension

An in-editor REPL panel that drives the SmartCodeGenerator `CodeGenerator` CLI.
Developer tool: run it with the SmartCodeGenerator repo open as the workspace.

## Develop / run

```bash
cd vscode-extension
npm install
npm run compile
```

Then press **F5** (uses `.vscode/launch.json`) to open an Extension Development
Host. Open the **SmartCodeGen** view in the bottom panel and type commands, e.g.
`create console project Demo`, then `use project Demo`, then `create class Item`.

Requires the .NET SDK on PATH; the engine is launched as
`dotnet run --project CodeGenerator` with `PROJECTPATH` set to the workspace root,
so generated files appear in the Explorer.

## Test

```bash
npm test                          # unit tests (fake engine)
RUN_ENGINE_INTEGRATION=1 npm test # also runs the real-CLI integration test
```
```

- [ ] **Step 4: Compile**

Run (from `vscode-extension/`): `npm run compile`
Expected: exits 0, produces `out/extension.js`.

- [ ] **Step 5: Manual end-to-end verification**

1. Open `D:\SmartCodeGenerator-Repository` in VS Code.
2. Press F5 from the `vscode-extension` folder → Extension Development Host opens.
3. Open the **SmartCodeGen** panel (bottom panel area). The status/input should move from disabled ("starting") to enabled once the engine prints its prompt.
4. Type `create console project Demo` → transcript shows engine output; a `Demo/` project appears in the Explorer.
5. Type `use project Demo` then `create class Item` → confirm the class file lands under `Demo/`.
6. Type `build app: a library that lends books to members` → the review-loop prompt appears; type `cancel` → returns to the normal prompt (proves multi-turn state).
7. Run command **SmartCodeGen: Restart Engine** → transcript shows restart, input returns to ready.

Record PASS/FAIL for each. Any failure at step 3 that looks like the input never enabling points back to `readyMarker` (Task 1).

- [ ] **Step 6: Commit**

```bash
git add vscode-extension/src/extension.ts vscode-extension/.vscode/launch.json vscode-extension/README.md
git commit -m "feat: extension entry wiring panel, engine host and commands"
```

---

## Self-Review

**Spec coverage:**
- §2 architecture (TS ext, webview + engine host, single long-lived process) → Tasks 2, 3, 6, 7. ✓
- §3.1 EngineHost (spawn, send, receive, idle detection, lifecycle) → Tasks 3, 4. ✓
- §3.2 Webview panel (bottom panel, transcript, input, history, toolbar/status) → Task 6 + status via `state` messages; toolbar Restart/Clear via commands in Task 7. ✓
- §3.3 extension entry (activate/deactivate, commands, wiring) → Task 7. ✓
- §4 message flow (submit/output/state) → `protocol.ts` Task 6, wiring Task 7. ✓
- §5 primary risk + spike → Task 1 (decision gate) feeds `readyMarker` used in Tasks 3/5/7. ✓
- §6 testing (host unit tests w/ fake engine; one gated real-CLI integration; webview manual) → Tasks 3, 4, 5; manual in Task 7. ✓
- §7 repo layout → Task 2 file structure + Global Constraints. ✓
- §9 success criteria 1–6 → criteria 1–5 verified manually in Task 7 step 5; criterion 6 (tests pass) in Tasks 3–5. ✓

**Placeholder scan:** No TBD/TODO/"handle edge cases"; every code step shows complete code. The only deliberate branch-on-outcome is Task 1's decision gate, which is a spike by design and lists concrete outcomes. ✓

**Type consistency:** `EngineState`, `EngineHost`, `EngineHostOptions`, `Disposable`, `onOutput`/`onStateChange`/`send`/`start`/`restart`/`dispose` are identical across Tasks 3, 4, 5, 7. `InboundMessage`/`OutboundMessage` identical across Tasks 6, 7. `SmartCodeGenPanel.viewType = 'smartcodegen.panel'` matches the view id in `package.json` (Task 2) and the provider registration (Task 7). `readyMarker` default regex identical in Tasks 3 and 5. ✓

# VS Code Project Sync + prompt-free repackage — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the tool creates a project, open/reveal it in VS Code and surface each subsequent generated change automatically — and ship it in a rebuilt `.vsix` that also carries the already-landed prompt-free fix.

**Architecture:** Extension-only. A new vscode-free core (`ProjectSyncCore`) buffers workspace filesystem events that occur *while a command runs* (engine `busy`→`ready`) and, on completion, reveals a newly created project and opens its key files, or opens changed source files. A thin vscode adapter (`ProjectSync`) owns a `FileSystemWatcher` and maps `EngineHost` state to the core. No C# / CLI changes.

**Tech Stack:** TypeScript (strict, CommonJS, ES2022), VS Code Extension API, `node:test` + `tsx` for unit tests, `@vscode/vsce` for packaging.

## Global Constraints

- Language/target: TypeScript `strict`, `module: commonjs`, `target: ES2022` (per `vscode-extension/tsconfig.json`). Do not change compiler options.
- The core module MUST NOT import `vscode` — tests run it under `tsx` with no vscode runtime. All vscode access lives in the adapter.
- Tests use `node:test` + `node:assert`, import source from `../src/...`, and run via `npm test` (`node --import tsx --test test/*.test.ts`). Match the style in `test/engineHost.test.ts`.
- No changes to any C# project, to the CLI, or to the store-update code. Fix #1 is packaging only.
- `out/` and `*.vsix` are git-ignored build artifacts — never `git add` them.
- Max tabs opened per command: 5. Grace window after `ready`: 400 ms.
- End every commit message with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.
- Run all git/npm commands from `vscode-extension/` unless stated otherwise.

---

### Task 1: `ProjectSyncCore` — buffering, classification, cap (vscode-free)

**Files:**
- Create: `vscode-extension/src/projectSyncCore.ts`
- Test: `vscode-extension/test/projectSync.test.ts`

**Interfaces:**
- Consumes: nothing (pure).
- Produces:
  - `interface EditorOps { revealFolder(path: string): void; openFile(path: string, focus: boolean): void; }`
  - `interface ProjectSyncOptions { enabled: boolean; maxTabs: number; }`
  - `class ProjectSyncCore { constructor(ops: EditorOps, opts: ProjectSyncOptions); commandStarted(): void; fileEvent(rawPath: string): void; commandFinished(): void; }`

- [ ] **Step 1: Write the failing tests**

Create `vscode-extension/test/projectSync.test.ts`:

```ts
// vscode-extension/test/projectSync.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import { EditorOps, ProjectSyncCore } from '../src/projectSyncCore';

class FakeOps implements EditorOps {
  revealed: string[] = [];
  opened: { path: string; focus: boolean }[] = [];
  revealFolder(path: string): void { this.revealed.push(path); }
  openFile(path: string, focus: boolean): void { this.opened.push({ path, focus }); }
}

function core(ops: EditorOps, enabled = true) {
  return new ProjectSyncCore(ops, { enabled, maxTabs: 5 });
}

test('new project: reveals the folder once and opens csproj + entry + models, capped', () => {
  const ops = new FakeOps();
  const c = core(ops);
  const dir = 'C:/ws/ShopWeb';
  c.commandStarted();
  for (const f of [
    `${dir}/ShopWeb.csproj`,
    `${dir}/Program.cs`,
    `${dir}/Models/Customer.cs`,
    `${dir}/Models/Order.cs`,
    `${dir}/Data/JsonStore.cs`,
    `${dir}/Data/Repository.cs`,
  ]) c.fileEvent(f);
  c.commandFinished();

  assert.deepEqual(ops.revealed, [dir]);
  const openedPaths = ops.opened.map((o) => o.path);
  assert.ok(openedPaths.includes(`${dir}/ShopWeb.csproj`));
  assert.ok(openedPaths.includes(`${dir}/Program.cs`));
  assert.ok(openedPaths.includes(`${dir}/Models/Customer.cs`));
  // Data/*.cs are not "key" files — not opened.
  assert.ok(!openedPaths.includes(`${dir}/Data/JsonStore.cs`));
  assert.ok(ops.opened.length <= 5);
  // The entry point is the focused tab.
  assert.deepEqual(ops.opened.filter((o) => o.focus).map((o) => o.path), [`${dir}/Program.cs`]);
});

test('single file change during a command is opened and focused', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.commandStarted();
  c.fileEvent('C:/ws/ShopWeb/Models/Customer.cs');
  c.commandFinished();
  assert.deepEqual(ops.revealed, []);
  assert.deepEqual(ops.opened, [{ path: 'C:/ws/ShopWeb/Models/Customer.cs', focus: true }]);
});

test('changes while idle (no command running) are ignored', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.fileEvent('C:/ws/ShopWeb/Models/Customer.cs'); // no commandStarted()
  c.commandFinished();
  assert.deepEqual(ops.opened, []);
  assert.deepEqual(ops.revealed, []);
});

test('build outputs, the store, and non-source files are ignored', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.commandStarted();
  c.fileEvent('C:/ws/ShopWeb/obj/Debug/Generated.cs');   // excluded segment: obj
  c.fileEvent('C:/ws/ShopWeb/bin/Debug/ShopWeb.dll');     // excluded segment + not openable
  c.fileEvent('C:/ws/Dump/enwiki-full.db');               // excluded segment: Dump
  c.fileEvent('C:/ws/ShopWeb/Models/Customer.cs');        // the only real change
  c.commandFinished();
  assert.deepEqual(ops.opened.map((o) => o.path), ['C:/ws/ShopWeb/Models/Customer.cs']);
});

test('duplicate and backslash paths are de-duplicated to one', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.commandStarted();
  c.fileEvent('C:/ws/App/Models/Customer.cs');
  c.fileEvent('C:\\ws\\App\\Models\\Customer.cs'); // same file, windows slashes
  c.commandFinished();
  assert.equal(ops.opened.length, 1);
  assert.equal(ops.opened[0].path, 'C:/ws/App/Models/Customer.cs');
});

test('autoOpenGenerated=false opens and reveals nothing', () => {
  const ops = new FakeOps();
  const c = core(ops, false);
  c.commandStarted();
  c.fileEvent('C:/ws/App/App.csproj');
  c.fileEvent('C:/ws/App/Program.cs');
  c.commandFinished();
  assert.deepEqual(ops.opened, []);
  assert.deepEqual(ops.revealed, []);
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `vscode-extension/`): `npm test`
Expected: FAIL — `Cannot find module '../src/projectSyncCore'` (file not created yet).

- [ ] **Step 3: Write the implementation**

Create `vscode-extension/src/projectSyncCore.ts`:

```ts
// vscode-extension/src/projectSyncCore.ts

/**
 * How the core asks the editor to surface files. Implemented by the vscode adapter,
 * faked in tests — so this module never imports `vscode` and unit-tests without it.
 */
export interface EditorOps {
  revealFolder(path: string): void;
  openFile(path: string, focus: boolean): void;
}

export interface ProjectSyncOptions {
  enabled: boolean;
  maxTabs: number;
}

const EXCLUDED_SEGMENTS = new Set([
  'bin', 'obj', '.git', 'node_modules', 'Dump', 'TestResults', '.codegen', '.vs',
]);

const OPENABLE_EXTENSIONS = ['.cs', '.csproj', '.xaml', '.json'];

// Lower-case base names, in priority order, of a project's natural "start here" file.
const ENTRY_FILE_NAMES = ['program.cs', 'mainwindow.xaml', 'app.xaml.cs'];

function normalize(path: string): string {
  return path.replace(/\\/g, '/');
}

function isExcluded(path: string): boolean {
  return path.split('/').some((segment) => EXCLUDED_SEGMENTS.has(segment));
}

function isOpenable(path: string): boolean {
  const lower = path.toLowerCase();
  return OPENABLE_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

function dirOf(path: string): string {
  const i = path.lastIndexOf('/');
  return i < 0 ? path : path.slice(0, i);
}

function baseName(path: string): string {
  const i = path.lastIndexOf('/');
  return i < 0 ? path : path.slice(i + 1);
}

interface KeySelection {
  files: string[];
  primary: string;
}

/** The csproj, the entry point, then the model classes — deduped and capped. */
function selectKeyFiles(projectDir: string, paths: string[], maxTabs: number): KeySelection {
  const inProject = paths.filter((p) => p === projectDir || p.startsWith(projectDir + '/'));

  const csproj = inProject.find((p) => p.toLowerCase().endsWith('.csproj'));
  const entry = ENTRY_FILE_NAMES
    .map((name) => inProject.find((p) => baseName(p).toLowerCase() === name))
    .find((p): p is string => p !== undefined);
  const models = inProject.filter(
    (p) => p.includes('/Models/') && p.toLowerCase().endsWith('.cs'),
  );

  const ordered: string[] = [];
  const add = (p: string | undefined): void => {
    if (p !== undefined && !ordered.includes(p)) ordered.push(p);
  };
  add(csproj);
  add(entry);
  for (const model of models) add(model);

  const files = ordered.slice(0, maxTabs);
  const primary = entry ?? csproj ?? files[0];
  return { files, primary };
}

/**
 * Buffers workspace file changes seen while a command runs and, when it finishes, opens
 * a newly created project's key files (revealing its folder) or the changed source files.
 * State-gated by design: events seen while idle are the user's own edits and are ignored.
 */
export class ProjectSyncCore {
  private buffering = false;
  private readonly buffer: string[] = [];

  constructor(
    private readonly ops: EditorOps,
    private readonly opts: ProjectSyncOptions,
  ) {}

  commandStarted(): void {
    this.buffering = true;
    this.buffer.length = 0;
  }

  fileEvent(rawPath: string): void {
    if (!this.buffering) return;
    const path = normalize(rawPath);
    if (isExcluded(path) || !isOpenable(path)) return;
    if (!this.buffer.includes(path)) this.buffer.push(path);
  }

  commandFinished(): void {
    this.buffering = false;
    const paths = this.buffer.slice();
    this.buffer.length = 0;
    if (!this.opts.enabled || paths.length === 0) return;
    this.act(paths);
  }

  private act(paths: string[]): void {
    const csproj = paths.find((p) => p.toLowerCase().endsWith('.csproj'));
    if (csproj !== undefined) {
      const projectDir = dirOf(csproj);
      this.ops.revealFolder(projectDir);
      const { files, primary } = selectKeyFiles(projectDir, paths, this.opts.maxTabs);
      this.openInOrder(files, primary);
    } else {
      const files = paths.slice(0, this.opts.maxTabs);
      this.openInOrder(files, files[files.length - 1]);
    }
  }

  private openInOrder(files: string[], primary: string): void {
    for (const file of files) this.ops.openFile(file, file === primary);
  }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run (from `vscode-extension/`): `npm test`
Expected: PASS — all 6 `projectSync` tests plus the existing suites green.

- [ ] **Step 5: Verify the core compiles under the extension build**

Run (from `vscode-extension/`): `npm run compile`
Expected: exit 0, no type errors. (`out/projectSyncCore.js` produced; it is git-ignored.)

- [ ] **Step 6: Commit**

```bash
git add vscode-extension/src/projectSyncCore.ts vscode-extension/test/projectSync.test.ts
git commit -m "feat: vscode-free ProjectSyncCore for state-gated auto-open

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `ProjectSync` vscode adapter + wiring + setting

**Files:**
- Create: `vscode-extension/src/projectSync.ts`
- Modify: `vscode-extension/src/extension.ts`
- Modify: `vscode-extension/package.json` (add `contributes.configuration`)

**Interfaces:**
- Consumes: `ProjectSyncCore`, `EditorOps` from `./projectSyncCore` (Task 1); `EngineHost` from `./engineHost` (existing — `onStateChange(cb: (state) => void): Disposable`, states `'starting' | 'ready' | 'busy' | 'stopped'`).
- Produces: `class ProjectSync { constructor(host: EngineHost, workspaceRoot: string | undefined); dispose(): void; }`

- [ ] **Step 1: Write the adapter**

Create `vscode-extension/src/projectSync.ts`:

```ts
// vscode-extension/src/projectSync.ts
import * as vscode from 'vscode';
import { EngineHost } from './engineHost';
import { EditorOps, ProjectSyncCore } from './projectSyncCore';

/** Grace window (ms) after the prompt returns, to absorb filesystem-watcher lag. */
const FLUSH_GRACE_MS = 400;

/**
 * Opens/reveals the files the tool writes while a command runs. A FileSystemWatcher feeds
 * every workspace source change to the core, but the core only acts on those buffered
 * between a command starting (engine `busy`) and finishing (`ready`) — so the user's own
 * edits, made while idle, never steal focus. Generated projects are subfolders of the open
 * workspace, so this reveals + opens rather than reloading the window with `openFolder`.
 */
export class ProjectSync {
  private readonly disposables: vscode.Disposable[] = [];
  private flushTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(host: EngineHost, workspaceRoot: string | undefined) {
    if (workspaceRoot === undefined) return; // no folder open → nothing to watch

    const enabled = vscode.workspace
      .getConfiguration('smartcodegen')
      .get<boolean>('autoOpenGenerated', true);

    const ops: EditorOps = {
      revealFolder: (path) =>
        void vscode.commands.executeCommand('revealInExplorer', vscode.Uri.file(path)),
      openFile: (path, focus) => {
        void vscode.workspace.openTextDocument(vscode.Uri.file(path)).then(
          (doc) =>
            vscode.window.showTextDocument(doc, { preview: false, preserveFocus: !focus }),
          () => { /* file vanished between event and flush; best-effort */ },
        );
      },
    };

    const core = new ProjectSyncCore(ops, { enabled, maxTabs: 5 });

    const watcher = vscode.workspace.createFileSystemWatcher(
      new vscode.RelativePattern(workspaceRoot, '**/*.{cs,csproj,xaml,json}'),
    );
    const onFs = (uri: vscode.Uri): void => core.fileEvent(uri.fsPath);
    watcher.onDidCreate(onFs, undefined, this.disposables);
    watcher.onDidChange(onFs, undefined, this.disposables);
    this.disposables.push(watcher);

    this.disposables.push(
      host.onStateChange((state) => {
        if (state === 'busy') {
          // A new command starts. Flush any command still pending its grace window first,
          // so its files aren't lost, then begin buffering the new one.
          if (this.flushTimer !== undefined) {
            clearTimeout(this.flushTimer);
            this.flushTimer = undefined;
            core.commandFinished();
          }
          core.commandStarted();
        } else if (state === 'ready') {
          this.flushTimer = setTimeout(() => {
            this.flushTimer = undefined;
            core.commandFinished();
          }, FLUSH_GRACE_MS);
        }
      }),
    );
  }

  dispose(): void {
    if (this.flushTimer !== undefined) clearTimeout(this.flushTimer);
    for (const d of this.disposables) d.dispose();
    this.disposables.length = 0;
  }
}
```

- [ ] **Step 2: Wire it into `extension.ts`**

In `vscode-extension/src/extension.ts`, add the import after the existing `SmartCodeGenPanel` import (around line 4):

```ts
import { ProjectSync } from './projectSync';
```

Inside `activate`, immediately after the existing `host.onStateChange(...)` block (the one that posts state to the panel, ~line 30-33), add:

```ts
  // Auto-open / reveal projects and files the tool generates while a command runs.
  const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  const projectSync = new ProjectSync(host, workspaceFolder);
```

Then add `projectSync` to the `context.subscriptions.push(...)` call so it is disposed on deactivate — insert it as the last argument before the closing `)`:

```ts
    { dispose: () => host?.dispose() },
    projectSync,
  );
```

(`ProjectSync` is constructed before `host.start()` — which remains the last statement in `activate` — so it observes the `starting → ready` and later transitions.)

- [ ] **Step 3: Add the setting to `package.json`**

In `vscode-extension/package.json`, inside `"contributes"`, add a `"configuration"` block alongside the existing `"commands"` array:

```json
    "configuration": {
      "title": "SmartCodeGen",
      "properties": {
        "smartcodegen.autoOpenGenerated": {
          "type": "boolean",
          "default": true,
          "description": "When the tool creates or changes files, open and reveal them in the editor automatically."
        }
      }
    }
```

Ensure the surrounding JSON stays valid (add a comma after the `"commands"` array's closing `]`).

- [ ] **Step 4: Compile**

Run (from `vscode-extension/`): `npm run compile`
Expected: exit 0, no type errors.

- [ ] **Step 5: Run the full test suite (no regression)**

Run (from `vscode-extension/`): `npm test`
Expected: PASS — Task 1 tests plus the existing `engineHost` / `smoke` suites still green. (The adapter isn't unit-tested here; it needs a vscode runtime and is verified by compile + the manual smoke test in Task 3.)

- [ ] **Step 6: Commit**

```bash
git add vscode-extension/src/projectSync.ts vscode-extension/src/extension.ts vscode-extension/package.json
git commit -m "feat: wire ProjectSync watcher into the extension with a toggle

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Repackage the `.vsix` and verify (fixes #1 and #2)

**Files:**
- Produces (git-ignored, do not commit): `vscode-extension/smartcodegen-vscode.vsix`

**Interfaces:**
- Consumes: the compiled `out/` from Tasks 1–2 and the current `media/`, `src/` on this branch.
- Produces: an installable `.vsix` carrying the prompt-free fix (`36768ef`, already on `main`/this branch) and Project Sync.

- [ ] **Step 1: Rebuild the package**

Run (from `vscode-extension/`): `npm run package`
Expected: exit 0; `smartcodegen-vscode.vsix` rewritten (newer mtime than the pre-fix 15:43 build). `vsce` may warn about a missing repository field/README — warnings are fine, an error is not.

- [ ] **Step 2: Confirm the packaged bits are current**

Run (from `vscode-extension/`): `npx @vscode/vsce ls`
Expected: the listing includes `out/projectSync.js`, `out/projectSyncCore.js`, `out/extension.js`, `media/main.js`, and `package.json`. This confirms Project Sync and the current media (with the stderr `info` styling from `36768ef`) are in the package.

- [ ] **Step 3: Install and manually verify (human step)**

Run (from `vscode-extension/`): `npm run install-ext`
Then, with the repo root open in VS Code:
1. Open the **SmartCodeGen** panel (Activity Bar). Confirm the input becomes usable within a few seconds and the store-update lines appear as muted status text **without** the input ever greying out — this is fix #1.
2. Type `build app: a shop with customers and orders`. When the command completes, confirm the new project folder is revealed in the Explorer and its `.csproj` + entry point + a model open in tabs — this is fix #2 (create → load).
3. Run an edit command (e.g. `add property Email to Customer`). Confirm the changed file opens/focuses when the command finishes — this is fix #2 (change → reflect).
4. Manually edit and save an unrelated file while the engine is idle. Confirm **no** tab is force-opened/stolen — the state gate working.

- [ ] **Step 4: Update memory**

Update `C:\Users\admin\.claude\projects\D--SmartCodeGenerator-Repository\memory\MEMORY.md` and the relevant memory file to record that Project Sync shipped on branch `feat/vscode-project-sync` and that fix #1 was a stale `.vsix` (source already correct as of `36768ef`), so it is not re-investigated.

- [ ] **Step 5: Finish the branch**

Use the `superpowers:finishing-a-development-branch` skill to choose merge/PR. Do not commit the `.vsix` (git-ignored).

---

## Self-Review

**Spec coverage:**
- Fix #1 (stale `.vsix`, rebuild + reinstall) → Task 3 Steps 1-3. ✓
- Fix #2 state-gated watcher, tool-vs-user distinction → Task 1 (core) + Task 2 (adapter/state mapping). ✓
- Reveal + key files on project create (no `openFolder`) → `selectKeyFiles` + `revealFolder`, Task 1; adapter uses `revealInExplorer`, Task 2. ✓
- Open/focus changed files on regeneration → `act` else-branch, Task 1. ✓
- Excludes (`bin/obj/.git/node_modules/Dump/TestResults/.codegen/.vs`) → `EXCLUDED_SEGMENTS`, tested. ✓
- Cap 5 tabs, grace 400 ms → `maxTabs`/`FLUSH_GRACE_MS`. ✓
- `smartcodegen.autoOpenGenerated` toggle → Task 2 Step 3, tested (`enabled=false`). ✓
- vscode-free core, `node --test` style → Task 1, tsconfig-compatible. ✓
- No C#/CLI changes → confirmed; only `vscode-extension/` touched. ✓

**Placeholder scan:** none — all steps carry full code/commands.

**Type consistency:** `EditorOps` (`revealFolder`, `openFile(path, focus)`), `ProjectSyncOptions` (`enabled`, `maxTabs`), and `ProjectSyncCore` (`commandStarted`/`fileEvent`/`commandFinished`) are identical across Task 1 (definition + tests) and Task 2 (adapter). `EngineHost.onStateChange` returns a `Disposable` pushed into `vscode.Disposable[]` (structurally compatible). ✓

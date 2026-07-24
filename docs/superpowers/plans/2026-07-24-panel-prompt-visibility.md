# Panel Prompt Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `CodeGenerator >>` visible at all times in the VS Code panel — as a fixed gutter label in front of the input, and as the transcript echo prefix — so the panel reads as a real REPL.

**Architecture:** The one non-trivial decision (which prefix a submitted line gets) is extracted from the untestable webview script into `media/prompt.js`, an ES module imported by both `media/main.js` and a Node test. Everything else is layout: a flex row in `media/main.css` and a `<span>` in the HTML that `panelView.ts` emits.

**Tech Stack:** VS Code webview API, plain ES modules (no bundler), `node --test` via `tsx`.

## Global Constraints

- Spec: [`docs/superpowers/specs/2026-07-24-panel-prompt-visibility-design.md`](../specs/2026-07-24-panel-prompt-visibility-design.md)
- The label text is **constant** in every engine state. State information stays in the placeholder.
- The prompt string is exactly `CodeGenerator >> ` (trailing space included).
- The webview protocol (`src/protocol.ts`), `src/engineHost.ts`, and the console CLI are **not** modified by this plan.
- All colours come from `var(--vscode-*)` tokens. No hardcoded colour values.
- Verification of anything visual requires `npm run compile && npm run package && npm run install-ext`, then reloading the VS Code window. A stale `.vsix` has previously produced a convincing false negative on this extension.

---

### Task 1: Extract the prompt module and test it

**Files:**
- Create: `vscode-extension/media/prompt.js`
- Create: `vscode-extension/media/package.json`
- Test: `vscode-extension/test/prompt.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: ES module `media/prompt.js` exporting
  - `PROMPT: string` — the literal `'CodeGenerator >> '`
  - `echoPrefix(state: string): string` — `'> '` when `state === 'prompting'`, otherwise `PROMPT`

**Why `media/package.json`:** Node decides a `.js` file's module kind from the nearest `package.json`. `vscode-extension/package.json` has no `"type"` field, so `.js` there is CommonJS and `export const` would be a syntax error under the test runner. Adding `{ "type": "module" }` scoped to `media/` makes `.js` files in that directory ESM for Node, while keeping the extension's own `out/*.js` CommonJS. The `.js` extension is retained (rather than `.mjs`) so the webview's resource server serves an unambiguous JavaScript MIME type — module scripts are rejected by the browser otherwise.

- [ ] **Step 1: Write the failing test**

Create `vscode-extension/test/prompt.test.ts`:

```ts
// vscode-extension/test/prompt.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import { PROMPT, echoPrefix } from '../media/prompt.js';

test('PROMPT is the CLI prompt string, trailing space included', () => {
  assert.strictEqual(PROMPT, 'CodeGenerator >> ');
});

test('top-level submissions echo behind the full prompt', () => {
  for (const state of ['ready', 'busy', 'starting', 'stopped']) {
    assert.strictEqual(echoPrefix(state), 'CodeGenerator >> ');
  }
});

test('an answer to a mid-command question keeps the bare marker', () => {
  // While `prompting` the engine is blocked on its own question (a batch block line,
  // `approve? (y/n)`). What the user types is an ANSWER, so echoing it behind the
  // top-level prompt would put a command in the scrollback that was never issued.
  assert.strictEqual(echoPrefix('prompting'), '> ');
});

test('an unknown state falls back to the top-level prompt', () => {
  assert.strictEqual(echoPrefix('something-new'), 'CodeGenerator >> ');
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd vscode-extension && npm test`

Expected: FAIL — `Cannot find module '../media/prompt.js'`.

- [ ] **Step 3: Create the module directory marker**

Create `vscode-extension/media/package.json`:

```json
{ "type": "module" }
```

- [ ] **Step 4: Write the module**

Create `vscode-extension/media/prompt.js`:

```js
// vscode-extension/media/prompt.js
// Shared by the webview (loaded as an ES module) and by test/prompt.test.ts under Node.
// media/package.json marks this directory "type": "module" so Node treats .js here as
// ESM; the .js extension is kept so the webview gets an unambiguous JavaScript MIME type.

/** The CLI's top-level prompt. Rendered as the gutter label and used as the echo prefix. */
export const PROMPT = 'CodeGenerator >> ';

/**
 * The prefix a submitted line gets in the transcript.
 *
 * While `prompting`, the engine is mid-command and blocked on its own question (batch's
 * block reader, the approval loop). What the user types is an ANSWER, not a command —
 * echoing it behind the top-level prompt would put a command in the scrollback that was
 * never issued. Every other state submits a real top-level command.
 */
export function echoPrefix(state) {
  return state === 'prompting' ? '> ' : PROMPT;
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd vscode-extension && npm test`

Expected: PASS — all four `prompt.test.ts` tests, and the existing `engineHost`, `projectSync`, and `smoke` suites still green.

- [ ] **Step 6: Commit**

```bash
git add vscode-extension/media/prompt.js vscode-extension/media/package.json vscode-extension/test/prompt.test.ts
git commit -m "feat(panel): extract prompt string and echo-prefix rule into a tested module"
```

---

### Task 2: Render the prompt in the panel

**Files:**
- Modify: `vscode-extension/src/panelView.ts:57-58` (CSP), `:63-65` (form markup), `:66` (script tag)
- Modify: `vscode-extension/media/main.css:14-20`
- Modify: `vscode-extension/media/main.js:1-8` (import + label handle), `:24` (echo), `:53-61` (state handler)

**Interfaces:**
- Consumes: `PROMPT` and `echoPrefix(state)` from `./prompt.js` (Task 1).
- Produces: no new exports. This task is the wiring.

- [ ] **Step 1: Widen the CSP and emit the label**

In `vscode-extension/src/panelView.ts`, replace the `Content-Security-Policy` meta tag and the form.

Change the CSP line from:

```html
    content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';" />
```

to:

```html
    content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}' ${webview.cspSource};" />
```

**This is load-bearing, not tidying.** `main.js` becomes a module whose `import './prompt.js'` is
fetched as a separate script subresource, and a CSP nonce does **not** propagate to imported
modules. Without `${webview.cspSource}` in `script-src`, the import is blocked and the panel comes
up blank. `localResourceRoots` already confines this to the `media` directory.

Change the form from:

```html
  <form id="inputform">
    <input id="input" type="text" autocomplete="off" placeholder="Type a command…" />
  </form>
```

to:

```html
  <form id="inputform">
    <span id="promptlabel" aria-hidden="true"></span>
    <input id="input" type="text" autocomplete="off"
           aria-label="CodeGenerator command" placeholder="Type a command…" />
  </form>
```

The span is left empty on purpose — `main.js` fills it from `PROMPT`, so the label and the
transcript echo cannot drift apart. `aria-hidden` plus the explicit `aria-label` keeps the input's
accessible name stable: the span is decoration, not a `<label>`, and must not be announced as one.

Change the script tag from:

```html
  <script nonce="${nonce}" src="${jsUri}"></script>
```

to:

```html
  <script type="module" nonce="${nonce}" src="${jsUri}"></script>
```

- [ ] **Step 2: Lay out the input row**

In `vscode-extension/media/main.css`, replace the `#inputform` and `#input` rules:

```css
#inputform {
  display: flex; align-items: center; gap: 6px;
  border-top: 1px solid var(--vscode-panel-border); padding: 4px;
}
#promptlabel {
  flex: 0 0 auto; user-select: none; white-space: pre;
  color: var(--vscode-textLink-foreground);
}
/* Matches #input:disabled, so the label dims with the box it belongs to. */
#promptlabel.dim { opacity: 0.6; }
#input {
  flex: 1 1 auto; min-width: 0; box-sizing: border-box; padding: 4px;
  background: var(--vscode-input-background); color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
}
#input:disabled { opacity: 0.6; }
```

`#input` loses `width: 100%` in favour of `flex: 1 1 auto`; `min-width: 0` stops the input from
overflowing the row at narrow panel widths. `--vscode-textLink-foreground` is the same token
`.line.cmd` already uses, so the live prompt and the echoed prompt read as one thing.

- [ ] **Step 3: Wire the label and the echo in the webview script**

In `vscode-extension/media/main.js`, add the import and the label handle at the top:

```js
// vscode-extension/media/main.js
import { PROMPT, echoPrefix } from './prompt.js';

const vscode = acquireVsCodeApi();
const transcript = document.getElementById('transcript');
const form = document.getElementById('inputform');
const input = document.getElementById('input');
const promptLabel = document.getElementById('promptlabel');
const history = [];
let histIndex = -1;
let state = 'starting';

// Single source of truth: the label and the transcript echo both come from PROMPT.
// Trailing space trimmed because the flex `gap` provides the spacing on screen.
promptLabel.textContent = PROMPT.trimEnd();
```

Change the echo line inside the submit handler from:

```js
  append('> ' + text, 'cmd');
```

to:

```js
  append(echoPrefix(state) + text, 'cmd');
```

In the `state` branch of the message listener, add the dim toggle immediately after
`input.disabled` is assigned and before the placeholder assignment:

```js
    input.disabled = msg.state === 'busy' || msg.state === 'starting' || msg.state === 'stopped';
    promptLabel.classList.toggle('dim', input.disabled);
```

- [ ] **Step 4: Verify the unit tests and the compile still pass**

Run: `cd vscode-extension && npm test && npm run compile`

Expected: all suites PASS, and `tsc` reports no errors. (`tsconfig.json` excludes `test`, and
`media` is not compiled, so this checks `src/panelView.ts` only.)

- [ ] **Step 5: Rebuild, reinstall, and verify in a real panel**

Run:

```bash
cd vscode-extension && npm run compile && npm run package && npm run install-ext
```

Then reload the VS Code window and open the SmartCodeGen panel. Confirm all of:

1. **The panel renders at all.** A blank panel means the CSP change in Step 1 did not take —
   open the webview developer tools (`Developer: Open Webview Developer Tools`) and check the
   console for a `Refused to load` CSP error naming `prompt.js`.
2. `CodeGenerator >>` is visible in front of the input box before you type anything.
3. While the engine is starting and while a command is running, the label dims along with the
   input box, and its text does not change.
4. Submitting `help` echoes into the transcript as `CodeGenerator >> help`.
5. Run a command that asks a question (`batch`, or a `build app:` that reaches the review loop).
   While it is waiting, the label still reads `CodeGenerator >>`, the placeholder reads
   `answer the question above…`, and the answer you type echoes as `> y` — **not**
   `CodeGenerator >> y`.
6. Narrow the panel to roughly a third of the editor width. The label stays intact on one line and
   the input shrinks; the row does not wrap or overflow.

- [ ] **Step 6: Commit**

```bash
git add vscode-extension/src/panelView.ts vscode-extension/media/main.css vscode-extension/media/main.js
git commit -m "feat(panel): show CodeGenerator >> as a gutter label and transcript echo prefix"
```

---

## Self-Review

**Spec coverage.** Input row markup, label colour/`user-select`/accessibility — Task 2 Step 1 and
Step 2. Constant label text across all five engine states with dimming — Task 2 Steps 2-3, verified
in Step 5.3. Transcript echo via `echoPrefix` including the `prompting` exception — Task 1 (logic
and tests) and Task 2 Step 3 (wiring), verified in Step 5.4-5.5. The `media/prompt.js` module and
its Node test — Task 1. The CSP consequence the spec flags as "must not be missed" — Task 2 Step 1,
with the blank-panel failure mode called out again in the Step 5 verification. The rebuild-and-
reinstall note — Global Constraints and Task 2 Step 5. Rejected mirroring of the real mid-command
prompt is honoured: no protocol change appears in any task. Out-of-scope items (console CLI,
`engineHost`, panel history) are untouched.

**Placeholder scan.** No TBDs, no "add error handling", no "similar to Task N". Every code step
carries the literal code.

**Type consistency.** `PROMPT` and `echoPrefix` are named identically in Task 1's module, Task 1's
test, and Task 2's import and call sites. The element id `promptlabel` matches across
`panelView.ts` (Step 1), `main.css` (Step 2), and `main.js` (Step 3). The `dim` class name matches
between `main.css` and the `classList.toggle` call.

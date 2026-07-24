# Panel prompt visibility — design

**Date:** 2026-07-24
**Status:** Approved for planning

## Problem

The CLI's prompt string is already exactly `"CodeGenerator >> "` — see
[`CommandLineEditor.cs:17`](../../../CodeGenerator/App/CommandLineEditor.cs) and
[`CodeGeneratorStarter.cs:123`](../../../CodeGenerator/App/CodeGeneratorStarter.cs). But it never
reaches the VS Code panel. The panel's input is a bare box with a grey `Type a command…`
placeholder ([`panelView.ts:64`](../../../vscode-extension/src/panelView.ts)), and submitted
commands echo into the transcript with a plain `> ` prefix
([`main.js:24`](../../../vscode-extension/media/main.js)).

The string `CodeGenerator >>` appears in the extension only inside the regex that *detects* the
prompt on stdout ([`engineHost.ts:51`](../../../vscode-extension/src/engineHost.ts)) — never in
anything the user sees.

## Goal

The panel reads as a real REPL: the prompt is on screen at all times, and it is in the scrollback.

## Scope

VS Code panel only. The console CLI is **not** changed — its prompt was judged prominent enough as
it stands.

## Design

### Input row

`#inputform` becomes a flex row holding a fixed label plus the existing input:

```html
<form id="inputform">
  <span id="promptlabel" aria-hidden="true">CodeGenerator &gt;&gt;</span>
  <input id="input" type="text" autocomplete="off" aria-label="CodeGenerator command" />
</form>
```

- The label is `flex: 0 0 auto` and `user-select: none` — decoration, not content to be copied out
  of the panel.
- It takes `var(--vscode-textLink-foreground)`, the colour `.line.cmd` already uses for echoed
  commands, so the live prompt and the scrollback prompt read as the same thing.
- `aria-hidden` on the label plus an explicit `aria-label` on the input keeps the accessible name
  stable. The label is not a `<label>` element and must not be announced as one.
- The input keeps `width: 100%; box-sizing: border-box` behaviour inside the flex row (`flex: 1 1
  auto`), so it still fills the remaining width at any panel size.

### Label states

The label text is **constant**. It never changes, in any engine state.

State continues to live in the placeholder, exactly as today
([`main.js:55-59`](../../../vscode-extension/media/main.js)):

| Engine state | Label | Placeholder | Input |
|---|---|---|---|
| `starting` | `CodeGenerator >>` (dim) | `starting engine…` | disabled |
| `ready` | `CodeGenerator >>` | `Type a command…` | enabled |
| `busy` | `CodeGenerator >>` (dim) | `working…` | disabled |
| `prompting` | `CodeGenerator >>` | `answer the question above…` | enabled |
| `stopped` | `CodeGenerator >>` (dim) | `engine stopped` | disabled |

"Dim" is a `dim` class toggled whenever `input.disabled` is set, giving `opacity: 0.6` to match the
existing `#input:disabled` rule.

**Rejected:** mirroring the engine's real mid-command prompt (`... `, `> `, `approve? (y/n) `) in
the label. It would be more truthful during `prompting`, but `engineHost` posts only the state
*name* — the prompt text is matched against a regex and discarded, so carrying it would need a new
protocol field. Not worth it for the gain. The protocol is unchanged by this work.

### Transcript echo

[`main.js:24`](../../../vscode-extension/media/main.js) changes from a hardcoded `'> ' + text` to
`echoPrefix(state) + text`.

`echoPrefix` is **not** a constant, because not every submission is a command:

```
echoPrefix(state) = state === 'prompting' ? '> ' : 'CodeGenerator >> '
```

When the engine is `prompting`, what the user types is an *answer* to a mid-command question
(batch's block reader, the approval loop). Echoing `y` into the transcript as
`CodeGenerator >> y` would misrepresent the scrollback — it would read as a top-level command that
was never issued. `main.js` already tracks `state` for the blank-line rule at
[`main.js:23`](../../../vscode-extension/media/main.js), so this costs nothing.

## Testing

`media/main.js` is served raw to the webview and has no tests today — it grabs the DOM at load and
calls `acquireVsCodeApi()`, so it cannot be imported by the Node test runner.

The one non-trivial decision is extracted into `media/prompt.js` as an ES module:

```js
export const PROMPT = 'CodeGenerator >> ';
export function echoPrefix(state) { ... }
```

`main.js` imports it, and `test/prompt.test.ts` imports it directly under the existing
`node --import tsx --test test/*.test.ts` runner. No bundler is introduced.

### CSP consequence — must not be missed

Loading `main.js` as `type="module"` means its static `import './prompt.js'` is fetched as a
separate script subresource. **A nonce does not propagate to imported modules**, so under the
current policy (`script-src 'nonce-${nonce}'`) the import would be blocked and the panel would come
up blank.

The fix is to widen the directive to `script-src 'nonce-${nonce}' ${webview.cspSource};` so scripts
from the webview's own resource origin are permitted. `localResourceRoots` already restricts this
to the `media` directory. This must be verified in a running webview, not assumed — a blank panel
is the failure mode.

Everything else — the flex layout, colours, dim class — is verified by rebuilding and reinstalling
the `.vsix`, not by unit tests.

### Verification note

Rebuild **and** reinstall the `.vsix` before judging whether a change worked
(`npm run compile && npm run package && npm run install-ext`). A stale `.vsix` has previously
produced a convincing false negative on this extension.

## Out of scope

- Any change to the console CLI's prompt rendering or `CommandLineEditor`.
- Any change to `engineHost`'s state machine, the ready/prompt markers, or the webview protocol.
- Command history, suggestions, or tab-completion in the panel.

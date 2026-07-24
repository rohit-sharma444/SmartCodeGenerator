// vscode-extension/media/prompt.js
// Shared by the webview (loaded as an ES module) and by test/prompt.test.ts under Node.
// media/package.json marks this directory "type": "module" so Node treats .js here as
// ESM; the .js extension is kept so the webview gets an unambiguous JavaScript MIME type.
//
// Consequence of that "type": "module" marker: EVERY .js file placed under media/ is now
// ESM as far as Node (and this test suite) is concerned, regardless of how the webview
// loads it. A future classic `<script>` (non-module) added here would still run fine in
// the browser but would be misinterpreted by Node's test runner — tests could pass against
// a file the panel cannot actually execute the way the test exercises it. Keep every .js
// file in this directory loaded as a module (`<script type="module">` / `import`) or move
// it out of media/.

/**
 * The CLI's top-level prompt. Rendered as the gutter label and used as the echo prefix.
 *
 * Hand-copied from the C# source of truth — there is no build-time link, so if you change
 * one, change all four:
 *   - CodeGenerator/App/CommandLineEditor.cs:17  (private const string Prompt = ...)
 *   - CodeGenerator/App/CommandLineEditor.cs:270  (const string prompt = ...)
 *   - vscode-extension/src/engineHost.ts:51  (readyMarker regex)
 *   - this constant
 */
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

/**
 * Removes a single trailing occurrence of `PROMPT` from a raw stdout chunk.
 *
 * The engine writes its own `CodeGenerator >> ` prompt to stdout — engineHost's ready
 * detection depends on seeing it there, so that can't change — but that means every chunk
 * forwarded to the webview still carries it, and without this the transcript would show the
 * prompt twice on the most common path (once as its own line, once as the echo prefix of the
 * next submitted command).
 *
 * Deliberately a suffix check, not whole-chunk equality: the engine combines output and the
 * next prompt into one write (e.g. `"ran: create class Foo\nCodeGenerator >> "`), so an
 * equality test would miss roughly half of real chunks. A `PROMPT` that appears anywhere
 * other than the very end is left untouched — that's real output, not the marker.
 */
export function stripTrailingPrompt(chunk) {
  return chunk.endsWith(PROMPT) ? chunk.slice(0, chunk.length - PROMPT.length) : chunk;
}

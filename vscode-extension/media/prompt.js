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

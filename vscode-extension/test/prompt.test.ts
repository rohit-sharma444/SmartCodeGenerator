// vscode-extension/test/prompt.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import { PROMPT, echoPrefix, isClear, isPromptEcho, stripTrailingPrompt } from '../media/prompt.js';

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

// The engine writes its own `CodeGenerator >> ` prompt to stdout (engineHost's ready
// detection depends on seeing it), so the raw chunk forwarded to the webview still carries
// it. Without stripping, the transcript would double the prompt on every single command.
test('stripTrailingPrompt: a chunk that is exactly the prompt becomes empty', () => {
  assert.strictEqual(stripTrailingPrompt('CodeGenerator >> '), '');
});

test('stripTrailingPrompt: real output followed by the prompt keeps only the output', () => {
  assert.strictEqual(
    stripTrailingPrompt('ran: create class Foo\nCodeGenerator >> '),
    'ran: create class Foo\n',
  );
});

test('stripTrailingPrompt: a chunk with no prompt is left unchanged', () => {
  assert.strictEqual(
    stripTrailingPrompt('ran: create class Foo\n'),
    'ran: create class Foo\n',
  );
});

test('stripTrailingPrompt: the prompt in the middle (not at the end) is left alone', () => {
  // e.g. the ready marker showed up mid-chunk because more output followed it in the same
  // write; only a *trailing* occurrence is the artifact this function exists to remove.
  assert.strictEqual(
    stripTrailingPrompt('CodeGenerator >> help\n'),
    'CodeGenerator >> help\n',
  );
});

test('stripTrailingPrompt: an empty chunk stays empty', () => {
  assert.strictEqual(stripTrailingPrompt(''), '');
});

// The panel has to recognise `clear` itself: the CLI's own handler calls Console.Clear()
// only when stdout is NOT redirected, and under the extension it always is, so forwarding
// the word would wipe nothing. These cases mirror CodeGenerator/App/ClearCommand.cs.
test('isClear: both spellings, any case, surrounding space ignored', () => {
  for (const text of ['clear', 'cls', 'CLEAR', 'Cls', '  clear  ', '\tcls\t']) {
    assert.strictEqual(isClear(text), true, `expected ${JSON.stringify(text)} to clear`);
  }
});

// Under redirection the CLI echoes the command it read (CommandLineEditor.ReadRedirectedLine),
// so stdout carries `CodeGenerator >> help` as one line. The panel already renders that itself.
test('isPromptEcho: the engine echoing back what we sent', () => {
  assert.strictEqual(isPromptEcho('CodeGenerator >> help', 'help'), true);
  assert.strictEqual(isPromptEcho('CodeGenerator >> help\r', 'help'), true, 'CRLF line ending');
  assert.strictEqual(isPromptEcho('CodeGenerator >> ', ''), true, 'a bare prompt after a blank submit');
});

test('isPromptEcho: real output is never swallowed', () => {
  // The decisive case: help text that MENTIONS the prompt must survive, which is why this
  // matches the whole line against the exact command sent rather than testing for a prefix.
  assert.strictEqual(
    isPromptEcho("CodeGenerator >> is the prompt; type 'help' there", 'help'), false);
  assert.strictEqual(isPromptEcho('CodeGenerator >> help', 'exit'), false, 'a different command');
  assert.strictEqual(isPromptEcho('Created Customer.cs', 'create Customer class'), false);
  assert.strictEqual(isPromptEcho('', 'help'), false);
});

test('isClear: anything else is a command for the engine', () => {
  // `clear all` and `clearly` matter: the CLI compares the whole trimmed line, so a
  // prefix match here would swallow real commands the engine should have seen.
  for (const text of ['', '   ', 'clearly', 'clear all', 'clsx', 'help', 'create Clear class']) {
    assert.strictEqual(isClear(text), false, `expected ${JSON.stringify(text)} not to clear`);
  }
});

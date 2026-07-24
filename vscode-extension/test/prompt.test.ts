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

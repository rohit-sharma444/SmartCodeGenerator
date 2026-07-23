// vscode-extension/test/smoke.test.ts
import { test } from 'node:test';
import assert from 'node:assert';

test('toolchain runs TypeScript tests', () => {
  assert.equal(1 + 1, 2);
});

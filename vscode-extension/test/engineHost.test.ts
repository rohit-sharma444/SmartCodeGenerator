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

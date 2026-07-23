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

test('a failed spawn transitions to stopped instead of throwing', async () => {
  const host = new EngineHost({
    command: 'definitely-not-a-real-binary-xyz123',
    args: [],
    cwd: process.cwd(),
  });
  host.start();
  await waitForState(host, 'stopped');
  assert.equal(host.state, 'stopped');
  host.dispose();
});

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

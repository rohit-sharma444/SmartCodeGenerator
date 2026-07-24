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

function waitForState(host: EngineHost, target: EngineState, ms = 5000): Promise<void> {
  return new Promise((resolve, reject) => {
    if (host.state === target) return resolve();
    const timer = setTimeout(
      () => { d.dispose(); reject(new Error(`timeout waiting for ${target}; state=${host.state}`)); },
      ms,
    );
    const d = host.onStateChange((s) => {
      if (s === target) { clearTimeout(timer); d.dispose(); resolve(); }
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

// The CLI blocks on Console.ReadLine() at several prompts that are NOT the top-level
// `CodeGenerator >> ` marker (batch's `... ` block reader and `approve? (y/n) ` loop,
// build-app's `> ` review loop). Treating those as `busy` left the panel input disabled
// while the engine waited for an answer — a deadlock, since the engine only returns to the
// top-level prompt once answered.
for (const command of ['ask-dots', 'ask-arrow', 'ask-approve']) {
  test(`a mid-command prompt (${command}) enters prompting, not busy`, async () => {
    const host = makeHost();
    try {
      host.start();
      await waitForState(host, 'ready');
      host.send(command);
      assert.equal(host.state, 'busy');
      await waitForState(host, 'prompting');
      assert.equal(host.state, 'prompting');
    } finally {
      host.dispose();
    }
  });
}

test('an answer sent while prompting reaches the engine and returns to ready', async () => {
  const host = makeHost();
  const out = collect(host);
  try {
    host.start();
    await waitForState(host, 'ready');
    host.send('ask-dots');
    await waitForState(host, 'prompting');
    host.send('create class Foo');
    await waitForState(host, 'ready');
    const text = out.filter((o) => o.stream === 'stdout').map((o) => o.c).join('');
    assert.match(text, /answered: create class Foo/);
  } finally {
    host.dispose();
  }
});

test('the top-level prompt is ready, never prompting', async () => {
  // `CodeGenerator >> ` also ends in "> " — it must not be mistaken for the review-loop prompt.
  const host = makeHost();
  const seen: EngineState[] = [];
  host.onStateChange((s) => seen.push(s));
  try {
    host.start();
    await waitForState(host, 'ready');
    host.send('create class Foo');
    await waitForState(host, 'ready');
    assert.ok(!seen.includes('prompting'), `unexpected prompting in ${JSON.stringify(seen)}`);
  } finally {
    host.dispose();
  }
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

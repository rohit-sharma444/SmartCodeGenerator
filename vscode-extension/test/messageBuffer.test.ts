// vscode-extension/test/messageBuffer.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import { MessageBuffer } from '../src/messageBuffer';

test('messages posted before the sink opens are delivered once it does, in order', () => {
  // The reported bug: activate() explains why the engine cannot run ~2.7s before the webview
  // exists, and every word of it was thrown away, leaving a panel that just said "engine
  // stopped". Holding them until the webview says it is listening is the whole point.
  const buffer = new MessageBuffer<string>();
  buffer.post('not a checkout');
  buffer.post('stopped');

  const seen: string[] = [];
  buffer.open((m) => seen.push(m));

  assert.deepStrictEqual(seen, ['not a checkout', 'stopped']);
});

test('messages posted after the sink opens go straight through', () => {
  const buffer = new MessageBuffer<string>();
  const seen: string[] = [];
  buffer.open((m) => seen.push(m));

  buffer.post('live');

  assert.deepStrictEqual(seen, ['live']);
});

test('opening a second time does not replay what the first sink already saw', () => {
  // A webview that reloads gets a fresh transcript, but replaying startup messages into a
  // session that has moved on would be worse than saying nothing.
  const buffer = new MessageBuffer<string>();
  buffer.post('early');

  const first: string[] = [];
  buffer.open((m) => first.push(m));
  buffer.close();

  const second: string[] = [];
  buffer.open((m) => second.push(m));

  assert.deepStrictEqual(first, ['early']);
  assert.deepStrictEqual(second, []);
});

test('closing resumes buffering rather than posting into a disposed view', () => {
  const buffer = new MessageBuffer<string>();
  const seen: string[] = [];
  buffer.open((m) => seen.push(m));
  buffer.close();

  buffer.post('while closed');
  assert.deepStrictEqual(seen, [], 'must not reach the closed sink');

  buffer.open((m) => seen.push(m));
  assert.deepStrictEqual(seen, ['while closed']);
});

test('the backlog is capped, keeping the most recent messages', () => {
  // A view the user closed leaves engine output with nowhere to go; unbounded buffering would
  // be a leak. Oldest-first eviction matches a terminal scrollback.
  const buffer = new MessageBuffer<number>(3);
  for (const n of [1, 2, 3, 4, 5]) buffer.post(n);

  const seen: number[] = [];
  buffer.open((m) => seen.push(m));

  assert.deepStrictEqual(seen, [3, 4, 5]);
});

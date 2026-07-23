// vscode-extension/test/engineHost.integration.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import * as os from 'node:os';
import * as path from 'node:path';
import * as fs from 'node:fs';
import { EngineHost, EngineState } from '../src/engineHost';

const RUN = !!process.env.RUN_ENGINE_INTEGRATION;
const repoRoot = path.resolve(process.cwd(), '..'); // vscode-extension/ -> repo root

function waitForState(host: EngineHost, target: EngineState, ms = 120000): Promise<void> {
  return new Promise((resolve, reject) => {
    if (host.state === target) return resolve();
    const timer = setTimeout(() => reject(new Error(`timeout waiting for ${target}`)), ms);
    const d = host.onStateChange((s) => {
      if (s === target) { clearTimeout(timer); d.dispose(); resolve(); }
    });
  });
}

test('real CLI: a class created by one command is visible to the next', { skip: !RUN }, async () => {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'scg-e2e-'));
  const out: string[] = [];
  const host = new EngineHost({
    command: 'dotnet',
    args: ['run', '--project', 'CodeGenerator'],
    cwd: repoRoot,
    env: { ...process.env, PROJECTPATH: workspace },
    readyMarker: /CodeGenerator >>\s*$/,
  });
  host.onOutput((c, stream) => { if (stream === 'stdout') out.push(c); });

  host.start();
  await waitForState(host, 'ready');
  // A class created with no project selected is written straight into the
  // workspace, but `show class` only looks inside the active project - so a
  // project must be created and selected first for the two commands to
  // actually share state (see CodeGenerator/README.md's example session).
  host.send('create console project IntegrationApp');
  await waitForState(host, 'ready');
  host.send('use project IntegrationApp');
  await waitForState(host, 'ready');
  host.send('create class IntegrationFoo');
  await waitForState(host, 'ready');
  const beforeShow = out.length;
  host.send('show class IntegrationFoo');
  await waitForState(host, 'ready');
  host.dispose();

  const showOutput = out.slice(beforeShow).join('');
  assert.match(showOutput, /public class IntegrationFoo/);
});

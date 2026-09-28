// Talks to WpfDriver.exe over its stdin/stdout JSON-lines protocol (see WpfDriver/Program.cs for
// the command list). record.mjs owns all scene pacing and captions; this client is just the
// request/response plumbing, one command in, one answer out, in order.
import { spawn } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const exe = path.join(here, 'WpfDriver', 'bin', 'Release', 'net10.0-windows', 'WpfDriver.exe');

export function startDriver() {
  const proc = spawn(exe, [], { stdio: ['pipe', 'pipe', 'inherit'] });
  const pending = [];
  let buffer = '';

  proc.stdout.on('data', chunk => {
    buffer += chunk.toString('utf8');
    let newline;
    while ((newline = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newline).trim();
      buffer = buffer.slice(newline + 1);
      if (!line) continue;
      const resolve = pending.shift();
      resolve?.(JSON.parse(line));
    }
  });

  proc.on('exit', code => {
    // Anything still waiting gets told the driver is gone, rather than hanging forever.
    while (pending.length) pending.shift()?.({ ok: false, error: `driver exited (${code})` });
  });

  /** Sends one command, resolves with its one reply. Calls do not overlap: the driver answers in
   * the order it received them, so a caller awaiting each one keeps the two streams in step. */
  function send(command) {
    return new Promise(resolve => {
      pending.push(resolve);
      proc.stdin.write(JSON.stringify(command) + '\n');
    });
  }

  async function stop() {
    await send({ cmd: 'quit' }).catch(() => {});
    proc.kill();
  }

  return { send, stop };
}

// Process plumbing shared by every step: run-to-completion with a timeout, start-and-keep for the
// apps being launched, and a whole-tree kill, because `dotnet run` leaves the real app as a CHILD
// process that killing dotnet alone would orphan - still holding a port or a locked binary that
// the next case's build then fails on.
import { spawn, execFileSync } from 'child_process';
import net from 'net';

/** Runs to completion. Never rejects: a failure is a result the report shows, not an exception. */
export function run(command, args, { cwd, env, input, timeoutMs = 10 * 60_000 } = {}) {
  return new Promise(resolve => {
    const started = Date.now();
    const proc = spawn(command, args, { cwd, env: { ...process.env, ...env }, windowsHide: true });
    let out = '';
    let timedOut = false;

    proc.stdout.on('data', chunk => (out += chunk));
    proc.stderr.on('data', chunk => (out += chunk));

    const timer = setTimeout(() => {
      timedOut = true;
      killTree(proc.pid);
    }, timeoutMs);

    proc.on('error', error => (out += `\n${error.message}`));
    proc.on('close', code => {
      clearTimeout(timer);
      resolve({ code: timedOut ? -1 : code, out, timedOut, secs: (Date.now() - started) / 1000 });
    });

    if (input !== undefined) proc.stdin.end(input);
    else proc.stdin.end();
  });
}

/** Starts and keeps running; the caller reads `output()` and must call `stop()`. Not windowsHide:
 * that starts a desktop app with its window hidden, and UI Automation then finds none. */
export function start(command, args, { cwd, env } = {}) {
  const proc = spawn(command, args, { cwd, env: { ...process.env, ...env } });
  let out = '';
  let exitCode = null;

  proc.stdout.on('data', chunk => (out += chunk));
  proc.stderr.on('data', chunk => (out += chunk));
  proc.on('error', error => (out += `\n${error.message}`));
  proc.on('exit', code => (exitCode = code ?? -1));

  return {
    pid: proc.pid,
    output: () => out,
    exited: () => exitCode !== null,
    exitCode: () => exitCode,
    stop: () => killTree(proc.pid),
  };
}

export function killTree(pid) {
  try {
    execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
  } catch {
    // Already gone.
  }
}

/** A port nothing is listening on right now, so two apps never collide on a launch profile's. */
export function freePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });
}

export const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

/** The last `lines` lines of `text`: the part of a build log that says what went wrong. */
export function tail(text, lines = 25) {
  return text.trimEnd().split(/\r?\n/).slice(-lines).join('\n');
}

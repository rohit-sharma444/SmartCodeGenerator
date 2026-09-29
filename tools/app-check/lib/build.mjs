// Steps 2 and 3: compile the generated app (building its test project builds the app too), then
// run its tests without rebuilding, so a compile error and a failing test are reported apart.
import { run, tail } from './process.mjs';

export async function build(testsProject, appProject) {
  const project = testsProject ?? appProject;
  const result = await run('dotnet', ['build', project, '--nologo', '-v', 'quiet'], { timeoutMs: 10 * 60_000 });
  const errors = [...new Set(result.out.split(/\r?\n/).filter(l => / error [A-Z]+\d+/.test(l)).map(l => l.trim()))];

  return {
    ok: result.code === 0,
    secs: result.secs,
    detail: result.code === 0 ? 'compiled' : (errors.slice(0, 8).join('\n') || tail(result.out)),
  };
}

export async function test(testsProject) {
  if (!testsProject) return { ok: false, secs: 0, detail: 'no test project was generated' };

  // Not -v quiet: that also drops the names of the tests that failed, and why.
  const result = await run('dotnet', ['test', testsProject, '--no-build', '--nologo'], { timeoutMs: 10 * 60_000 });
  const summary = /(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+)/.exec(result.out);

  return {
    ok: result.code === 0,
    secs: result.secs,
    detail: summary
      ? `${summary[3]} passed, ${summary[2]} failed` + failures(result.out)
      : tail(result.out),
  };
}

/** Each failed test's name and the first line of its error message. */
function failures(out) {
  const lines = out.split(/\r?\n/);
  const found = [];

  lines.forEach((line, i) => {
    const name = /^\s*Failed (\S+)/.exec(line)?.[1];
    if (!name) return;

    const at = lines.findIndex((l, j) => j > i && /Error Message:/.test(l));
    const message = at > i && at < i + 4 ? lines[at + 1]?.trim() : '';
    found.push(`${name.split('.').slice(-2).join('.')}: ${message}`);
  });

  return found.length ? '\n' + found.slice(0, 10).join('\n') : '';
}

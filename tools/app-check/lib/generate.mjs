// Step 1: run `build app:` through the real CLI, piped - the same path a script would drive, no
// browser workflow and no wizard - and find what it wrote.
import fs from 'fs';
import path from 'path';
import { run, tail } from './process.mjs';

/**
 * Pipes `command` then `approve <target>` into the CLI, in a workspace of its own. A piped run
 * treats end-of-input as cancel, so the approve line is what makes it generate at all.
 */
export async function generate({ cli, command, target, workspace }) {
  fs.mkdirSync(workspace, { recursive: true });

  // The CLI resolves its workspace from PROJECTPATH; without it, it climbs from the current
  // directory to the nearest project folder and could write into this repository.
  const result = await run(cli, [], {
    cwd: workspace,
    env: { PROJECTPATH: workspace, CG_NO_WEB: '1' },
    input: `${command}\napprove ${target}\n`,
    timeoutMs: 20 * 60_000,
  });

  const log = result.out;
  const generated = /Generated project at (.+)/.exec(log)?.[1]?.trim();
  const tests = /Generated test project at (.+\.csproj)/.exec(log)?.[1]?.trim();

  if (result.timedOut)
    return { ok: false, secs: result.secs, detail: 'timed out after 20 minutes', log };

  if (!generated || !fs.existsSync(generated))
    return { ok: false, secs: result.secs, detail: 'no project was generated\n' + tail(log), log };

  const app = describeApp(generated);

  return {
    ok: true,
    secs: result.secs,
    detail: `${app.classes.length} class(es): ${app.classes.join(', ')}`,
    log,
    source: discoverySource(log),
    app,
    testsProject: tests && fs.existsSync(tests) ? tests : null,
  };
}

/** What the rest of the checks need to know about a generated project, read from disk. */
function describeApp(dir) {
  const csproj = fs.readdirSync(dir).find(f => f.endsWith('.csproj'));
  const models = path.join(dir, 'Models');
  const hasLogin = fs.existsSync(path.join(dir, 'Auth'));

  // User is the login's own account class, present in every login app whatever the domain, so
  // it says nothing about what the prompt asked for.
  const classes = fs.existsSync(models)
    ? fs.readdirSync(models)
        .filter(f => f.endsWith('.cs'))
        .map(f => path.basename(f, '.cs'))
        .filter(name => !(hasLogin && name === 'User'))
    : [];

  return {
    dir,
    csproj: csproj ? path.join(dir, csproj) : null,
    name: csproj ? path.basename(csproj, '.csproj') : path.basename(dir),
    classes,
    hasLogin,
  };
}

/** The CLI's own "Classes from qwen2.5:3b (Ollama)." line: who answered discovery. A data-file
 * build has none - its classes come from the file. */
function discoverySource(log) {
  return /^Classes from .+$/m.exec(log)?.[0]?.trim() ?? null;
}

/**
 * How many of the expected classes were found. Each expectation is a list of names any one of
 * which counts; a found class matches a name when it IS that name or ENDS with it, so
 * "LibraryBook" satisfies "Book" but "Bookshelf" does not.
 */
export function scoreClasses(found, expect) {
  const lower = found.map(f => f.toLowerCase());
  const missing = [];
  let hits = 0;

  for (const names of expect) {
    const hit = names.some(n => lower.some(f => f === n.toLowerCase() || f.endsWith(n.toLowerCase())));
    if (hit) hits++;
    else missing.push(names[0]);
  }

  return { hits, total: expect.length, missing, ok: missing.length === 0 };
}

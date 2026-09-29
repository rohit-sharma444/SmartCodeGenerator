// app-check: generate an app from every prompt in prompts.json, build it, test it, launch it and
// use it, then write results.json and report.html for the run. See README.md.
//
//   node check.mjs [--only <text>] [--no-launch] [--runs <folder>]
//
// --only keeps just the cases whose id contains <text> ("sql", "blazor", "clinic-wpf").
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import { generate, scoreClasses } from './lib/generate.mjs';
import { build, test } from './lib/build.mjs';
import { launch } from './lib/launch.mjs';
import { writeReport } from './lib/report.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');
const cli = path.join(repo, 'CodeGenerator', 'bin', 'Release', 'net10.0', 'CodeGenerator.exe');

const args = process.argv.slice(2);
const option = name => {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : undefined;
};

const only = option('--only');
const noLaunch = args.includes('--no-launch');
// Short on purpose: a generated project under a long path hits MAX_PATH during its build.
const runsRoot = option('--runs') ?? 'C:\\ac\\runs';

if (!fs.existsSync(cli)) {
  console.error(`The CLI is not built: ${cli}\nRun run.ps1, which builds it first.`);
  process.exit(2);
}

const prompts = JSON.parse(fs.readFileSync(path.join(here, 'prompts.json'), 'utf8'));
const expand = command => command.replaceAll('{repo}', repo);

const cases = [
  ...prompts.data.flatMap(p => prompts.targets.map(target => ({
    id: `${p.id}-${target}`, kind: 'data', prompt: p.id, command: expand(p.command), target,
  }))),
  ...prompts.description.map(p => ({
    id: `${p.id}-${p.target}`, kind: 'description', prompt: p.id, command: expand(p.command), target: p.target, expect: p.expect,
  })),
].filter(c => !only || c.id.includes(only));

// Local time, so a run's folder name matches the clock of the person reading the report.
const now = new Date();
const pad = n => String(n).padStart(2, '0');
const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}`;
const runDir = path.join(runsRoot, stamp);
fs.mkdirSync(runDir, { recursive: true });

const previous = previousRun(runsRoot, stamp);
const run = { id: stamp, started: new Date().toISOString(), finished: null, only: only ?? null, noLaunch, cases: [] };

console.log(`app-check ${stamp}: ${cases.length} case(s) -> ${runDir}`);

for (const c of cases) {
  const caseDir = path.join(runDir, c.id);
  const result = { ...c, steps: {}, ok: false };
  run.cases.push(result);

  const gen = await generate({ cli, command: c.command, target: c.target, workspace: path.join(caseDir, 'ws') });
  fs.writeFileSync(path.join(caseDir, 'cli.log'), gen.log ?? '');
  result.steps.generate = { ok: gen.ok, secs: gen.secs, detail: gen.detail };
  result.source = gen.source ?? null;

  if (gen.ok) {
    result.classes = gen.app.classes;

    if (c.expect) {
      const score = scoreClasses(gen.app.classes, c.expect);
      result.steps.classes = {
        ok: score.ok,
        secs: 0,
        detail: `${score.hits} of ${score.total} expected` + (score.missing.length ? ` - missed ${score.missing.join(', ')}` : ''),
      };
    }

    result.steps.build = await build(gen.testsProject, gen.app.csproj);

    if (result.steps.build.ok) {
      result.steps.tests = await test(gen.testsProject);
      if (!noLaunch) result.steps.launch = await launch(c.target, gen.app, caseDir);
    }
  }

  if (result.steps.launch?.screenshot)
    result.steps.launch.screenshot = path.relative(runDir, result.steps.launch.screenshot).replaceAll('\\', '/');

  const expected = ['generate', 'build', 'tests', ...(noLaunch ? [] : ['launch']), ...(c.expect ? ['classes'] : [])];
  result.ok = expected.every(s => result.steps[s]?.ok);

  const marks = expected.map(s => `${s} ${result.steps[s] ? (result.steps[s].ok ? 'ok' : 'FAIL') : '-'}`).join('  ');
  console.log(`${result.ok ? 'PASS' : 'FAIL'}  ${c.id.padEnd(24)} ${marks}`);

  // After every case, not just at the end: a long run killed halfway still leaves a report.
  save();
}

run.finished = new Date().toISOString();
save();

const passed = run.cases.filter(c => c.ok).length;
console.log(`\n${passed} of ${run.cases.length} fully working.  Report: ${path.join(runDir, 'report.html')}`);

function save() {
  fs.writeFileSync(path.join(runDir, 'results.json'), JSON.stringify(run, null, 2));
  writeReport(run, previous, path.join(runDir, 'report.html'));
}

/** The most recent earlier run that got as far as writing results, to compare this one against. */
function previousRun(root, current) {
  const earlier = fs.readdirSync(root)
    .filter(d => d < current && fs.existsSync(path.join(root, d, 'results.json')))
    .sort();

  for (const dir of earlier.reverse()) {
    try {
      return JSON.parse(fs.readFileSync(path.join(root, dir, 'results.json'), 'utf8'));
    } catch {
      // A run killed mid-write; try the one before it.
    }
  }

  return null;
}

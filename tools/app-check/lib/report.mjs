// report.html: one row per app, one cell per step, the failures spelled out underneath, and the
// headline compared with the previous run. Self-contained - no scripts, no server - so it opens
// straight from disk.
import fs from 'fs';

const STEPS = [
  ['generate', 'Generated'],
  ['fields', 'No own-key field'],
  ['build', 'Compiles'],
  ['tests', 'Tests pass'],
  ['launch', 'Runs'],
  ['classes', 'Right classes'],
];

export function writeReport(run, previous, file) {
  fs.writeFileSync(file, render(run, previous));
}

function render(run, previous) {
  const total = run.cases.length;
  const passed = run.cases.filter(c => c.ok).length;
  const before = previous ? previous.cases.filter(c => c.ok).length : null;
  const beforeById = new Map((previous?.cases ?? []).map(c => [c.id, c]));

  const steps = STEPS.filter(([key]) => run.cases.some(c => c.steps[key]));
  const failures = run.cases.filter(c => !c.ok);

  const trend = previous
    ? `Previous run (${esc(previous.id)}): ${before} of ${previous.cases.length}`
    : 'No previous run to compare with';

  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>App check ${esc(run.id)}</title>
<style>
  :root {
    --bg: #f6f7f9; --panel: #ffffff; --text: #1c2330; --muted: #5c6675; --line: #dfe3ea;
    --ok: #1a7f45; --ok-bg: #e5f5ec; --bad: #b42318; --bad-bg: #fdecea; --none: #8a93a2;
    --accent: #1d4ed8;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg: #12151b; --panel: #1b2028; --text: #e6e9ef; --muted: #9aa3b2; --line: #2c333f;
      --ok: #4ac383; --ok-bg: #16301f; --bad: #f47067; --bad-bg: #3a1c1b; --none: #6b7482;
      --accent: #7aa2ff;
    }
  }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--text);
         font: 14px/1.5 "Segoe UI", system-ui, sans-serif; }
  main { max-width: 1180px; margin: 0 auto; padding: 24px 16px 64px; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  h2 { font-size: 16px; margin: 32px 0 12px; }
  .sub { color: var(--muted); margin: 0 0 20px; }
  .headline { display: flex; gap: 12px; flex-wrap: wrap; margin-bottom: 8px; }
  .tile { background: var(--panel); border: 1px solid var(--line); border-radius: 8px; padding: 12px 16px; min-width: 180px; }
  .tile b { display: block; font-size: 26px; line-height: 1.2; }
  .tile span { color: var(--muted); font-size: 13px; }
  .wrap { overflow-x: auto; background: var(--panel); border: 1px solid var(--line); border-radius: 8px; }
  table { border-collapse: collapse; width: 100%; }
  th, td { padding: 8px 10px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: top; }
  th { font-size: 12px; color: var(--muted); font-weight: 600; white-space: nowrap; }
  tr:last-child td { border-bottom: none; }
  td.id { font-weight: 600; white-space: nowrap; }
  .cell { display: inline-block; min-width: 22px; text-align: center; border-radius: 4px; font-weight: 700; }
  .ok { color: var(--ok); background: var(--ok-bg); }
  .bad { color: var(--bad); background: var(--bad-bg); }
  .none { color: var(--none); }
  .detail { display: block; color: var(--muted); font-size: 12px; max-width: 260px; }
  .change { font-size: 12px; font-weight: 600; }
  .fail { background: var(--panel); border: 1px solid var(--line); border-left: 4px solid var(--bad);
          border-radius: 8px; padding: 12px 16px; margin-bottom: 12px; }
  .fail h3 { margin: 0 0 6px; font-size: 14px; }
  .fail code { font-size: 12px; color: var(--muted); word-break: break-all; }
  pre { background: var(--bg); border: 1px solid var(--line); border-radius: 6px; padding: 8px 10px;
        white-space: pre-wrap; word-break: break-word; font-size: 12px; margin: 6px 0 0; }
  .shot { display: block; margin-top: 8px; max-width: 100%; width: 520px; border: 1px solid var(--line); border-radius: 6px; }
  a { color: var(--accent); }
</style>
</head>
<body>
<main>
  <h1>App check</h1>
  <p class="sub">Run ${esc(run.id)}${run.only ? ` &middot; only “${esc(run.only)}”` : ''}${run.noLaunch ? ' &middot; launch skipped' : ''}${run.finished ? '' : ' &middot; <b>still running</b>'}</p>

  <div class="headline">
    <div class="tile"><b>${passed} of ${total}</b><span>apps fully working</span></div>
    <div class="tile"><b>${before === null ? '&ndash;' : signed(passed - before)}</b><span>${trend}</span></div>
    <div class="tile"><b>${failures.length}</b><span>apps with a failure</span></div>
  </div>

  <h2>Every app</h2>
  <div class="wrap">
  <table>
    <thead><tr><th>App</th><th>Prompt</th><th>Target</th>${steps.map(([, label]) => `<th>${label}</th>`).join('')}<th>vs previous</th></tr></thead>
    <tbody>
${run.cases.map(c => row(c, steps, beforeById.get(c.id))).join('\n')}
    </tbody>
  </table>
  </div>

  <h2>What failed</h2>
  ${failures.length ? failures.map(failure).join('\n') : '<p class="sub">Nothing. Every app generated, compiled, passed its tests and ran.</p>'}
</main>
</body>
</html>
`;
}

function row(c, steps, before) {
  const cells = steps.map(([key]) => {
    const step = c.steps[key];
    if (!step) return '<td><span class="cell none">&ndash;</span></td>';
    const detail = key === 'classes' || key === 'tests' || !step.ok ? `<span class="detail">${esc(firstLine(step.detail))}</span>` : '';
    return `<td><span class="cell ${step.ok ? 'ok' : 'bad'}">${step.ok ? '&#10003;' : '&#10007;'}</span>${detail}</td>`;
  }).join('');

  let change = '';
  if (before && before.ok !== c.ok) change = c.ok ? '<span class="change" style="color:var(--ok)">fixed</span>' : '<span class="change" style="color:var(--bad)">broke</span>';
  else if (!before) change = '<span class="detail">new</span>';

  return `      <tr><td class="id">${esc(c.id)}</td><td>${esc(c.command.replace(/^build app:\s*/, '').slice(0, 70))}</td><td>${esc(c.target)}</td>${cells}<td>${change}</td></tr>`;
}

function failure(c) {
  const parts = [];

  for (const [key, label] of STEPS) {
    const step = c.steps[key];
    if (!step || step.ok) continue;

    if (key === 'launch' && step.checks) {
      for (const check of step.checks.filter(k => !k.ok))
        parts.push(`<div><b>${label}:</b> ${esc(check.name)}${check.detail ? `<pre>${esc(check.detail)}</pre>` : ''}</div>`);
    } else {
      parts.push(`<div><b>${label}:</b><pre>${esc(step.detail)}</pre></div>`);
    }
  }

  const shot = c.steps.launch?.screenshot ? `<a href="${esc(c.steps.launch.screenshot)}"><img class="shot" src="${esc(c.steps.launch.screenshot)}" alt="The ${esc(c.id)} app's screen when it was checked"></a>` : '';

  return `  <div class="fail">
    <h3>${esc(c.id)}</h3>
    <code>${esc(c.command)}</code>
    ${c.classes ? `<div class="detail" style="max-width:none">Classes: ${esc(c.classes.join(', ') || 'none')}${c.source ? ` &middot; ${esc(c.source)}` : ''}</div>` : ''}
    ${parts.join('\n    ')}
    ${shot}
    <div class="detail" style="max-width:none"><a href="${esc(c.id)}/cli.log">CLI output</a></div>
  </div>`;
}

const firstLine = text => String(text ?? '').split('\n')[0].slice(0, 120);
const signed = n => (n > 0 ? `+${n}` : String(n));

function esc(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

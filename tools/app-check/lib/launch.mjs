// Step 4: start the generated app and use it the way a person would on first run - sign in, open
// a screen, see the seeded records, press New then Save - because the defects that matter most
// have been ones every generated test passed and only a RUNNING app showed.
//
// Each check returns { ok, secs, checks: [{ name, ok, detail }], screenshot? }. A launch step is
// ok only when every one of its checks is.
import fs from 'fs';
import path from 'path';
import { createRequire } from 'module';
import { start, run, freePort, sleep } from './process.mjs';
import { startDriver } from '../../demo-video/wpf-driver-client.mjs';

const require = createRequire(import.meta.url);

export async function launch(target, app, caseDir) {
  const started = Date.now();
  const checks = [];
  const check = (name, ok, detail = '') => {
    checks.push({ name, ok: Boolean(ok), detail: String(detail ?? '') });
    return Boolean(ok);
  };

  let screenshot = null;

  try {
    switch (target) {
      case 'wpf':
      case 'winforms':
        screenshot = await desktop(target, app, caseDir, check);
        break;
      case 'blazor':
        screenshot = await blazor(app, caseDir, check);
        break;
      case 'webapi':
        await webapi(app, check);
        break;
      case 'worker':
        await worker(app, check);
        break;
      case 'console':
        await consoleApp(app, check);
        break;
      default:
        check('nothing to launch', true, 'a class library has no entry point');
    }
  } catch (error) {
    check('launch check ran to the end', false, error.stack ?? error.message);
  }

  return {
    ok: checks.every(c => c.ok),
    secs: (Date.now() - started) / 1000,
    detail: checks.filter(c => !c.ok).map(c => c.name).join(', ') || `${checks.length} check(s) passed`,
    checks,
    screenshot,
  };
}

/** The built executable, wherever its target framework put it. */
function builtExe(app) {
  const debug = path.join(app.dir, 'bin', 'Debug');
  if (!fs.existsSync(debug)) return null;

  for (const tfm of fs.readdirSync(debug)) {
    const exe = path.join(debug, tfm, `${app.name}.exe`);
    if (fs.existsSync(exe)) return exe;
  }

  return null;
}

// ------------------------------------------------------------------ WPF and WinForms

async function desktop(target, app, caseDir, check) {
  const exe = builtExe(app);
  if (!check('executable exists', exe, exe ?? 'no .exe under bin\\Debug')) return null;

  const proc = start(exe, [], { cwd: path.dirname(exe) });
  const driver = startDriver();
  const shot = path.join(caseDir, 'screen.png');
  const send = command => driver.send(command);

  try {
    await sleep(1500);

    const attached = await send({ cmd: 'attach', pid: proc.pid });
    if (!check('window opens', attached.ok, attached.error ?? attached.title)) return null;

    let names = await visibleNames(send);

    const wpfLogin = names.has('UserNameBox');
    const formsLogin = names.has('User name') && names.has('Password');

    if (wpfLogin || formsLogin) {
      const user = wpfLogin ? { automationId: 'UserNameBox' } : { name: 'User name' };
      const password = wpfLogin ? { automationId: 'PasswordBox' } : { name: 'Password' };

      const signIn = async () => {
        await send({ cmd: 'type', ...user, value: 'admin', direct: true });
        await send({ cmd: 'type', ...password, value: 'admin', direct: true });
        await send({ cmd: 'click-button', name: 'Sign in' });
        return send({ cmd: 'wait-for-window' });
      };

      // Set directly, not typed: Windows will not give this process the foreground, and when
      // another window held the keyboard - a terminal, once, in a real run - the typed "admin"
      // went there and the login refused a password it never saw. Once more if refused anyway:
      // a login that is really broken refuses both times, and a needed retry is reported.
      let shell = await signIn();
      let retried = false;

      if (!shell.ok) {
        retried = true;
        shell = await signIn();
      }

      const refusal = shell.ok ? '' : [...(await visibleNames(send))].find(n => /not recognised|invalid|incorrect/i.test(n));
      if (!check('signs in as admin / admin', shell.ok,
        shell.ok ? (retried ? 'signed in on the second try' : '') : `refused twice: ${refusal ?? shell.error}`)) return null;

      await sleep(1000);
      names = await visibleNames(send);
    }

    const entity = app.classes.find(c => names.has(c));
    if (!check('menu lists an entity', entity, entity ?? `none of ${app.classes.join(', ')} is on screen`)) return null;

    // Opened through UI Automation, not the mouse: a real mouse click lands on whatever window is
    // on top, and a browser someone was watching took this click once - the app never opened the
    // screen, and the list below was reported missing. The driver says which way it went; a mouse
    // click there is refused when another window covers the app, rather than clicking into it.
    const opened = await send({ cmd: 'click', name: entity, direct: true });
    if (!check(`opens ${entity}`, opened.ok, opened.ok ? (opened.via === 'mouse' ? 'by mouse click' : '') : opened.error)) return null;

    await sleep(1500);

    // Drawn by the window itself, so a covered app still shows what it showed, not what covered it.
    await send({ cmd: 'screenshot', path: shot });

    const grid = await send({ cmd: 'find-grid' });
    check('list shows the seeded records', grid.ok && grid.rowCount > 0,
      grid.ok ? `${grid.rowCount} row(s) visible` : `${grid.error} - the record list is not on screen`);

    const created = await send({ cmd: 'click-button', name: 'New' });
    if (check('New button works', created.ok, created.error)) {
      await send({ cmd: 'click-button', name: 'Save' });
      const status = await send({ cmd: 'read-status' });
      check('Save gives an answer', status.ok, status.ok ? status.text : status.error);
    }

    await sleep(500);
    check('app is still running', !proc.exited(), proc.exited() ? `exited with code ${proc.exitCode()}` : '');

    return fs.existsSync(shot) ? shot : null;
  } finally {
    await driver.stop();
    proc.stop();
  }
}

/** Every name and AutomationId on the current window, for "is X on screen" questions. */
async function visibleNames(send) {
  const dump = await send({ cmd: 'dump' });
  const names = new Set();

  for (const element of dump.elements ?? []) {
    if (element.name) names.add(element.name);
    if (element.automationId) names.add(element.automationId);
  }

  return names;
}

// ------------------------------------------------------------------ Blazor

/** Starts a web app the way a person would - `dotnet run`, so its launch profile's Development
 * environment serves the framework's own scripts - on a port of our choosing. */
async function startWeb(app, check) {
  const port = await freePort();
  const url = `http://127.0.0.1:${port}`;
  const proc = start('dotnet', ['run', '--no-build', '--', '--urls', url], { cwd: app.dir });
  const deadline = Date.now() + 90_000;

  while (Date.now() < deadline && !proc.exited()) {
    try {
      await fetch(url, { redirect: 'manual' });
      check('server starts', true, url);
      return { proc, url };
    } catch {
      await sleep(500);
    }
  }

  check('server starts', false, proc.exited() ? `exited with code ${proc.exitCode()}\n${proc.output().slice(-2000)}` : 'no answer in 90s');
  proc.stop();
  return null;
}

async function blazor(app, caseDir, check) {
  const web = await startWeb(app, check);
  if (!web) return null;

  const { chromium } = require('playwright');
  const browser = await chromium.launch({ channel: 'chromium' });
  const shot = path.join(caseDir, 'screen.png');

  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    page.on('console', m => m.type() === 'error' && errors.push(m.text()));
    page.on('response', r => r.status() >= 500 && errors.push(`${r.status()} ${r.url()}`));

    if (app.hasLogin) {
      await page.goto(`${web.url}/login`);
      await page.fill('#userName', 'admin');
      await page.fill('#password', 'admin');
      await page.click('button[type=submit]');
      const signedIn = await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 15_000 }).then(() => true, () => false);
      if (!check('signs in as admin / admin', signedIn, signedIn ? '' : await page.locator('.alert').allTextContents())) return null;
    }

    const entity = app.classes.find(c => fs.existsSync(path.join(app.dir, 'Components', 'Pages', `${c}Page.razor`)));
    if (!check('an entity has a page', entity, entity ?? 'no <Entity>Page.razor')) return null;

    const route = /@page\s+"([^"]+)"/.exec(fs.readFileSync(path.join(app.dir, 'Components', 'Pages', `${entity}Page.razor`), 'utf8'))?.[1];
    await page.goto(web.url + route);
    await page.waitForLoadState('networkidle');

    const hasTable = await page.waitForSelector('table', { timeout: 15_000 }).then(() => true, () => false);
    const rows = hasTable ? await page.locator('table tbody tr').count() : 0;
    check(`opens ${entity}`, hasTable, hasTable ? route : 'no table on the page');
    check('list shows the seeded records', rows > 0, `${rows} row(s)`);

    // The page is prerendered first; a click before its circuit connects lands on a button with
    // no handler yet and does nothing. Networkidle plus a short settle covers the connect.
    await page.waitForTimeout(2000);
    await page.click('button:has-text("New")');
    const opened = await page.waitForURL(u => String(u).includes('/new'), { timeout: 15_000 }).then(() => true, () => false);

    if (check('New opens the editor', opened, opened ? page.url() : `still on ${page.url()}`)) {
      await page.waitForTimeout(1000);
      await page.click('button:has-text("Save")');
      await page.waitForTimeout(2000);
      const status = (await page.locator('[role=status]').allTextContents()).join(' ').trim();
      const left = !page.url().includes('/new');
      check('Save gives an answer', left || status, left ? 'saved and returned to the list' : status);
    }

    await page.screenshot({ path: shot });
    check('no errors in the browser', errors.length === 0, errors.slice(0, 5).join('\n'));
    return shot;
  } finally {
    await browser.close();
    web.proc.stop();
  }
}

// ------------------------------------------------------------------ Web API, Worker, Console

async function webapi(app, check) {
  const program = fs.readFileSync(path.join(app.dir, 'Program.cs'), 'utf8');
  const routes = [...program.matchAll(/MapEntity<\w+>\(app,\s*"([^"]+)"/g)].map(m => m[1]);
  if (!check('routes are mapped', routes.length, routes.join(', ') || 'no MapEntity calls in Program.cs')) return;

  const web = await startWeb(app, check);
  if (!web) return;

  try {
    for (const route of routes) {
      const response = await fetch(`${web.url}/${route}`);
      const body = response.ok ? await response.json().catch(() => null) : null;
      const count = Array.isArray(body) ? body.length : null;
      check(`GET /${route} returns records`, response.ok && count > 0, `HTTP ${response.status}, ${count ?? 'no'} record(s)`);
    }
  } finally {
    web.proc.stop();
  }
}

async function worker(app, check) {
  const proc = start('dotnet', ['run', '--no-build'], { cwd: app.dir });

  try {
    await sleep(8000);
    const out = proc.output();
    const failure = out.split(/\r?\n/).find(l => /^(fail|crit):|Unhandled exception/.test(l.trim()));
    check('runs for 8 seconds without stopping', !proc.exited(), proc.exited() ? `exited with code ${proc.exitCode()}\n${out.slice(-1500)}` : '');
    check('logs no errors', !failure, failure ?? '');
  } finally {
    proc.stop();
  }
}

async function consoleApp(app, check) {
  const exe = builtExe(app);
  if (!check('executable exists', exe, exe ?? 'no .exe under bin\\Debug')) return;

  // "1" opens the first entity's menu; the rest walk back out. Whatever that menu prints, it
  // must not crash, and it must end when told to.
  const result = await run(exe, [], { cwd: path.dirname(exe), input: '1\n0\n0\n', timeoutMs: 30_000 });
  const missing = app.classes.filter(c => !result.out.includes(c));
  check('shows the entity menu', result.out.includes('Entities:') && missing.length === 0,
    missing.length ? `not listed: ${missing.join(', ')}` : '');
  check('exits cleanly when told to', result.code === 0, result.timedOut ? 'still running after 30s' : `exit code ${result.code}\n${result.out.slice(-800)}`);
}

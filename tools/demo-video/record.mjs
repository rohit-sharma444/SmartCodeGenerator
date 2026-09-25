// Records the build app demo: one real workflow run, from a CSV to a running Blazor app.
//
// Drives the page the DemoVideoHost test serves, scene by scene. Each scene shows its narration's
// captions at the times the voice says them and waits until its line has been spoken, so the
// picture never runs ahead of the voice. Writes out/raw.webm and out/scenes.json, which
// assemble.py turns into the finished video.
//
// Every scene starts with a SYNC MARKER: a few magenta pixels in the top-left corner for a moment.
// The recorder's own clock cannot place the narration: under load - the build, above all - the
// recording falls seconds behind real time, and a voice placed by the clock ran ahead of the
// picture by seven seconds by the end. assemble.py finds the markers in the video itself and puts
// each line where its scene actually appears. Cyan and yellow markers bracket the build wait,
// which is played faster.
//
// Usage: node record.mjs <workspace folder the host is serving>

import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, 'out');
const workspace = process.argv[2] ?? 'C:\\dv';
const workflowUrl = fs.readFileSync(path.join(workspace, 'url.txt'), 'utf8').trim();
const narration = JSON.parse(fs.readFileSync(path.join(here, 'narration.json'), 'utf8'));
const voice = id => JSON.parse(fs.readFileSync(path.join(out, 'voice', `${id}.json`), 'utf8'));

const size = { width: 1280, height: 720 };
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

fs.mkdirSync(path.join(out, 'video'), { recursive: true });

const browser = await chromium.launch({ channel: 'chromium' });
const context = await browser.newContext({
  viewport: size,
  recordVideo: { dir: path.join(out, 'video'), size },
});

// The caption bar and a pointer, on every page the video visits. A recorded page has no cursor
// of its own, and a click nobody can see reads as the page changing by itself.
await context.addInitScript(() => {
  const install = () => {
    if (document.getElementById('demo-caption')) return;
    const style = document.createElement('style');
    style.textContent = `
      #demo-caption { position: fixed; left: 50%; bottom: 28px; transform: translateX(-50%);
        max-width: 1000px; padding: 10px 22px; border-radius: 10px; z-index: 2147483647;
        background: rgba(10, 14, 20, 0.82); color: #fff; font: 500 22px/1.35 "Segoe UI", sans-serif;
        text-align: center; pointer-events: none; transition: opacity .2s; }
      #demo-caption:empty { opacity: 0; }
      #demo-pointer { position: fixed; left: 0; top: 0; width: 22px; height: 22px; margin: -11px 0 0 -11px;
        border-radius: 50%; background: rgba(255, 196, 0, .55); border: 2px solid #f5a300;
        z-index: 2147483646; pointer-events: none; transition: transform .55s cubic-bezier(.3,.7,.2,1);
        transform: translate(640px, 360px); }
      #demo-pointer.press { box-shadow: 0 0 0 10px rgba(245, 163, 0, .25); }
      /* Room at the foot of every page, so its last lines can be scrolled above the caption bar:
         the refused-save message sat behind it, at the bottom of a form that scrolled no further. */
      body::after { content: ""; display: block; height: 150px; }
      #demo-sync { position: fixed; left: 0; top: 0; width: 14px; height: 14px; z-index: 2147483647;
        pointer-events: none; display: none; }
      #demo-badge { position: fixed; right: 24px; top: 20px; z-index: 2147483647; display: none;
        padding: 8px 16px; border-radius: 999px; background: #f5a300; color: #111;
        font: 600 18px/1 "Segoe UI", sans-serif; pointer-events: none; }`;
    document.documentElement.appendChild(style);
    const caption = document.createElement('div');
    caption.id = 'demo-caption';
    const pointer = document.createElement('div');
    pointer.id = 'demo-pointer';
    const sync = document.createElement('div');
    sync.id = 'demo-sync';
    const badge = document.createElement('div');
    badge.id = 'demo-badge';
    document.documentElement.append(caption, pointer, sync, badge);
    const last = JSON.parse(sessionStorage.getItem('demo-pointer') ?? '[640,360]');
    pointer.style.transform = `translate(${last[0]}px, ${last[1]}px)`;
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', install);
  else install();
  window.__install = install;
  window.__caption = text => { const c = document.getElementById('demo-caption'); if (c) c.textContent = text; };
  window.__sync = color => {
    const m = document.getElementById('demo-sync');
    if (!m) return;
    m.style.background = color ?? 'transparent';
    m.style.display = color ? 'block' : 'none';
  };
  window.__badge = text => {
    const b = document.getElementById('demo-badge');
    if (!b) return;
    b.textContent = text ?? '';
    b.style.display = text ? 'block' : 'none';
  };
  window.__point = (x, y, press) => {
    const p = document.getElementById('demo-pointer');
    if (!p) return;
    p.style.transform = `translate(${x}px, ${y}px)`;
    p.classList.toggle('press', !!press);
    sessionStorage.setItem('demo-pointer', JSON.stringify([x, y]));
  };
});

const page = await context.newPage();
const videoStart = Date.now();
const scenes = [];
let currentCaption = '';

async function caption(text) {
  currentCaption = text;
  await page.evaluate(t => window.__caption?.(t), text).catch(() => {});
}

// A navigation wipes the page, so the caption on screen is put back on the new one.
page.on('load', () => page.evaluate(t => window.__caption?.(t), currentCaption).catch(() => {}));

/** Moves the pointer to the element, pauses so the eye can follow, then clicks it. */
async function click(locator, { settle = 700 } = {}) {
  await locator.scrollIntoViewIfNeeded();
  const box = await locator.boundingBox();
  if (box) {
    await page.evaluate(([x, y]) => window.__point?.(x, y, false), [box.x + box.width / 2, box.y + box.height / 2]);
    await sleep(650);
    await page.evaluate(([x, y]) => window.__point?.(x, y, true), [box.x + box.width / 2, box.y + box.height / 2]);
  }
  await locator.click();
  await sleep(250);
  if (box) await page.evaluate(([x, y]) => window.__point?.(x, y, false), [box.x + box.width / 2, box.y + box.height / 2]);
  await sleep(settle);
}

/** Types into a field a character at a time, the way a person would. */
async function type(locator, text) {
  await click(locator, { settle: 200 });
  await locator.fill('');
  await locator.pressSequentially(text, { delay: 55 });
  await locator.press('Tab');
  await sleep(400);
}

async function scrollTo(locator) {
  await locator.evaluate(e => e.scrollIntoView({ behavior: 'smooth', block: 'center' }));
  await sleep(900);
}

const continueButton = () => page.locator('button.stage-continue');
const node = name => page.locator('.stage-node', { hasText: name }).locator('button.node-open').first();

/** Shows a sync marker for long enough that the recording is sure to hold a frame of it. */
async function mark(color) {
  await page.evaluate(c => window.__sync?.(c), color).catch(() => {});
  await sleep(450);
  await page.evaluate(() => window.__sync?.(null)).catch(() => {});
}

const Marker = { scene: '#ff00ff', fastStart: '#00ffff', fastEnd: '#ffff00' };

/**
 * One scene: its marker, its captions on their schedule, its actions alongside, and nothing moving
 * on until both are done and the line has been spoken.
 */
async function scene(id, actions = async () => {}) {
  const spoken = voice(id);
  const start = Date.now();
  scenes.push({ id, start: (start - videoStart) / 1000 });
  const marked = mark(Marker.scene);

  const captions = (async () => {
    for (const line of spoken.captions) {
      const wait = start + line.start * 1000 - Date.now();
      if (wait > 0) await sleep(wait);
      await caption(line.text);
    }
  })();

  await Promise.all([marked, captions, actions()]);

  const rest = start + spoken.duration * 1000 + 600 - Date.now();
  if (rest > 0) await sleep(rest);
  await caption('');
  console.log(`${id.padEnd(9)} ${((Date.now() - start) / 1000).toFixed(1)}s`);
}

/**
 * Draws one of the video's own pages. setContent replaces the document the caption bar, pointer
 * and sync marker were put in, so they are put back - without them the title and closing cards
 * had no marker, and assemble.py could not place their narration.
 */
async function show(html) {
  await page.setContent(html);
  await page.evaluate(() => window.__install?.());
}

const card = (title, body) => `<!doctype html><html><head><meta charset="utf-8"><style>
  html, body { margin: 0; height: 100%; }
  body { display: grid; place-items: center; background: radial-gradient(1200px 700px at 30% 20%, #1e3a8a, #0b1020 70%);
    color: #e8eefc; font-family: "Segoe UI", sans-serif; }
  .box { text-align: center; }
  h1 { font-size: 64px; margin: 0 0 12px; letter-spacing: -1px; }
  h1 span { color: #7dd3fc; }
  p { font-size: 26px; margin: 0; color: #b6c4e3; }
  code { font-family: "Cascadia Code", Consolas, monospace; color: #fde68a; }
  </style></head><body><div class="box"><h1>${title}</h1><p>${body}</p></div></body></html>`;

const terminal = `<!doctype html><html><head><meta charset="utf-8"><style>
  html, body { margin: 0; height: 100%; background: #0b1020; }
  .win { position: absolute; inset: 60px 90px 110px; background: #0c0c0c; border-radius: 10px;
    box-shadow: 0 20px 60px rgba(0,0,0,.6); overflow: hidden; }
  .bar { height: 34px; background: #202020; color: #bbb; font: 14px "Segoe UI"; display: flex; align-items: center; padding: 0 14px; }
  pre { margin: 0; padding: 18px 22px; color: #d4d4d4; font: 19px/1.55 "Cascadia Code", Consolas, monospace; white-space: pre-wrap; }
  .p { color: #4fc1ff; } .ok { color: #89d185; } .cursor { background: #d4d4d4; }
  </style></head><body><div class="win"><div class="bar">SmartCodeGenerator</div><pre id="t"></pre></div></body></html>`;

// Whatever fails, the frame it failed on is kept: a half-written recording shows nothing.
const failed = async error => {
  console.error(error);
  console.error('failed at', page.url(), 'after scenes:', scenes.map(s => s.id).join(', '));
  await page.screenshot({ path: path.join(out, 'failed.png') }).catch(() => {});
  process.exit(1);
};
process.on('unhandledRejection', failed);
process.on('uncaughtException', failed);

// ---------------------------------------------------------------- the video

await show(card('Smart<span>Code</span>Generator', 'From a spreadsheet to a running application, with <code>build app</code>'));

await scene('intro');

await show(terminal);
await scene('console', async () => {
  const show = html => page.evaluate(h => { document.getElementById('t').innerHTML = h; }, html);
  const prompt = '<span class="p">CodeGenerator &gt;&gt;</span> ';
  const command = 'build app: an order management system from csv orders.csv';
  let typed = '';
  await sleep(1200);
  for (const ch of command) {
    typed += ch;
    await show(prompt + typed + '<span class="cursor"> </span>');
    await sleep(45);
  }
  await sleep(700);
  await show(prompt + typed + '\n  <span class="ok">Opening the build workflow at http://127.0.0.1:52613/build</span>\n  Ctrl-C here cancels.');
});

await page.goto(workflowUrl);
await page.waitForSelector('.stage-diagram');

await scene('diagram', async () => {
  await sleep(2500);
  for (const name of ['Describe', 'Data']) {
    const box = await page.locator('.stage-node', { hasText: name }).first().boundingBox();
    if (box) await page.evaluate(([x, y]) => window.__point?.(x, y, false), [box.x + box.width / 2, box.y + box.height / 2]);
    await sleep(2200);
  }
});

await scene('theme', async () => {
  const picker = page.locator('select.theme-choice');
  await sleep(1500);
  const box = await picker.boundingBox();
  if (box) await page.evaluate(([x, y]) => window.__point?.(x, y, false), [box.x + box.width / 2, box.y + box.height / 2]);
  for (const theme of ['dark', 'contrast', 'warm', 'light']) {
    await sleep(1400);
    await picker.selectOption(theme);
  }
});

await scene('apptype', async () => {
  await sleep(6500);
  await click(page.locator('label.choice', { hasText: 'Blazor' }).locator('input'));
  await sleep(1500);
  await click(continueButton());
});

await scene('data', async () => { await sleep(9000); await click(continueButton()); });

await scene('look', async () => {
  await click(page.locator('input.customize'));
  await type(page.locator('input.company'), 'Northwind Traders');
  await page.locator('select.theme').selectOption('dark');
  await sleep(800);
  await type(page.locator('input.accent'), '#2563eb');
  await scrollTo(page.locator('input.login'));
  await sleep(2500);
  await click(continueButton());
});

await scene('discover', async () => {
  await page.waitForSelector('.findings');
  await sleep(6500);
  await click(page.locator('.findings input[type=checkbox]').first());
  await sleep(3500);
  await click(continueButton());
});

await scene('review', async () => {
  await page.waitForSelector('.entity');
  await sleep(9000);
  const customer = page.locator('.entity[data-entity="Order"] input.field-name').nth(3);
  await scrollTo(customer);
  await type(customer, 'CustomerName');
  await sleep(1000);
});

await scene('rules', async () => {
  const rule = text => page.locator('.proposal-row', { hasText: text }).locator('input.proposal');
  await sleep(3500);
  for (const text of ['no two of the 24 rows share a OrderNumber', 'Status holds only',
                      'OrderDate is always on or before DeliveryDate', 'Total always equals']) {
    await scrollTo(rule(text));
    await click(rule(text));
    await sleep(1500);
  }
});

await scene('workflow', async () => {
  const order = page.locator('.workflow-order').first();
  await scrollTo(order);
  await sleep(6500);
  await click(order.locator('li', { hasText: 'Delivered' }).locator('input.final'));
  await sleep(2000);
});

await scene('output', async () => {
  await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'smooth' }));
  await sleep(700);
  await click(continueButton());
  await sleep(4500);
  const role = page.locator('tr[data-entity="Order"] select.role');
  await click(role);
  await role.selectOption('Transaction');
  await sleep(2500);
  const echo = page.locator('pre.command-echo');
  const box = await echo.boundingBox();
  if (box) await page.evaluate(([x, y]) => window.__point?.(x, y, false), [box.x + 40, box.y + box.height / 2]);
});

await scene('entry', async () => { await click(continueButton()); });

await scene('generate', async () => {
  await sleep(1500);
  await click(continueButton());
  await sleep(1500);
  await click(page.locator('button.write'));
});

// However long the build takes: the next line is only said once it has finished. Played faster in
// the video, between the two markers, with a badge saying so.
await page.evaluate(() => window.__badge?.('Building - sped up 5x'));
await mark(Marker.fastStart);
await page.waitForFunction(
  () => /compiled|did not compile|Build succeeded|Build FAILED/i.test(document.querySelector('.panel')?.innerText ?? ''),
  null, { timeout: 300_000 });
await sleep(1000);
await page.evaluate(() => window.__badge?.(null));
await mark(Marker.fastEnd);
await sleep(1000);

let appUrl = null;
await scene('run', async () => {
  await click(continueButton());
  await sleep(800);
  await click(page.locator('button.run'));
  for (let i = 0; i < 120 && !appUrl; i++) {
    await sleep(1000);
    const lines = await page.$$eval('.report-line', e => e.map(x => x.textContent.trim()));
    const opened = lines.find(l => l.startsWith('Opened '));
    if (opened) appUrl = opened.slice('Opened '.length);
  }
});

if (!appUrl) throw new Error('The app did not report where it was listening.');

await page.goto(appUrl);
await page.waitForSelector('#userName', { timeout: 60_000 });

await scene('signin', async () => {
  await type(page.locator('#userName'), 'admin');
  await type(page.locator('#password'), 'admin');
  await click(page.locator('button', { hasText: 'Sign in' }));
  await page.waitForSelector('a.nav-link[href="order"], a.nav-link[href="/order"]', { timeout: 30_000 });
});

await scene('app', async () => {
  await sleep(1500);
  await click(page.locator('a.nav-link[href="order"], a.nav-link[href="/order"]').first());
  await page.waitForSelector('table tbody tr');
  await sleep(2500);
  // Down the list and back, so the imported rows are seen - the table is not scrolled sideways,
  // which cut off its first columns in an earlier take.
  await page.evaluate(() => window.scrollBy({ top: 420, behavior: 'smooth' }));
  await sleep(1800);
  await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'smooth' }));
  await sleep(1200);
  await click(page.locator('button', { hasText: 'New' }));
  await page.waitForSelector('#OrderNumber');
});

// The form is filled between the two lines, so the refusal is said once it is on screen. A date box
// is SET rather than typed into: typed characters go into its day, month and year parts by the
// browser's own rules, and the first recording saved a date nobody had asked for.
const fill = async (id, value) => {
  const box = page.locator(`#${id}`);
  if ((await box.getAttribute('type')) === 'date') {
    await click(box, { settle: 200 });
    await box.fill(value);
    await sleep(500);
  } else {
    await type(box, value);
  }
};
await fill('OrderNumber', 'SO-2001');
await fill('OrderDate', '2026-09-20');
await fill('DeliveryDate', '2026-09-12');
await fill('CustomerName', 'Harbor Cafe');
await fill('CustomerCity', 'Seattle');
await fill('Status', 'Draft');
await fill('Net', '100');
await fill('TaxRate', '0.05');
await fill('Total', '105');
await click(page.locator('button', { hasText: 'Save' }));
await sleep(800);
await scene('refused', async () => {
  const message = page.getByText('must not be after', { exact: false }).first();
  if (await message.count()) {
    // Mid-screen: at the foot of the form it sat behind the caption bar.
    await message.evaluate(e => e.scrollIntoView({ behavior: 'smooth', block: 'center' }));
    await sleep(700);
    const box = await message.boundingBox();
    if (box) await page.evaluate(([x, y]) => window.__point?.(x, y, false), [box.x + 30, box.y + box.height / 2]);
  }
});

await scene('saved', async () => {
  await sleep(1200);
  await fill('DeliveryDate', '2026-09-26');
  await click(page.locator('button', { hasText: 'Add OrderItem' }));
  await type(page.locator('tbody input[type="text"]').first(), 'Coffee beans');
  await click(page.locator('button', { hasText: 'Save' }));
  // A saved record goes back to the list, where the new order is the last row.
  await page.waitForURL(url => /\/order\/?$/.test(new URL(url).pathname), { timeout: 30_000 });
  const added = page.locator('tbody tr', { hasText: 'SO-2001' }).first();
  await added.waitFor({ timeout: 30_000 });
  await sleep(800);
  await added.evaluate(e => e.scrollIntoView({ behavior: 'smooth', block: 'center' }));
  await sleep(1200);
  {
    const box = await added.boundingBox();
    if (box) await page.evaluate(([x, y]) => window.__point?.(x, y, false), [box.x + 120, box.y + box.height / 2]);
  }
  await sleep(2000);
});

await show(card('Smart<span>Code</span>Generator', '<code>build app</code>: describe it, point it at your data, and run it'));
await scene('wrap');

const video = page.video();
await context.close();
await browser.close();

fs.writeFileSync(path.join(out, 'scenes.json'), JSON.stringify(scenes, null, 2));
fs.copyFileSync(await video.path(), path.join(out, 'raw.webm'));
console.log('recorded', path.join(out, 'raw.webm'));

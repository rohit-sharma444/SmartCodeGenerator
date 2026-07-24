// vscode-extension/test/projectSync.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import { EditorOps, ProjectSyncCore } from '../src/projectSyncCore';

class FakeOps implements EditorOps {
  revealed: string[] = [];
  opened: { path: string; focus: boolean }[] = [];
  revealFolder(path: string): void { this.revealed.push(path); }
  openFile(path: string, focus: boolean): void { this.opened.push({ path, focus }); }
}

function core(ops: EditorOps, enabled = true) {
  return new ProjectSyncCore(ops, { enabled, maxTabs: 5 });
}

test('new project: reveals the folder once and opens csproj + entry + models, capped', () => {
  const ops = new FakeOps();
  const c = core(ops);
  const dir = 'C:/ws/ShopWeb';
  c.commandStarted();
  for (const f of [
    `${dir}/ShopWeb.csproj`,
    `${dir}/Program.cs`,
    `${dir}/Models/Customer.cs`,
    `${dir}/Models/Order.cs`,
    `${dir}/Data/JsonStore.cs`,
    `${dir}/Data/Repository.cs`,
  ]) c.fileEvent(f);
  c.commandFinished();

  assert.deepEqual(ops.revealed, [dir]);
  const openedPaths = ops.opened.map((o) => o.path);
  assert.ok(openedPaths.includes(`${dir}/ShopWeb.csproj`));
  assert.ok(openedPaths.includes(`${dir}/Program.cs`));
  assert.ok(openedPaths.includes(`${dir}/Models/Customer.cs`));
  // Data/*.cs are not "key" files — not opened.
  assert.ok(!openedPaths.includes(`${dir}/Data/JsonStore.cs`));
  assert.ok(ops.opened.length <= 5);
  // The entry point is the focused tab.
  assert.deepEqual(ops.opened.filter((o) => o.focus).map((o) => o.path), [`${dir}/Program.cs`]);
});

test('single file change during a command is opened and focused', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.commandStarted();
  c.fileEvent('C:/ws/ShopWeb/Models/Customer.cs');
  c.commandFinished();
  assert.deepEqual(ops.revealed, []);
  assert.deepEqual(ops.opened, [{ path: 'C:/ws/ShopWeb/Models/Customer.cs', focus: true }]);
});

test('changes while idle (no command running) are ignored', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.fileEvent('C:/ws/ShopWeb/Models/Customer.cs'); // no commandStarted()
  c.commandFinished();
  assert.deepEqual(ops.opened, []);
  assert.deepEqual(ops.revealed, []);
});

test('build outputs, the store, and non-source files are ignored', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.commandStarted();
  c.fileEvent('C:/ws/ShopWeb/obj/Debug/Generated.cs');   // excluded segment: obj
  c.fileEvent('C:/ws/ShopWeb/bin/Debug/ShopWeb.dll');     // excluded segment + not openable
  c.fileEvent('C:/ws/Dump/enwiki-full.db');               // excluded segment: Dump
  c.fileEvent('C:/ws/ShopWeb/Models/Customer.cs');        // the only real change
  c.commandFinished();
  assert.deepEqual(ops.opened.map((o) => o.path), ['C:/ws/ShopWeb/Models/Customer.cs']);
});

test('duplicate and backslash paths are de-duplicated to one', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.commandStarted();
  c.fileEvent('C:/ws/App/Models/Customer.cs');
  c.fileEvent('C:\\ws\\App\\Models\\Customer.cs'); // same file, windows slashes
  c.commandFinished();
  assert.equal(ops.opened.length, 1);
  assert.equal(ops.opened[0].path, 'C:/ws/App/Models/Customer.cs');
});

test('autoOpenGenerated=false opens and reveals nothing', () => {
  const ops = new FakeOps();
  const c = core(ops, false);
  c.commandStarted();
  c.fileEvent('C:/ws/App/App.csproj');
  c.fileEvent('C:/ws/App/Program.cs');
  c.commandFinished();
  assert.deepEqual(ops.opened, []);
  assert.deepEqual(ops.revealed, []);
});

// A command that stops to ask a question (build-app's review loop, batch's approvals) goes
// busy -> prompting -> busy. That second `busy` resumes the *same* command, so the files it
// already wrote must survive; only a genuinely new command starts from an empty buffer.
test('a command resumed after a mid-command prompt keeps the files it already wrote', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.commandStarted();                          // `build app: ...`
  c.fileEvent('C:/ws/ShopWeb/ShopWeb.csproj'); // written before the review-loop prompt
  c.commandStarted();                          // answer submitted — same command resumes
  c.fileEvent('C:/ws/ShopWeb/Program.cs');
  c.commandFinished();

  assert.deepEqual(ops.revealed, ['C:/ws/ShopWeb']);
  const openedPaths = ops.opened.map((o) => o.path);
  assert.ok(openedPaths.includes('C:/ws/ShopWeb/ShopWeb.csproj'), 'pre-prompt file was dropped');
  assert.ok(openedPaths.includes('C:/ws/ShopWeb/Program.cs'));
});

test('a new command after one finishes starts from an empty buffer', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.commandStarted();
  c.fileEvent('C:/ws/App/Models/Customer.cs');
  c.commandFinished();
  ops.opened.length = 0;

  c.commandStarted();
  c.fileEvent('C:/ws/App/Models/Order.cs');
  c.commandFinished();
  assert.deepEqual(ops.opened.map((o) => o.path), ['C:/ws/App/Models/Order.cs']);
});

test('reset() discards a stale buffer so a later commandFinished() opens nothing', () => {
  const ops = new FakeOps();
  const c = core(ops);
  c.commandStarted();
  c.fileEvent('C:/ws/App/App.csproj');
  c.fileEvent('C:/ws/App/Program.cs');
  c.reset();
  c.commandFinished();
  assert.deepEqual(ops.opened, []);
  assert.deepEqual(ops.revealed, []);
});

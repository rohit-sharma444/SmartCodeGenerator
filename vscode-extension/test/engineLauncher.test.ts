// vscode-extension/test/engineLauncher.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { readStampedRoot, resolveEngineRoot } from '../src/engineLauncher';

/** A directory that looks like a SmartCodeGenerator checkout, as far as the launcher cares. */
function makeRepo(): string {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'scg-repo-'));
  fs.mkdirSync(path.join(root, 'CodeGenerator'), { recursive: true });
  fs.writeFileSync(path.join(root, 'CodeGenerator', 'CodeGenerator.csproj'), '<Project />');
  return root;
}

/** A directory that is emphatically not one — the case this whole function exists for. */
function makePlainFolder(): string {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'scg-other-'));
}

test('the open workspace is used when it is itself the repository', () => {
  const repo = makeRepo();
  // Even with a setting pointing elsewhere: opening the repo must keep working exactly as
  // before, so an existing user is unaffected by the setting's introduction.
  assert.strictEqual(resolveEngineRoot(repo, makeRepo()), repo);
  assert.strictEqual(resolveEngineRoot(repo, undefined), repo);
});

test('the configured repository is used when the workspace is some other folder', () => {
  const repo = makeRepo();
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), repo), repo);
});

test('undefined when the workspace is not the repo and nothing is configured', () => {
  // This is the reported bug: it must be reported as "not configured", not attempted and
  // failed, so the panel can say something better than "Engine stopped".
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), undefined), undefined);
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), ''), undefined);
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), '   '), undefined);
});

test('a subfolder of the checkout finds the checkout above it', () => {
  // Opening `…\SmartCodeGenerator-Repository\docs` is opening the repo as far as anyone
  // sane is concerned, so it must not demand a setting.
  const repo = makeRepo();
  const nested = path.join(repo, 'docs', 'guides');
  fs.mkdirSync(nested, { recursive: true });

  assert.strictEqual(resolveEngineRoot(nested, undefined), repo);
});

test('the walk up stops at the filesystem root instead of looping', () => {
  // A plain folder with no checkout anywhere above it must terminate, not spin.
  assert.strictEqual(resolveEngineRoot(path.parse(os.tmpdir()).root, undefined), undefined);
});

test('a remembered checkout is used when nothing else identifies one', () => {
  // Zero configuration for anyone who has ever opened the repo: the checkout seen then is
  // reused in whatever folder they open next, which is the case the user actually hit.
  const repo = makeRepo();
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), undefined, repo), repo);
});

test('an explicit setting outranks a remembered checkout', () => {
  // Memory is a convenience; a path the user typed is an instruction.
  const configured = makeRepo();
  const remembered = makeRepo();
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), configured, remembered), configured);
});

test('a remembered checkout that has since moved is ignored, not trusted', () => {
  const gone = path.join(os.tmpdir(), 'scg-remembered-but-deleted');
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), undefined, gone), undefined);
});

test('the open workspace still outranks both setting and memory', () => {
  const repo = makeRepo();
  assert.strictEqual(resolveEngineRoot(repo, makeRepo(), makeRepo()), repo);
});

test('undefined when the configured path is wrong rather than silently guessing', () => {
  const notARepo = makePlainFolder();
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), notARepo), undefined);
  assert.strictEqual(
    resolveEngineRoot(makePlainFolder(), path.join(os.tmpdir(), 'scg-does-not-exist')),
    undefined,
  );
});

test('surrounding whitespace in the setting is tolerated', () => {
  const repo = makeRepo();
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), `  ${repo}  `), repo);
});

test('the checkout the extension was built from is used when nothing else identifies one', () => {
  // The case that actually bit: a freshly installed VSIX, opened in a folder that is not the
  // repo, having never yet been opened in one. Workspace, setting and memory are all empty,
  // and before this there was simply no engine — "engine stopped", with nothing to restart.
  const repo = makeRepo();
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), undefined, undefined, repo), repo);
});

test('a remembered checkout outranks the one the extension was built from', () => {
  // Both are conveniences, but memory records a checkout this user actually ran; the stamp
  // only records where the VSIX happened to be packaged.
  const remembered = makeRepo();
  const stamped = makeRepo();
  assert.strictEqual(
    resolveEngineRoot(makePlainFolder(), undefined, remembered, stamped),
    remembered,
  );
});

test('a build-stamped checkout that has since moved is ignored, not trusted', () => {
  // A VSIX outlives the checkout it was built from — installed on another machine, or the
  // repo simply moved. Verified like every other candidate, so it degrades to the prompt.
  const gone = path.join(os.tmpdir(), 'scg-stamped-but-deleted');
  assert.strictEqual(resolveEngineRoot(makePlainFolder(), undefined, undefined, gone), undefined);
});

test('the build stamp is read from the packaged extension directory', () => {
  const repo = makeRepo();
  const extensionDir = makePlainFolder();
  fs.mkdirSync(path.join(extensionDir, 'out'), { recursive: true });
  fs.writeFileSync(
    path.join(extensionDir, 'out', 'buildInfo.json'),
    JSON.stringify({ engineRoot: repo }),
  );

  assert.strictEqual(readStampedRoot(extensionDir), repo);
});

test('a missing or unreadable build stamp is simply absent, never a crash', () => {
  // An extension built without the stamping step must still activate.
  assert.strictEqual(readStampedRoot(makePlainFolder()), undefined);

  const malformed = makePlainFolder();
  fs.mkdirSync(path.join(malformed, 'out'), { recursive: true });
  fs.writeFileSync(path.join(malformed, 'out', 'buildInfo.json'), 'not json at all');
  assert.strictEqual(readStampedRoot(malformed), undefined);

  const wrongShape = makePlainFolder();
  fs.mkdirSync(path.join(wrongShape, 'out'), { recursive: true });
  fs.writeFileSync(path.join(wrongShape, 'out', 'buildInfo.json'), JSON.stringify({ engineRoot: 7 }));
  assert.strictEqual(readStampedRoot(wrongShape), undefined);
});

test('a directory without the csproj is not a repository', () => {
  // A folder that merely has a CodeGenerator/ subdirectory must not qualify — the project
  // file is what the build actually needs.
  const root = makePlainFolder();
  fs.mkdirSync(path.join(root, 'CodeGenerator'));
  assert.strictEqual(resolveEngineRoot(root, undefined), undefined);
});

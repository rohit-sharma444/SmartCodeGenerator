// vscode-extension/test/engineLauncher.test.ts
import { test } from 'node:test';
import assert from 'node:assert';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { resolveEngineRoot } from '../src/engineLauncher';

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

test('a directory without the csproj is not a repository', () => {
  // A folder that merely has a CodeGenerator/ subdirectory must not qualify — the project
  // file is what the build actually needs.
  const root = makePlainFolder();
  fs.mkdirSync(path.join(root, 'CodeGenerator'));
  assert.strictEqual(resolveEngineRoot(root, undefined), undefined);
});

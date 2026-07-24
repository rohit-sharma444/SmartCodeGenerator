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

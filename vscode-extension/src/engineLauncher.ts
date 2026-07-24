// vscode-extension/src/engineLauncher.ts
import { spawn } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';

const PROJECT = 'CodeGenerator';
const CONFIG = 'Debug';

/**
 * The directory holding the SmartCodeGenerator checkout the engine is built from, or undefined
 * if we cannot tell.
 *
 * The engine is a .NET project in this repository, so it has to be built and launched from a
 * checkout — but the folder you have OPEN is usually the project you are generating code into,
 * which is a different thing entirely. Conflating the two is why the panel reported "Engine
 * stopped" in any folder that was not the repo: `dotnet build CodeGenerator` was run against a
 * directory with no such project.
 *
 * Four sources, in descending order of how strongly they state intent:
 *   1. the open workspace, or any folder above it — you are plainly in a checkout;
 *   2. `smartcodegen.repositoryPath` — a path the user typed is an instruction;
 *   3. the checkout last seen on this machine — a convenience, so that having opened the repo
 *      once means every other folder just works without configuring anything;
 *   4. the checkout this extension was built from, stamped into the package at build time —
 *      the backstop that makes a fresh install work in an unrelated folder straight away.
 *      Without it the first three are all empty until the repo has been opened at least once,
 *      which is exactly the "engine stopped in any other folder" the user hit.
 * Each is verified before use, so a checkout that has since been moved or deleted is skipped
 * rather than trusted.
 *
 * Returning undefined rather than guessing is deliberate: the caller can then say what is
 * wrong and offer to fix it, which is far more use than a failed build.
 *
 * Only the ENGINE comes from here. The engine's working directory and `PROJECTPATH` stay on the
 * open workspace, so generated code still lands in the folder you are working in.
 */
export function resolveEngineRoot(
  workspace: string,
  configured: string | undefined,
  remembered?: string | undefined,
  stamped?: string | undefined,
): string | undefined {
  return findEngineRootAt(workspace)
    ?? firstEngineRoot(configured)
    ?? firstEngineRoot(remembered)
    ?? firstEngineRoot(stamped);
}

/** Where `scripts/stamp-build-info.mjs` records the checkout, inside the packaged extension. */
const BUILD_INFO = ['out', 'buildInfo.json'];

/**
 * The checkout this extension was built from, as recorded at build time, or undefined if the
 * stamp is absent or unusable.
 *
 * The VSIX is packaged from a checkout, so that path is known at build time — and was until now
 * simply discarded, leaving a fresh install with no way to name a checkout at all. The stamp is
 * a build artifact (`out/` is git-ignored), so no machine-specific path is ever committed.
 *
 * Anything unexpected here reads as "no stamp": an extension built without the stamping step,
 * or installed somewhere the checkout no longer exists, must still activate and ask.
 */
export function readStampedRoot(extensionPath: string): string | undefined {
  try {
    const parsed: unknown = JSON.parse(
      fs.readFileSync(path.join(extensionPath, ...BUILD_INFO), 'utf8'),
    );
    const root = (parsed as { engineRoot?: unknown } | null)?.engineRoot;
    return typeof root === 'string' ? root : undefined;
  } catch {
    return undefined;
  }
}

/** A candidate path, honoured only if it really is a checkout. Blank and stale paths drop out. */
function firstEngineRoot(candidate: string | undefined): string | undefined {
  const trimmed = (candidate ?? '').trim();
  return trimmed !== '' && isEngineRoot(trimmed) ? trimmed : undefined;
}

/**
 * The checkout at `directory` or above it. Opening a subfolder of the repo — `docs`, or one of
 * the projects — is opening the repo as far as the user is concerned, and must not demand a
 * setting. The walk terminates at the filesystem root, where `path.dirname` becomes a fixed
 * point.
 */
function findEngineRootAt(directory: string): string | undefined {
  let current = directory.trim();
  if (current === '') return undefined;

  for (;;) {
    if (isEngineRoot(current)) return current;
    const parent = path.dirname(current);
    if (parent === current) return undefined;
    current = parent;
  }
}

/** Whether a directory holds the engine's project file — what `dotnet build` actually needs. */
export function isEngineRoot(directory: string): boolean {
  return directory !== ''
    && fs.existsSync(path.join(directory, PROJECT, `${PROJECT}.csproj`));
}

/**
 * Absolute path where the built CLI assembly lives (once built), or undefined if no build
 * output is present yet. Resolves the target-framework folder rather than hard-coding it.
 */
export function resolveDebugDll(workspaceRoot: string): string | undefined {
  const binDir = path.join(workspaceRoot, PROJECT, 'bin', CONFIG);
  let frameworks: string[];
  try {
    frameworks = fs.readdirSync(binDir);
  } catch {
    return undefined;
  }
  for (const framework of frameworks) {
    const dll = path.join(binDir, framework, `${PROJECT}.dll`);
    if (fs.existsSync(dll)) return dll;
  }
  return undefined;
}

export interface BuildResult {
  ok: boolean;
  message: string;
}

/**
 * Builds the CLI so the launched binary always carries the current source — in particular the
 * fix that routes Wikipedia store-update progress to stderr. A stale binary (e.g. an old Release
 * build) writes that progress to stdout, which floods the panel and wedges the ready-marker, so
 * launching a pre-built binary without rebuilding is unsafe. The build is incremental: a no-op
 * once warm (~5 s), a one-time cost when cold, after which every launch runs the fresh DLL fast
 * (~1 s) instead of paying `dotnet run`'s ~2-minute cold restore/evaluate on every start.
 */
export function buildEngine(workspaceRoot: string): Promise<BuildResult> {
  return new Promise((resolve) => {
    const child = spawn(
      'dotnet',
      ['build', PROJECT, '-c', CONFIG, '--nologo', '-v', 'quiet'],
      { cwd: workspaceRoot },
    );
    let stdout = '';
    let stderr = '';
    child.stdout?.setEncoding('utf8');
    child.stderr?.setEncoding('utf8');
    child.stdout?.on('data', (d: string) => { stdout += d; });
    child.stderr?.on('data', (d: string) => { stderr += d; });
    child.on('error', (e) => resolve({ ok: false, message: e.message }));
    child.on('exit', (code) =>
      resolve(code === 0
        ? { ok: true, message: 'built' }
        : { ok: false, message: (stderr || stdout || `dotnet build exited ${code}`).trim() }));
  });
}

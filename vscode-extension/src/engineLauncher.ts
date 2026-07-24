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
 * The open workspace wins when it is itself a checkout, so opening the repo behaves exactly as
 * it always did and the setting is only consulted when it is actually needed. Returning
 * undefined rather than guessing is deliberate: the caller can then say what is wrong, which is
 * far more use than a failed build.
 *
 * Only the ENGINE comes from here. The engine's working directory and `PROJECTPATH` stay on the
 * open workspace, so generated code still lands in the folder you are working in.
 */
export function resolveEngineRoot(
  workspace: string,
  configured: string | undefined,
): string | undefined {
  if (isEngineRoot(workspace)) return workspace;

  const setting = (configured ?? '').trim();
  return setting !== '' && isEngineRoot(setting) ? setting : undefined;
}

/** Whether a directory holds the engine's project file — what `dotnet build` actually needs. */
function isEngineRoot(directory: string): boolean {
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

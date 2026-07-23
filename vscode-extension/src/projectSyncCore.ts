// vscode-extension/src/projectSyncCore.ts

/**
 * How the core asks the editor to surface files. Implemented by the vscode adapter,
 * faked in tests — so this module never imports `vscode` and unit-tests without it.
 */
export interface EditorOps {
  revealFolder(path: string): void;
  openFile(path: string, focus: boolean): void;
}

export interface ProjectSyncOptions {
  enabled: boolean;
  maxTabs: number;
}

const EXCLUDED_SEGMENTS = new Set([
  'bin', 'obj', '.git', 'node_modules', 'Dump', 'TestResults', '.codegen', '.vs',
]);

const OPENABLE_EXTENSIONS = ['.cs', '.csproj', '.xaml', '.json'];

// Lower-case base names, in priority order, of a project's natural "start here" file.
const ENTRY_FILE_NAMES = ['program.cs', 'mainwindow.xaml', 'app.xaml.cs'];

function normalize(path: string): string {
  return path.replace(/\\/g, '/');
}

function isExcluded(path: string): boolean {
  return path.split('/').some((segment) => EXCLUDED_SEGMENTS.has(segment));
}

function isOpenable(path: string): boolean {
  const lower = path.toLowerCase();
  return OPENABLE_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

function dirOf(path: string): string {
  const i = path.lastIndexOf('/');
  return i < 0 ? path : path.slice(0, i);
}

function baseName(path: string): string {
  const i = path.lastIndexOf('/');
  return i < 0 ? path : path.slice(i + 1);
}

interface KeySelection {
  files: string[];
  primary: string;
}

/** The csproj, the entry point, then the model classes — deduped and capped. */
function selectKeyFiles(projectDir: string, paths: string[], maxTabs: number): KeySelection {
  const inProject = paths.filter((p) => p === projectDir || p.startsWith(projectDir + '/'));

  const csproj = inProject.find((p) => p.toLowerCase().endsWith('.csproj'));
  const entry = ENTRY_FILE_NAMES
    .map((name) => inProject.find((p) => baseName(p).toLowerCase() === name))
    .find((p): p is string => p !== undefined);
  const models = inProject.filter(
    (p) => p.includes('/Models/') && p.toLowerCase().endsWith('.cs'),
  );

  const ordered: string[] = [];
  const add = (p: string | undefined): void => {
    if (p !== undefined && !ordered.includes(p)) ordered.push(p);
  };
  add(csproj);
  add(entry);
  for (const model of models) add(model);

  const files = ordered.slice(0, maxTabs);
  const primary = entry ?? csproj ?? files[0];
  return { files, primary };
}

/**
 * Buffers workspace file changes seen while a command runs and, when it finishes, opens
 * a newly created project's key files (revealing its folder) or the changed source files.
 * State-gated by design: events seen while idle are the user's own edits and are ignored.
 */
export class ProjectSyncCore {
  private buffering = false;
  private readonly buffer: string[] = [];

  constructor(
    private readonly ops: EditorOps,
    private readonly opts: ProjectSyncOptions,
  ) {}

  commandStarted(): void {
    this.buffering = true;
    this.buffer.length = 0;
  }

  fileEvent(rawPath: string): void {
    if (!this.buffering) return;
    const path = normalize(rawPath);
    if (isExcluded(path) || !isOpenable(path)) return;
    if (!this.buffer.includes(path)) this.buffer.push(path);
  }

  commandFinished(): void {
    this.buffering = false;
    const paths = this.buffer.slice();
    this.buffer.length = 0;
    if (!this.opts.enabled || paths.length === 0) return;
    this.act(paths);
  }

  private act(paths: string[]): void {
    const csproj = paths.find((p) => p.toLowerCase().endsWith('.csproj'));
    if (csproj !== undefined) {
      const projectDir = dirOf(csproj);
      this.ops.revealFolder(projectDir);
      const { files, primary } = selectKeyFiles(projectDir, paths, this.opts.maxTabs);
      this.openInOrder(files, primary);
    } else {
      const files = paths.slice(0, this.opts.maxTabs);
      this.openInOrder(files, files[files.length - 1]);
    }
  }

  private openInOrder(files: string[], primary: string): void {
    for (const file of files) this.ops.openFile(file, file === primary);
  }
}

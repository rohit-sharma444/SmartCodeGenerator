// vscode-extension/src/projectSync.ts
import * as vscode from 'vscode';
import { EngineHost } from './engineHost';
import { EditorOps, ProjectSyncCore } from './projectSyncCore';

/** Grace window (ms) after the prompt returns, to absorb filesystem-watcher lag. */
const FLUSH_GRACE_MS = 400;

/**
 * Opens/reveals the files the tool writes while a command runs. A FileSystemWatcher feeds
 * every workspace source change to the core, but the core only acts on those buffered
 * between a command starting (engine `busy`) and finishing (`ready`) — so the user's own
 * edits, made while idle, never steal focus. Generated projects are subfolders of the open
 * workspace, so this reveals + opens rather than reloading the window with `openFolder`.
 */
export class ProjectSync {
  private readonly disposables: vscode.Disposable[] = [];
  private flushTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(host: EngineHost, workspaceRoot: string | undefined) {
    if (workspaceRoot === undefined) return; // no folder open → nothing to watch

    const enabled = vscode.workspace
      .getConfiguration('smartcodegen')
      .get<boolean>('autoOpenGenerated', true);

    const ops: EditorOps = {
      revealFolder: (path) =>
        void vscode.commands.executeCommand('revealInExplorer', vscode.Uri.file(path)),
      openFile: (path, focus) => {
        void vscode.workspace.openTextDocument(vscode.Uri.file(path)).then(
          (doc) =>
            vscode.window.showTextDocument(doc, { preview: false, preserveFocus: !focus }),
          () => { /* file vanished between event and flush; best-effort */ },
        );
      },
    };

    const core = new ProjectSyncCore(ops, { enabled, maxTabs: 5 });

    const watcher = vscode.workspace.createFileSystemWatcher(
      new vscode.RelativePattern(workspaceRoot, '**/*.{cs,csproj,xaml,json}'),
    );
    const onFs = (uri: vscode.Uri): void => core.fileEvent(uri.fsPath);
    watcher.onDidCreate(onFs, undefined, this.disposables);
    watcher.onDidChange(onFs, undefined, this.disposables);
    this.disposables.push(watcher);

    this.disposables.push(
      host.onStateChange((state) => {
        if (state === 'busy') {
          // A new command starts. Flush any command still pending its grace window first,
          // so its files aren't lost, then begin buffering the new one.
          if (this.flushTimer !== undefined) {
            clearTimeout(this.flushTimer);
            this.flushTimer = undefined;
            core.commandFinished();
          }
          core.commandStarted();
        } else if (state === 'ready') {
          this.flushTimer = setTimeout(() => {
            this.flushTimer = undefined;
            core.commandFinished();
          }, FLUSH_GRACE_MS);
        }
      }),
    );
  }

  dispose(): void {
    if (this.flushTimer !== undefined) clearTimeout(this.flushTimer);
    for (const d of this.disposables) d.dispose();
    this.disposables.length = 0;
  }
}

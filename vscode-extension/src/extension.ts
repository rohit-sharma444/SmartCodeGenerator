// vscode-extension/src/extension.ts
import * as vscode from 'vscode';
import * as path from 'node:path';
import { EngineHost } from './engineHost';
import { SmartCodeGenPanel } from './panelView';
import { ProjectSync } from './projectSync';
import { buildEngine, resolveDebugDll, resolveEngineRoot } from './engineLauncher';
import { dbg, LOG_PATH } from './debugLog';

let host: EngineHost | undefined;

// True while the one-time `dotnet build` runs at startup. The engine process isn't up yet, but
// the panel should read as "starting", not "stopped" — so state reporting consults this flag.
let building = false;

export function activate(context: vscode.ExtensionContext): void {
  const workspace = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? process.cwd();
  dbg(`ACTIVATE start; workspace=${JSON.stringify(workspace)}; log=${LOG_PATH}`);

  // The engine is built from a SmartCodeGenerator checkout, which is NOT necessarily the folder
  // you have open — that is usually the project you are generating code into. Only the engine
  // comes from here; cwd and PROJECTPATH below stay on the workspace, so generated code lands in
  // the folder you are working in.
  const engineRoot = resolveEngineRoot(
    workspace,
    vscode.workspace.getConfiguration('smartcodegen').get<string>('repositoryPath'),
  );
  dbg(`engineRoot=${JSON.stringify(engineRoot)}`);

  // Launch the built assembly directly (fast). Where it lives is known before the build; a fresh
  // clone with no build output yet falls back to the conventional path, which the build creates.
  const dll = engineRoot === undefined
    ? undefined
    : resolveDebugDll(engineRoot)
      ?? path.join(engineRoot, 'CodeGenerator', 'bin', 'Debug', 'net10.0', 'CodeGenerator.dll');
  dbg(`engine dll=${JSON.stringify(dll)}`);

  host = new EngineHost({
    command: 'dotnet',
    args: [dll ?? ''],
    cwd: workspace,
    env: { ...process.env, PROJECTPATH: workspace },
  });

  const reportState = () =>
    panel.post({ type: 'state', state: building ? 'starting' : host!.state });

  const panel = new SmartCodeGenPanel(context.extensionUri, {
    onSubmit: (text) => {
      if (building) return; // input is disabled while starting; ignore any stray submit
      if (host!.state === 'stopped') {
        panel.post({ type: 'system', text: 'Engine is stopped. Run "SmartCodeGen: Restart Engine".' });
        return;
      }
      host!.send(text);
    },
    onReady: () => {
      dbg(`PANEL handshake: webview ready, building=${building}, host.state=${host!.state}, hasView=${panel.hasView()}`);
      reportState();
    },
    onDbg: (text) => dbg(`WEBVIEW ${text}`),
  });

  host.onOutput((chunk, stream) => panel.post({ type: 'output', chunk, stream }));
  host.onStateChange((state) => {
    dbg(`EXT onStateChange -> ${state}; hasView=${panel.hasView()} (posting to panel)`);
    panel.post({ type: 'state', state });
    if (state === 'stopped') panel.post({ type: 'system', text: 'Engine stopped.' });
  });

  // Auto-open / reveal projects and files the tool generates while a command runs.
  const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  const projectSync = new ProjectSync(host, workspaceFolder);

  context.subscriptions.push(
    vscode.window.registerWebviewViewProvider(SmartCodeGenPanel.viewType, panel, {
      webviewOptions: { retainContextWhenHidden: true },
    }),
    vscode.commands.registerCommand('smartcodegen.restartEngine', () => {
      panel.post({ type: 'system', text: 'Restarting engine…' });
      host!.restart();
    }),
    vscode.commands.registerCommand('smartcodegen.clearTranscript', () => panel.clear()),
    vscode.commands.registerCommand('smartcodegen.focusInput', () => panel.focusInput()),
    { dispose: () => host?.dispose() },
    projectSync,
  );

  if (engineRoot === undefined) {
    // Nothing to build against. Say what is actually wrong and how to fix it — attempting the
    // build here is what used to produce a bare "Engine stopped" with no explanation.
    panel.post({
      type: 'system',
      text:
        `This folder is not a SmartCodeGenerator checkout, so there is no engine to run.\n` +
        `Set "smartcodegen.repositoryPath" to your checkout, then reload the window ` +
        `(Developer: Reload Window). Generated code will still go to this folder.`,
    });
    panel.post({ type: 'state', state: 'stopped' });
    return;
  }

  // Build the CLI (so the binary carries the current source — notably the store-update-to-stderr
  // fix a stale build lacks), then launch the fresh DLL. Building is incremental and fast once warm.
  void buildThenStart(engineRoot, panel, reportState);
}

async function buildThenStart(
  engineRoot: string,
  panel: SmartCodeGenPanel,
  reportState: () => void,
): Promise<void> {
  building = true;
  reportState();
  panel.post({ type: 'system', text: 'Preparing the code generator (first run may take a minute)…' });
  dbg('BUILD start');

  const result = await buildEngine(engineRoot);
  building = false;
  dbg(`BUILD done ok=${result.ok} ${result.ok ? '' : JSON.stringify(result.message).slice(0, 300)}`);

  if (!result.ok) {
    panel.post({ type: 'system', text: `Could not build the code generator:\n${result.message}` });
    panel.post({ type: 'state', state: 'stopped' });
    return;
  }

  dbg('BUILD ok; calling host.start()');
  host!.start();
}

export function deactivate(): void {
  host?.dispose();
  host = undefined;
}

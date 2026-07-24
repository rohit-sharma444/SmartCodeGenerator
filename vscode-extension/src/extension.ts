// vscode-extension/src/extension.ts
import * as vscode from 'vscode';
import * as path from 'node:path';
import { EngineHost } from './engineHost';
import { SmartCodeGenPanel } from './panelView';
import { ProjectSync } from './projectSync';
import { buildEngine, isEngineRoot, resolveDebugDll, resolveEngineRoot } from './engineLauncher';
import { dbg, LOG_PATH } from './debugLog';

let host: EngineHost | undefined;

// True while the one-time `dotnet build` runs at startup. The engine process isn't up yet, but
// the panel should read as "starting", not "stopped" — so state reporting consults this flag.
let building = false;

// The checkout the engine is currently built from, once one is known. Undefined means we have
// nowhere to run from yet, which is a thing to say plainly rather than a failure to launch.
let engineRoot: string | undefined;

/**
 * Where the last known checkout is remembered, so that opening some unrelated folder later
 * needs no configuration at all. Machine-scoped on purpose: a checkout is a location on this
 * disk, not a property of any one project.
 */
const REMEMBERED_ROOT_KEY = 'smartcodegen.lastKnownRepositoryPath';

export function activate(context: vscode.ExtensionContext): void {
  const workspace = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? process.cwd();
  dbg(`ACTIVATE start; workspace=${JSON.stringify(workspace)}; log=${LOG_PATH}`);

  // The engine is built from a SmartCodeGenerator checkout, which is NOT necessarily the folder
  // you have open — that is usually the project you are generating code into. Only the engine
  // comes from here; cwd and PROJECTPATH below stay on the workspace, so generated code lands in
  // the folder you are working in.
  const discovered = resolveEngineRoot(
    workspace,
    vscode.workspace.getConfiguration('smartcodegen').get<string>('repositoryPath'),
    context.globalState.get<string>(REMEMBERED_ROOT_KEY),
  );
  dbg(`engineRoot=${JSON.stringify(discovered)}`);

  host = new EngineHost({
    command: 'dotnet',
    args: [],
    cwd: workspace,
    env: { ...process.env, PROJECTPATH: workspace },
  });

  const reportState = () =>
    panel.post({ type: 'state', state: building ? 'starting' : host!.state });

  const panel = new SmartCodeGenPanel(context.extensionUri, {
    onSubmit: (text) => {
      if (building) return; // input is disabled while starting; ignore any stray submit
      if (host!.state === 'stopped') {
        // Never having had a checkout and having crashed are different problems with different
        // fixes; offering "Restart Engine" for the former just stops it again.
        panel.post({
          type: 'system',
          text: engineRoot === undefined
            ? 'No SmartCodeGenerator checkout is configured, so there is no engine to run. Run "SmartCodeGen: Locate Checkout".'
            : 'Engine is stopped. Run "SmartCodeGen: Restart Engine".',
        });
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

  /**
   * Points the engine at a checkout and starts it. The one path into a running engine, shared
   * by startup, the setting changing and the folder picker, so a checkout found late behaves
   * exactly like one known from the start — no window reload anywhere.
   */
  const useEngineRoot = (root: string): void => {
    if (root === engineRoot && (building || host!.state !== 'stopped')) return; // already on it
    engineRoot = root;
    void context.globalState.update(REMEMBERED_ROOT_KEY, root);

    // Launch the built assembly directly (fast). Where it lives is known before the build; a
    // fresh clone with no build output yet falls back to the conventional path, which the
    // build creates.
    const dll = resolveDebugDll(root)
      ?? path.join(root, 'CodeGenerator', 'bin', 'Debug', 'net10.0', 'CodeGenerator.dll');
    dbg(`engine dll=${JSON.stringify(dll)}`);
    host!.setArgs([dll]);
    host!.dispose(); // no-op when not running; replaces an engine from a previous checkout

    // Build the CLI (so the binary carries the current source — notably the store-update-to-
    // stderr fix a stale build lacks), then launch the fresh DLL. Incremental and fast once warm.
    void buildThenStart(root, panel, reportState);
  };

  /** Asks for the checkout and remembers it, for when nothing else could identify one. */
  const locateRepository = async (): Promise<void> => {
    const picked = await vscode.window.showOpenDialog({
      canSelectFolders: true,
      canSelectFiles: false,
      canSelectMany: false,
      openLabel: 'Use this checkout',
      title: 'Select your SmartCodeGenerator checkout',
    });
    const folder = picked?.[0]?.fsPath;
    if (folder === undefined) return; // cancelled

    if (!isEngineRoot(folder)) {
      // Name what was missing rather than just refusing: the usual mistake is picking the
      // CodeGenerator project folder instead of the checkout that contains it.
      void vscode.window.showErrorMessage(
        `That folder is not a SmartCodeGenerator checkout — it has no CodeGenerator${path.sep}` +
        `CodeGenerator.csproj. Pick the repository's top-level folder.`,
      );
      return;
    }

    // Machine-scoped: the checkout is a place on this disk, not a property of this project.
    await vscode.workspace
      .getConfiguration('smartcodegen')
      .update('repositoryPath', folder, vscode.ConfigurationTarget.Global);
    useEngineRoot(folder);
  };

  context.subscriptions.push(
    vscode.window.registerWebviewViewProvider(SmartCodeGenPanel.viewType, panel, {
      webviewOptions: { retainContextWhenHidden: true },
    }),
    vscode.commands.registerCommand('smartcodegen.restartEngine', () => {
      if (engineRoot === undefined) {
        // Nothing to restart. An engine that never had a checkout would just stop again;
        // point at the thing that actually needs doing.
        panel.post({ type: 'system', text: 'No SmartCodeGenerator checkout is configured yet.' });
        void locateRepository();
        return;
      }
      panel.post({ type: 'system', text: 'Restarting engine…' });
      host!.restart();
    }),
    vscode.commands.registerCommand('smartcodegen.locateRepository', () => void locateRepository()),
    vscode.commands.registerCommand('smartcodegen.clearTranscript', () => panel.clear()),
    vscode.commands.registerCommand('smartcodegen.focusInput', () => panel.focusInput()),
    // Setting the path is meant to fix things there and then; making the user reload the window
    // for it to take effect is one more way for this to look broken.
    vscode.workspace.onDidChangeConfiguration((e) => {
      if (!e.affectsConfiguration('smartcodegen.repositoryPath')) return;
      const root = resolveEngineRoot(
        workspace,
        vscode.workspace.getConfiguration('smartcodegen').get<string>('repositoryPath'),
        context.globalState.get<string>(REMEMBERED_ROOT_KEY),
      );
      if (root !== undefined) useEngineRoot(root);
    }),
    { dispose: () => host?.dispose() },
    projectSync,
  );

  if (discovered === undefined) {
    // Nothing to build against. Say what is actually wrong and offer to fix it — attempting the
    // build here is what used to produce a bare "Engine stopped" with no explanation.
    panel.post({
      type: 'system',
      text:
        `This folder is not a SmartCodeGenerator checkout, so there is no engine to run.\n` +
        `Run "SmartCodeGen: Locate Checkout" to point at your copy of the repository — it is ` +
        `remembered, so this is a one-off. Generated code still goes to this folder.`,
    });
    panel.post({ type: 'state', state: 'stopped' });
    void vscode.window
      .showWarningMessage(
        'SmartCodeGen needs your SmartCodeGenerator checkout to run the code generator.',
        'Locate checkout…',
      )
      .then((choice) => {
        if (choice !== undefined) void locateRepository();
      });
    return;
  }

  useEngineRoot(discovered);
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
  // Module state outlives a single activation in a reused extension host; a stale checkout
  // here would make the next activate() think an engine was already configured.
  engineRoot = undefined;
  building = false;
}

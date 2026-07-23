// vscode-extension/src/extension.ts
import * as vscode from 'vscode';
import { EngineHost } from './engineHost';
import { SmartCodeGenPanel } from './panelView';

let host: EngineHost | undefined;

export function activate(context: vscode.ExtensionContext): void {
  const workspace = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? process.cwd();

  host = new EngineHost({
    command: 'dotnet',
    args: ['run', '--project', 'CodeGenerator'],
    cwd: workspace,
    env: { ...process.env, PROJECTPATH: workspace },
  });

  const panel = new SmartCodeGenPanel(context.extensionUri, {
    onSubmit: (text) => {
      if (host!.state === 'stopped') {
        panel.post({ type: 'system', text: 'Engine is stopped. Run "SmartCodeGen: Restart Engine".' });
        return;
      }
      host!.send(text);
    },
    onReady: () => panel.post({ type: 'state', state: host!.state }),
  });

  host.onOutput((chunk, stream) => panel.post({ type: 'output', chunk, stream }));
  host.onStateChange((state) => {
    panel.post({ type: 'state', state });
    if (state === 'stopped') panel.post({ type: 'system', text: 'Engine stopped.' });
  });

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
    { dispose: () => host?.dispose() }
  );

  // Start the engine now; the onView activation event means activate() runs when the panel is opened.
  host.start();
}

export function deactivate(): void {
  host?.dispose();
  host = undefined;
}

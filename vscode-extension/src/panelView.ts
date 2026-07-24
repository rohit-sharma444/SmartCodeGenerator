// vscode-extension/src/panelView.ts
import * as vscode from 'vscode';
import { InboundMessage, OutboundMessage } from './protocol';

export class SmartCodeGenPanel implements vscode.WebviewViewProvider {
  public static readonly viewType = 'smartcodegen.panel';
  private view: vscode.WebviewView | undefined;

  constructor(
    private readonly extensionUri: vscode.Uri,
    private readonly handlers: {
      onSubmit: (text: string) => void;
      onReady?: () => void;
      onDbg?: (text: string) => void;
    }
  ) {}

  resolveWebviewView(view: vscode.WebviewView): void {
    this.view = view;
    view.webview.options = {
      enableScripts: true,
      localResourceRoots: [vscode.Uri.joinPath(this.extensionUri, 'media')],
    };
    view.webview.html = this.getHtml(view.webview);
    view.webview.onDidReceiveMessage((msg: InboundMessage) => {
      if (msg.type === 'submit') this.handlers.onSubmit(msg.text);
      else if (msg.type === 'ready') this.handlers.onReady?.();
      else if (msg.type === 'dbg') this.handlers.onDbg?.(msg.text);
    });
  }

  post(msg: OutboundMessage): void {
    this.view?.webview.postMessage(msg);
  }

  hasView(): boolean {
    return this.view !== undefined;
  }

  clear(): void {
    this.post({ type: 'system', text: '__clear__' });
  }

  focusInput(): void {
    this.view?.show?.(true);
    this.post({ type: 'system', text: '__focus__' });
  }

  private getHtml(webview: vscode.Webview): string {
    const nonce = getNonce();
    const cssUri = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'media', 'main.css'));
    const jsUri = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'media', 'main.js'));
    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy"
    content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';" />
  <link href="${cssUri}" rel="stylesheet" />
</head>
<body>
  <div id="transcript" role="log" aria-live="polite"></div>
  <form id="inputform">
    <input id="input" type="text" autocomplete="off" placeholder="Type a command…" />
  </form>
  <script nonce="${nonce}" src="${jsUri}"></script>
</body>
</html>`;
  }
}

function getNonce(): string {
  let text = '';
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  for (let i = 0; i < 32; i++) text += chars.charAt(Math.floor(Math.random() * chars.length));
  return text;
}

// vscode-extension/src/panelView.ts
import * as vscode from 'vscode';
import { MessageBuffer } from './messageBuffer';
import { InboundMessage, OutboundMessage } from './protocol';

export class SmartCodeGenPanel implements vscode.WebviewViewProvider {
  public static readonly viewType = 'smartcodegen.panel';
  private view: vscode.WebviewView | undefined;

  /**
   * Messages are held until the webview says it is listening. Two separate windows would
   * otherwise swallow them: before `resolveWebviewView` there is no webview at all, and
   * between that and the script's first line the page is still loading, so a `postMessage`
   * lands with nothing to receive it. Both are ordinary at startup — activate() reports why
   * the engine cannot run seconds before the panel is able to show it — and a dropped message
   * there reads as a crashed engine.
   */
  private readonly outbound = new MessageBuffer<OutboundMessage>();

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
    // A reloaded webview has not run its script yet, so hold messages again until it says so.
    this.outbound.close();
    view.webview.options = {
      enableScripts: true,
      localResourceRoots: [vscode.Uri.joinPath(this.extensionUri, 'media')],
    };
    view.webview.html = this.getHtml(view.webview);
    view.webview.onDidReceiveMessage((msg: InboundMessage) => {
      if (msg.type === 'submit') this.handlers.onSubmit(msg.text);
      else if (msg.type === 'ready') {
        // The handshake is the first moment a post can actually arrive. Flush before the
        // handler runs so the backlog precedes whatever it posts, keeping the transcript in
        // the order things really happened.
        this.outbound.open((m) => void view.webview.postMessage(m));
        this.handlers.onReady?.();
      } else if (msg.type === 'dbg') this.handlers.onDbg?.(msg.text);
    });
    view.onDidDispose(() => {
      // Only if this is still the current view: a late disposal from a replaced view must not
      // close the sink its replacement has already opened.
      if (this.view !== view) return;
      this.view = undefined;
      this.outbound.close();
    });
  }

  post(msg: OutboundMessage): void {
    this.outbound.post(msg);
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
    content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}' ${webview.cspSource};" />
  <link href="${cssUri}" rel="stylesheet" />
</head>
<body>
  <div id="transcript" role="log" aria-live="polite"></div>
  <form id="inputform">
    <span id="promptlabel" aria-hidden="true"></span>
    <input id="input" type="text" autocomplete="off"
           aria-label="CodeGenerator command" placeholder="Type a command…" />
  </form>
  <script type="module" nonce="${nonce}" src="${jsUri}"></script>
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

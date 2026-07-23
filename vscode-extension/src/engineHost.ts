// vscode-extension/src/engineHost.ts
import { spawn, ChildProcess } from 'node:child_process';

export type EngineState = 'starting' | 'ready' | 'busy' | 'stopped';

export interface Disposable {
  dispose(): void;
}

export interface EngineHostOptions {
  command: string;
  args: string[];
  cwd: string;
  env?: NodeJS.ProcessEnv;
  readyMarker?: RegExp;
}

type OutputListener = (chunk: string, stream: 'stdout' | 'stderr') => void;
type StateListener = (state: EngineState) => void;

export class EngineHost {
  private child: ChildProcess | undefined;
  private _state: EngineState = 'stopped';
  private readonly outputListeners = new Set<OutputListener>();
  private readonly stateListeners = new Set<StateListener>();
  private readonly readyMarker: RegExp;
  private tail = '';

  constructor(private readonly opts: EngineHostOptions) {
    this.readyMarker = opts.readyMarker ?? /CodeGenerator >>\s*$/;
  }

  get state(): EngineState {
    return this._state;
  }

  onOutput(cb: OutputListener): Disposable {
    this.outputListeners.add(cb);
    return { dispose: () => this.outputListeners.delete(cb) };
  }

  onStateChange(cb: StateListener): Disposable {
    this.stateListeners.add(cb);
    return { dispose: () => this.stateListeners.delete(cb) };
  }

  start(): void {
    if (this.child) return;
    this.setState('starting');
    this.tail = '';
    const child = spawn(this.opts.command, this.opts.args, {
      cwd: this.opts.cwd,
      env: this.opts.env ?? process.env,
    });
    this.child = child;
    child.stdout!.setEncoding('utf8');
    child.stderr!.setEncoding('utf8');
    child.stdout!.on('data', (d: string) => this.handleOutput(d, 'stdout'));
    child.stderr!.on('data', (d: string) => this.handleOutput(d, 'stderr'));
    child.on('exit', () => {
      if (this.child === child) {
        this.child = undefined;
        this.setState('stopped');
      }
    });
  }

  send(text: string): void {
    if (!this.child) throw new Error('engine not running');
    this.setState('busy');
    this.child.stdin!.write(text + '\n');
  }

  restart(): void {
    this.dispose();
    this.start();
  }

  dispose(): void {
    const child = this.child;
    this.child = undefined;
    if (child) child.kill();
    this.setState('stopped');
  }

  private handleOutput(chunk: string, stream: 'stdout' | 'stderr'): void {
    for (const cb of this.outputListeners) cb(chunk, stream);
    if (stream === 'stdout') {
      this.tail = (this.tail + chunk).slice(-200);
      if (this.readyMarker.test(this.tail)) {
        this.tail = '';
        this.setState('ready');
      }
    }
  }

  private setState(s: EngineState): void {
    if (this._state === s) return;
    this._state = s;
    for (const cb of this.stateListeners) cb(s);
  }
}

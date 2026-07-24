// vscode-extension/src/engineHost.ts
import { spawn, ChildProcess } from 'node:child_process';
import { dbg } from './debugLog';

/**
 * `ready` — at the top-level prompt, accepting a new command.
 * `busy` — running; input must stay disabled.
 * `prompting` — blocked on a *mid-command* question (batch's block reader and approval loop,
 *   build-app's review loop). The engine cannot proceed until answered, so input must be
 *   enabled — but the command is still in flight, which is why this isn't just `ready`.
 */
export type EngineState = 'starting' | 'ready' | 'busy' | 'prompting' | 'stopped';

/**
 * The prompts the CLI writes before a mid-command `Console.ReadLine()`, anchored to the end of
 * the stream because they are written without a trailing newline:
 *   `... `             BatchCommand.ReadBlock
 *   `> `               BatchCommand.ConfirmSnapshotSize, BuildAppCommand.RunReviewLoop
 *   `approve? (y/n) `  BatchCommand.CollectApprovals
 * `CodeGenerator >> ` also ends in "> ", so the leading newline anchor matters — and the ready
 * marker is tested first regardless. Add a pattern here whenever the CLI grows a new prompt.
 */
const DEFAULT_PROMPT_MARKER = /(?:^|\r?\n)(?:\.\.\.|>) $|approve\? \(y\/n\) $/;

export interface Disposable {
  dispose(): void;
}

export interface EngineHostOptions {
  command: string;
  args: string[];
  cwd: string;
  env?: NodeJS.ProcessEnv;
  readyMarker?: RegExp;
  promptMarker?: RegExp;
}

type OutputListener = (chunk: string, stream: 'stdout' | 'stderr') => void;
type StateListener = (state: EngineState) => void;

export class EngineHost {
  private child: ChildProcess | undefined;
  private _state: EngineState = 'stopped';
  private readonly outputListeners = new Set<OutputListener>();
  private readonly stateListeners = new Set<StateListener>();
  private readonly readyMarker: RegExp;
  private readonly promptMarker: RegExp;
  private tail = '';

  constructor(private readonly opts: EngineHostOptions) {
    this.readyMarker = opts.readyMarker ?? /CodeGenerator >>\s*$/;
    this.promptMarker = opts.promptMarker ?? DEFAULT_PROMPT_MARKER;
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
    child.stdout!.on('data', (d: string) => { if (this.child === child) this.handleOutput(d, 'stdout'); });
    child.stderr!.on('data', (d: string) => { if (this.child === child) this.handleOutput(d, 'stderr'); });
    child.on('exit', () => {
      if (this.child === child) {
        this.child = undefined;
        this.setState('stopped');
      }
    });
    child.on('error', () => {
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
    if (child && child.pid !== undefined) {
      if (process.platform === 'win32') {
        // `dotnet run` is a launcher; kill the whole tree so the built CLI isn't orphaned.
        spawn('taskkill', ['/pid', String(child.pid), '/t', '/f'], { stdio: 'ignore' });
      } else {
        child.kill();
      }
    }
    this.setState('stopped');
  }

  private handleOutput(chunk: string, stream: 'stdout' | 'stderr'): void {
    for (const cb of this.outputListeners) cb(chunk, stream);
    if (stream === 'stdout') {
      this.tail = (this.tail + chunk).slice(-200);
      // Ready wins over prompting: `CodeGenerator >> ` ends in "> " like the review-loop prompt.
      const matched = this.readyMarker.test(this.tail) ? 'ready'
        : this.promptMarker.test(this.tail) ? 'prompting'
        : undefined;
      dbg(`OUT stdout ${JSON.stringify(chunk.slice(0, 60))} tailEnd=${JSON.stringify(this.tail.slice(-45))} match=${matched ?? false}`);
      if (matched !== undefined) {
        this.tail = '';
        this.setState(matched);
      }
    } else {
      dbg(`OUT stderr ${JSON.stringify(chunk.slice(0, 60))}`);
    }
  }

  private setState(s: EngineState): void {
    if (this._state === s) return;
    dbg(`STATE ${this._state} -> ${s}`);
    this._state = s;
    for (const cb of this.stateListeners) cb(s);
  }
}

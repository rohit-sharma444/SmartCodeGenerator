// vscode-extension/src/messageBuffer.ts

/** Backlog kept while no sink is attached. Generous: startup holds a handful of messages. */
const DEFAULT_LIMIT = 1000;

/**
 * Holds messages until something is actually listening, then lets them straight through.
 *
 * A webview does not exist when the extension activates, and posting to one that has not
 * loaded silently discards the message — there is no error and no retry. That cost us the
 * only explanation the user ever gets: activate() posted "this folder is not a checkout, set
 * smartcodegen.repositoryPath" roughly 2.7 seconds before the webview script loaded, so the
 * panel showed nothing but a disabled input reading "engine stopped". The bug looked like a
 * crashed engine and was really a message dropped on the floor.
 *
 * Buffering is per-sink rather than permanent: a webview that reloads starts an empty
 * transcript, but replaying old startup text into a session that has moved on would be worse
 * than staying quiet, so a flushed backlog is not kept for the next sink.
 */
export class MessageBuffer<T> {
  private pending: T[] = [];
  private sink: ((message: T) => void) | undefined;

  constructor(private readonly limit: number = DEFAULT_LIMIT) {}

  /** Delivers now if a sink is attached, otherwise holds until one is. */
  post(message: T): void {
    if (this.sink !== undefined) {
      this.sink(message);
      return;
    }
    this.pending.push(message);
    // Evict oldest-first, like a terminal scrollback: if a view stays closed while the engine
    // talks, the recent lines are the ones worth having.
    if (this.pending.length > this.limit) this.pending.splice(0, this.pending.length - this.limit);
  }

  /** Attaches a sink and flushes the backlog into it, in the order the messages were posted. */
  open(sink: (message: T) => void): void {
    this.sink = sink;
    const backlog = this.pending;
    this.pending = [];
    for (const message of backlog) sink(message);
  }

  /** Detaches the sink so later posts are held rather than sent into a disposed view. */
  close(): void {
    this.sink = undefined;
  }
}

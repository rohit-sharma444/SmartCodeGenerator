// vscode-extension/src/debugLog.ts
// TEMPORARY diagnostics: writes a timestamped timeline to a temp file so a locked-prompt
// session can be inspected directly. Remove once the panel-state bug is found.
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

export const LOG_PATH = path.join(os.tmpdir(), 'scg-debug.log');

let started = false;

export function dbg(line: string): void {
  try {
    const stamp = new Date().toISOString();
    if (!started) {
      fs.writeFileSync(LOG_PATH, `=== SCG debug started ${stamp} ===\n`);
      started = true;
    }
    fs.appendFileSync(LOG_PATH, `${stamp}  ${line}\n`);
  } catch {
    /* diagnostics must never break the extension */
  }
}

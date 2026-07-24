// vscode-extension/scripts/stamp-build-info.mjs
// Records the checkout this extension is being built from into `out/buildInfo.json`, so an
// installed VSIX can run the engine in a folder that is not the repository — the first run in
// any other folder, with nothing configured and nothing yet remembered.
//
// Runs after `tsc`, and reuses the compiled `isEngineRoot` rather than restating what a checkout
// looks like, so the build stamp and the runtime check can never drift apart.
import { createRequire } from 'node:module';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.join(here, '..', 'out');

const require = createRequire(import.meta.url);
const { isEngineRoot } = require(path.join(outDir, 'engineLauncher.js'));

/** The checkout at or above `directory`, or undefined at the filesystem root. */
function findEngineRoot(directory) {
  for (let current = directory; ; ) {
    if (isEngineRoot(current)) return current;
    const parent = path.dirname(current);
    if (parent === current) return undefined;
    current = parent;
  }
}

const engineRoot = findEngineRoot(here);

// Building from outside a checkout is not an error — the extension just falls back to asking.
if (engineRoot === undefined) {
  console.warn('stamp-build-info: no SmartCodeGenerator checkout above this script; not stamping.');
  process.exit(0);
}

fs.mkdirSync(outDir, { recursive: true });
fs.writeFileSync(
  path.join(outDir, 'buildInfo.json'),
  `${JSON.stringify({ engineRoot }, undefined, 2)}\n`,
);
console.log(`stamp-build-info: engineRoot=${engineRoot}`);

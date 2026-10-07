# SmartCodeGen VS Code Extension

An in-editor REPL panel that drives the SmartCodeGenerator `CodeGenerator` CLI.
Requires the .NET SDK on PATH and a SmartCodeGenerator checkout on the machine.

The engine runs from the checkout; generated code goes into the folder you have
open. The two are usually different folders, so you do **not** need to open the
SmartCodeGenerator repo as your workspace.

## How the engine is launched

1. The extension finds the checkout (`resolveEngineRoot` in
   `src/engineLauncher.ts`). The first source that holds
   `CodeGenerator/CodeGenerator.csproj` wins:
   1. the open workspace, or any folder above it;
   2. the `smartcodegen.repositoryPath` setting;
   3. the last checkout used on this machine (remembered in global state as
      `smartcodegen.lastKnownRepositoryPath`);
   4. the checkout the extension was built from, stamped into
      `out/buildInfo.json` at build time.

   If none is found, the panel says so; run **SmartCodeGen: Locate Checkout**.
2. It runs `dotnet build CodeGenerator -c Debug --nologo -v quiet` in the
   checkout. The build is incremental: a few seconds once warm, longer on a
   fresh clone. The input stays disabled while it runs.
3. It starts `dotnet <checkout>/CodeGenerator/bin/Debug/<framework>/CodeGenerator.dll`
   with the working directory and `PROJECTPATH` set to the open workspace, so
   generated files appear in the Explorer.

## Install (use it in your normal VS Code — recommended)

```bash
cd vscode-extension
npm install
npm run package      # builds smartcodegen-vscode.vsix
npm run install-ext  # code --install-extension ... --force
```

Then **reload VS Code** (Command Palette → *Developer: Reload Window*, or restart).
Open the folder you want to generate code into and click the **SmartCodeGen**
icon in the **Activity Bar** (primary side bar). The panel opens, builds and
starts the engine; type commands, e.g. `create console project Demo`, then
`use project Demo`, then `create class Item`.

Re-running `npm run package && npm run install-ext` after a code change updates
the installed extension (reload the window to pick it up).

## Settings

| Setting | Default | Purpose |
| --- | --- | --- |
| `smartcodegen.repositoryPath` | `""` | Absolute path to the checkout. Needed only when none of the other sources finds one. Takes effect immediately. |
| `smartcodegen.autoOpenGenerated` | `true` | Open and reveal files the tool creates or changes. |

## Commands

| Command | Does |
| --- | --- |
| SmartCodeGen: Restart Engine | Restarts the CLI process (asks for a checkout if none is known). |
| SmartCodeGen: Locate Checkout | Folder picker; sets `smartcodegen.repositoryPath` and starts the engine. |
| SmartCodeGen: Clear Transcript | Clears the panel. |
| SmartCodeGen: Focus Input | Moves focus to the input box. |

## Develop (F5, without installing)

```bash
npm install
npm run compile      # tsc, then scripts/stamp-build-info.mjs writes out/buildInfo.json
```

Press **F5** (uses `.vscode/launch.json`) to open an Extension Development Host;
its workspace is opened at the repo root (`${workspaceFolder}/..`) automatically.
This always opens a *separate* window — use the Install flow above to run inside
your normal VS Code instead.

## Notes

- **Wikipedia store:** the CLI does not apply store increments. Only the
  updater service (`WikipediaUpdateService/UpdateWorker.cs`) does. At startup
  the CLI prints store status only when something is wrong (no store date, the
  service is not running, or the last update failed); `status` shows it always.

## Test

```bash
npm test                          # unit tests (fake engine)
RUN_ENGINE_INTEGRATION=1 npm test # also runs the real-CLI integration test
```

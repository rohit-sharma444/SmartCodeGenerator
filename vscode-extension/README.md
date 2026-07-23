# SmartCodeGen VS Code Extension

An in-editor REPL panel that drives the SmartCodeGenerator `CodeGenerator` CLI.
Developer tool: use it with the SmartCodeGenerator repo open as the workspace.
Requires the .NET SDK on PATH — the engine is launched as
`dotnet run --project CodeGenerator` with `PROJECTPATH` set to the workspace
root, so generated files appear in the Explorer.

## Install (use it in your normal VS Code — recommended)

```bash
cd vscode-extension
npm install
npm run package      # builds smartcodegen-vscode.vsix
npm run install-ext  # code --install-extension ... --force
```

Then **reload VS Code** (Command Palette → *Developer: Reload Window*, or restart).
Open the SmartCodeGenerator repo as your workspace folder and click the
**SmartCodeGen** icon in the **Activity Bar** (primary side bar). The panel opens
and starts the engine; type commands, e.g. `create console project Demo`, then
`use project Demo`, then `create class Item`. On the first run the input stays
disabled while `dotnet run` cold-builds.

Re-running `npm run package && npm run install-ext` after a code change updates
the installed extension (reload the window to pick it up).

## Develop (F5, without installing)

```bash
npm install
npm run compile
```

Press **F5** (uses `.vscode/launch.json`) to open an Extension Development Host;
its workspace is opened at the repo root (`${workspaceFolder}/..`) automatically.
This always opens a *separate* window — use the Install flow above to run inside
your normal VS Code instead.

## Notes

- **Wikipedia store update:** the CLI applies daily store increments in the
  background at startup. Its progress (`Wikipedia store updating N%`) is written
  to stderr, so it shows in the transcript as muted status lines **without**
  locking the prompt — the input stays free while the store updates.

## Test

```bash
npm test                          # unit tests (fake engine)
RUN_ENGINE_INTEGRATION=1 npm test # also runs the real-CLI integration test
```

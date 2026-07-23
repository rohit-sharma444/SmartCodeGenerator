# SmartCodeGen VS Code Extension

An in-editor REPL panel that drives the SmartCodeGenerator `CodeGenerator` CLI.
Developer tool: run it with the SmartCodeGenerator repo open as the workspace.

## Develop / run

```bash
cd vscode-extension
npm install
npm run compile
```

Then press **F5** (uses `.vscode/launch.json`) to open an Extension Development
Host. The launch config opens the repo root (`${workspaceFolder}/..`) as the
host's workspace automatically, so the engine can find `CodeGenerator` and write
generated files into the Explorer. Open the **SmartCodeGen** view in the bottom
panel (Ctrl+J, then the SmartCodeGen tab) and type commands, e.g.
`create console project Demo`, then `use project Demo`, then `create class Item`.
On the first run the input stays disabled while `dotnet run` cold-builds.

Requires the .NET SDK on PATH; the engine is launched as
`dotnet run --project CodeGenerator` with `PROJECTPATH` set to the workspace root,
so generated files appear in the Explorer.

## Test

```bash
npm test                          # unit tests (fake engine)
RUN_ENGINE_INTEGRATION=1 npm test # also runs the real-CLI integration test
```

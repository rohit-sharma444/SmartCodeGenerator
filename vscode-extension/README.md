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
Host. Open the **SmartCodeGen** view in the bottom panel and type commands, e.g.
`create console project Demo`, then `use project Demo`, then `create class Item`.

Requires the .NET SDK on PATH; the engine is launched as
`dotnet run --project CodeGenerator` with `PROJECTPATH` set to the workspace root,
so generated files appear in the Explorer.

## Test

```bash
npm test                          # unit tests (fake engine)
RUN_ENGINE_INTEGRATION=1 npm test # also runs the real-CLI integration test
```

# CodeGenerator.Tests

Test suite for the code-generation stack — [`CodeGenerationEngine`](../CodeGenerationEngine/README.md),
[`NaturalLanguageBridge`](../NaturalLanguageBridge/README.md), the
[`CodeGenerator`](../CodeGenerator/README.md) CLI itself, and
[`AppScaffolding`](../AppScaffolding/README.md). Uses **MSTest** (`net10.0`).

## What's covered

| Folder | Focus |
| --- | --- |
| `Core/` | Intent analysis and NLP-driven parsing, entity extraction, the learning engine (`LearnedIntentMemory`/`Store`, `LearningCoordinator`), end-to-end pipeline runs, and workspace/save reproductions of past bugs. |
| `Bridge/` | The `NaturalLanguagePipeline` and `NlpActionProvider` glue between NLE and the code engine. |
| `App/` | The `build app` path end to end: the CLI's own commands, the review loop, `DomainModelMapper`, every generated template, the scaffolding targets, and the command/help catalogs. |
| `Helper/` | Shared test utilities. |

This is also the only suite that sees **both**
[`DataIngestion`](../DataIngestion/README.md) and the scaffolder, so the tests
that need both live here rather than in
[`DataIngestion.Tests`](../DataIngestion.Tests/README.md):

| Class | Focus |
| --- | --- |
| `App/BuildAppDataSourceTests.cs` | The CLI front step: the widened `build app` trigger (and that it does not hijack "build application"), the tail parse, app naming from a description or a file name, and combining findings across tables into one numbered list. |
| `App/SeedFileWriterTests.cs` | Imported rows projected through each class's **final** property list — so a field removed in the review loop stops being written, an added one is omitted, a value that will not parse is dropped, nav properties are never written, and a hostile value stays one string. |
| `App/SeedDataFromFileTests.cs` | The generated seeder's second mode, and that a project with **no** imported data carries no seed branch at all. Plus the csproj `Content` item, and that injecting it twice is idempotent. |
| `App/GeneratedProjectBuildTests.cs` | `ProjectGeneratedFromACsv_*` — a CSV becomes a project that **actually builds**, with its data present as valid JSON and copied to the build output. Substring assertions over generated source cannot see CS0023, CS0542, or a literal that broke out of its string; compiling can. |

The generated-project tests are `[TestCategory("Integration")]` and shell out to
`dotnet build` / `dotnet test`, so they are the slow ones. They also write under
the system temp folder deliberately: a longer root plus a generated project's
nesting exceeds `MAX_PATH`, which MSBuild reports as MSB3030 "file not found" on a
file that is plainly there.

## Running

```bash
# from the repository root

# run this suite
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj

# a single class
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj \
  --filter "FullyQualifiedName~PipelineEndToEndTests"

# everything except the slow compile-the-generated-app tests
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj \
  --filter "TestCategory!=Integration"
```

Or run the whole solution the way CI does — which is **not** the same as building
one project in Debug, since that hides analyzer errors and runs one suite of five:

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test  SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

`docs/commands.md` is generated from the command catalog and pinned by
`CommandReferenceDocTests`. After adding or changing a command, regenerate it
rather than hand-editing:

```bash
UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj \
  --filter "FullyQualifiedName~CommandReferenceDocTests"
```

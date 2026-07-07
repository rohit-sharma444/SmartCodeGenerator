# CodeGenerationEngine

The core library behind the Smart Code Generator. It turns a parsed
natural-language command into intent, entities, and — via Roslyn — actual C#
source on disk. The [`CodeGenerator`](../CodeGenerator/README.md) CLI and the
[`NaturalLanguageBridge`](../NaturalLanguageBridge/README.md) are thin layers on
top of this project; almost all of the real work happens here.

This is a class library (`net10.0`) with no entry point of its own. It is
consumed as a project reference.

## What it does

Given a user command such as `create class Customer with properties Name string`,
the engine:

1. **Analyzes** the text into a semantic model (optionally guided by an action
   verb supplied by the NLP bridge).
2. **Detects intent** — `CreateClass`, `CreateProject`, `AddValidation`,
   `AnalyzeClass`, and the rest of [`IntentType`](Models/IntentType.cs).
3. **Extracts entities** — class/project names, properties, methods, types,
   validation rules.
4. **Generates and persists code** using Roslyn, then runs semantic and
   compilation checks over the result.
5. **Learns** from each outcome so repeated phrasings resolve faster.

## Entry points

Most callers go through a single façade:

```csharp
using CodeGenerationEngine.Service;

bool handled = CommandHandlingService.ProcessInput(
    input:               "create class Customer with properties Name string",
    currentProject:      ref project,          // ProjectMetaData? (updated in place)
    configuredRules:     rules,                // List<PropertyRule>
    workspaceRoot:       workspaceRoot,        // where files are written
    setLastCreatedProject: p => { /* persist selection */ },
    nlpAction:           "create");            // optional verb hint from NLE
```

`ProcessInput` parses the command, dispatches it to the matching handler,
records the outcome for the learning layer, and returns whether the command was
handled.

Lower-level building blocks are also public, e.g.:

- `NaturalLanguageCodeEngine.ExecuteCommand(command, workspaceRoot)` — generate
  code for an already-parsed command.
- `RoslynClassGenerator.Generate(classDefinition)` — build C# source from a
  `ClassDefinition`.

## Layout

| Folder | Contents |
| --- | --- |
| `Core/` | Parsing, intent analysis, entity extraction, the Roslyn generators, validation, and the learning engine. |
| `Service/` | `CommandHandlingService` (the dispatch façade) and `GeneratedCodeService` (persistence). |
| `Models/` | Command, semantic, project, and validation record/DTO types. |
| `Factory/` | `DotNetProjectFactory` — scaffolds `.csproj` projects by type. |
| `Rules/`, `Dictionary/`, `Tokenizer/`, `Helper/` | Supporting rule definitions, synonyms, tokenization, and file/console helpers. |

## Dependencies

- `Microsoft.CodeAnalysis.Common` and `Microsoft.CodeAnalysis.CSharp` (Roslyn)
  for code generation and compilation checks.

## Build & test

```bash
# from the repository root
dotnet build CodeGenerationEngine/CodeGenerationEngine.csproj
```

The engine's behavior is exercised by [`CodeGenerator.Tests`](../CodeGenerator.Tests/README.md).

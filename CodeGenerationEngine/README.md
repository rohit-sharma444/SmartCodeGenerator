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
   `AddBusinessValidation`, `AnalyzeClass`, and the rest of
   [`IntentType`](Models/IntentType.cs).
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
| `Core/` | Parsing, intent analysis, entity extraction, the Roslyn generators, the learning engine, and `Validation/` — the extensible validation rule registry (see below). |
| `Service/` | `CommandHandlingService` (the dispatch façade) and `GeneratedCodeService` (persistence). |
| `Models/` | Command, semantic, project, and validation record/DTO types. |
| `Factory/` | `DotNetProjectFactory` — scaffolds `.csproj` projects by type. |
| `Rules/` | Supporting rule definitions. |
| `Helper/` | File and console helpers. |

## Validation

Two independent commands — `add validation` (data, field-level) and
`add business validation` (business, entity-level) — sit on one extensible rule
engine in `Core/Validation/`. The design is open for extension, closed for
modification: a new rule type is one new file, discovered automatically.

- **`IValidationRule`** — a parsed rule that knows how to `Describe()` itself
  (for the `show …` commands) and `Emit()` its own C# check into the generated
  `Validate()` method. `Category` is `Data` or `Business`.
- **`IValidationRuleParser`** — recognizes one rule type's natural-language
  phrasing (`TryParse`); `Priority` gives deterministic ordering.
- **`ValidationRuleCatalog`** — reflection-discovers every parser in the
  assembly, ordered by `Priority`. **Adding a validation type = drop one
  `IValidationRule` + `IValidationRuleParser` pair into `Core/Validation/Rules/`;
  nothing else changes** — not the catalog, the `ValidationRegistry` store, the
  emitter, or the command handlers.
- **`ValidationRegistry`** stores the parsed rules per class, filterable by
  category. `ValidationEngine.EmitValidationPartial(...)` renders one `Validate()`
  partial with a data-validation section and a business-rules section, asking
  each stored rule to emit itself.

Shipped rule types: `RequiredRule`, `LengthRangeRule`, `NumericRangeRule`,
`GreaterThanRule`, `LessThanRule`, `RegexRule` (email + pattern),
`AllowedValuesRule` (data); `ComparisonRule` (cross-field, e.g. `EndDate must be
after StartDate`) and `RequiredWhenRule` (conditional) (business). Emitted code
is verified by compiling it with Roslyn in the tests, not just string-matching.

## Dependencies

- `Microsoft.CodeAnalysis.Common` and `Microsoft.CodeAnalysis.CSharp` (Roslyn)
  for code generation and compilation checks.

## Build & test

```bash
# from the repository root
dotnet build CodeGenerationEngine/CodeGenerationEngine.csproj
```

The engine's behavior is exercised by [`CodeGenerator.Tests`](../CodeGenerator.Tests/README.md).

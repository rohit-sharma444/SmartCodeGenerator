# CodeGenerationEngine

The core library behind the Smart Code Generator. It turns a parsed
natural-language command into intent, entities, and — via Roslyn — actual C#
source on disk. The [`CodeGenerator`](../CodeGenerator/README.md) CLI and the
[`NaturalLanguageBridge`](../NaturalLanguageBridge/README.md) are thin layers on
top of this project; almost all of the real work happens here.

This is a class library (`net10.0`) with no entry point of its own. It is
consumed as a project reference.

## What it does

The engine does **not** read raw text. The
[`NaturalLanguageBridge`](../NaturalLanguageBridge/README.md) turns a sentence
into a `ParsedCommand` (intent plus fields); the engine executes it. Given the
command for `create class Customer with properties Name string`, the engine:

1. **Dispatches** on the command's [`IntentType`](Models/IntentType.cs) to one
   handler (`CreateClass`, `AddValidation`, `AddBusinessValidation`,
   `AnalyzeClass`, and so on).
2. **Generates and persists code** using Roslyn, then runs semantic and
   compilation checks over the result.
3. **Reports the outcome** to `LearningCoordinator`, which reinforces or weakens
   the learned intent memory (`LearnedIntentMemory`, stored by
   `LearnedIntentStore`). Learning of whole phrases from a miss followed by a
   retry (`PhraseCandidates`) is also recorded here, and shipped phrases are
   reviewed by hand.

`CommandParser`, `CommandNormalizer`, `CommandDictionary` and `EntityExtractor`
in `Core/` normalize text and read entities back out of generated class files.

## Entry points

Most callers go through one façade:

```csharp
using CodeGenerationEngine.Service;

bool handled = CommandHandlingService.Execute(
    command:               parsedCommand,        // ParsedCommand, made by the bridge
    currentProject:        ref project,          // ProjectMetaData? (updated in place)
    configuredRules:       rules,                // List<PropertyRule>
    workspaceRoot:         workspaceRoot,        // where files are written
    setLastCreatedProject: p => { /* persist selection */ });
```

`Execute` looks the command's intent up in a handler table, runs the handler,
records the outcome for the learning layer, and returns whether the command
succeeded. `CommandHandlingService.IsSupported(intent)` says whether an intent
has a handler, so a caller can preview a command without running it.

Lower-level building blocks are also public, e.g.:

- `NaturalLanguageCodeEngine.ExecuteCommand(command, workspaceRoot)` — generate
  code for an already-parsed command.
- `RoslynClassGenerator` — build C# source from a `ClassDefinition`.
- `Compiler.Compile` / `Compiler.CompileSources` — compile one source, or every
  class in a workspace together.

## Layout

| Folder | Contents |
| --- | --- |
| `Core/` | Command parsing helpers, entity extraction, the Roslyn generators, the class, property, method and project services, and the learning types (`LearningCoordinator`, `LearnedIntentStore`, `PhraseCandidates`). Sub-folders below. |
| `Core/Validation/` | The extensible validation rule registry (see below). |
| `Core/Relationships/` | Class relationships: `RelationshipDetector` proposes missing ones; `RelationshipWriter` and `JoinClassWriter` write them and verify the whole workspace still compiles. |
| `Core/Undo/` | Undo and redo: `UndoJournal` (capacity 50), `UndoScope`, `SessionState` and `SessionRecorder`. `WorkspaceSnapshot` (in `Core/`) is the file copy it restores from. |
| `Service/` | `CommandHandlingService` (the dispatch façade) and `GeneratedCodeService` (persistence). |
| `Models/` | Command, semantic, project, and validation record/DTO types. |
| `Factory/` | `DotNetProjectFactory` — scaffolds `.csproj` projects by type. |
| `Rules/` | `IPropertyRule` and `DefaultTypeRules`: the type rules for properties. |
| `Helper/` | File, path, process and console-output helpers. |

## Handled intents

The handler table in `CommandHandlingService` covers: projects (create, use,
list, show, current), classes (create, show, rename, delete, restore, restore
last, list deleted, inherit), interfaces (`CreateInterface`), properties
(create, list, delete, rename, change type), methods (create, list, delete,
rename), validation (data and business: add, show, remove; `GenerateValidator`),
analysis and documentation (`AnalyzeClass`, `AnalyzeProject`,
`GenerateDocumentation`, `GenerateDesignDocument`), `ValidateProject` and
`CompileProject`, `ListClasses`, `ListClassesInProject`, `Help`, and
`ShowLearning` / `ResetLearning`. An intent with no entry (for example
`GenerateCrud`) fails with a "not supported yet" report.

## Validation

Two independent commands — `add validation` (data, field-level) and
`add business validation` (business, entity-level) — sit on one extensible rule
engine in `Core/Validation/`. The design is open for extension, closed for
modification: a new rule type is one new file, discovered automatically.

- **`IValidationRule`** — a parsed rule that knows how to `Describe()` itself
  (for the `show …` commands) and `Emit()` its own C# check into the generated
  `Validate()` method. `Category` is `Data`, `Business` or `Workflow`.
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

Shipped rule types, in `Core/Validation/Rules/` (20 files):

- Data: `RequiredRule`, `LengthRangeRule`, `LengthBoundRule`, `NumericRangeRule`,
  `GreaterThanRule`, `LessThanRule`, `InclusiveBoundRule`, `RegexRule` (email +
  pattern), `AllowedValuesRule`, `NamedFormatRule`, `NumberShapeRule`,
  `TextShapeRule`, `RelativeDateRule`.
- Business: `ComparisonRule` (cross-field, e.g. `EndDate must be after
  StartDate`), `RequiredWhenRule` (conditional), `SameAsRule`, `EitherOrRule`,
  `ArithmeticRule`, `PercentageRule` (total = net times one plus a rate).
- Workflow: `WorkflowRule` — a status may only move forward through a stated
  order. It has no parser: it is built only from an accepted, ordered Status
  proposal, never typed by hand.

Emitted code is verified by compiling it with Roslyn in the tests, not just
string-matching.

## Dependencies

- `Microsoft.CodeAnalysis.Common` and `Microsoft.CodeAnalysis.CSharp` (Roslyn)
  for code generation and compilation checks.

## Build & test

```bash
# from the repository root
dotnet build CodeGenerationEngine/CodeGenerationEngine.csproj
```

The engine's behavior is exercised by [`CodeGenerator.Tests`](../CodeGenerator.Tests/README.md).

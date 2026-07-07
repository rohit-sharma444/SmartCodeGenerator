# NaturalLanguageBridge

A small adapter that wires the [`NaturalLanguageEngine`](../NaturalLanguageEngine/README.md)
(NLE) to the [`CodeGenerationEngine`](../CodeGenerationEngine/README.md). It lets
the CLI analyze a command with NLE **first**, then hand the result to the code
generator.

This is a class library (`net10.0`) with two types and no entry point of its own.

## Why it exists

NLE and the code generator each have their own notion of a command. The bridge
keeps them decoupled: NLE stays a general-purpose NLP library, the code engine
stays focused on generation, and this project owns the glue.

## Types

### `NlpActionProvider`

Builds the NLE analyzer once (lazily) and reuses it. Because the engine shares
mutable stage instances across calls, `Analyze` is serialized with a lock.

```csharp
using NaturalLanguageBridge;

SemanticResult? analysis = NlpActionProvider.Analyze("create class Customer");
string? verb = NlpActionProvider.GetPrimaryAction(analysis); // "create"
```

- `Analyze(string)` — runs NLE over the input. Returns `null` for blank input or
  if NLE throws; it never throws itself.
- `GetPrimaryAction(...)` — extracts the lowercased primary action verb, falling
  back to the intent name (`"AddIntent"` → `"add"`) and ignoring the generic
  `DescribeIntent`.

### `NaturalLanguagePipeline`

The NLE-led entry point for a single command: analyze once, optionally surface
the analysis via a callback, then delegate parsing and code generation to
`CommandHandlingService` with the extracted verb.

```csharp
var pipeline = new NaturalLanguagePipeline();

PipelineOutcome outcome = pipeline.Handle(
    input:                 "create class Customer",
    project:               ref project,
    configuredRules:       rules,
    workspaceRoot:         workspaceRoot,
    setLastCreatedProject: p => { /* persist selection */ },
    onAnalyzed:            a => { /* optional: inspect the SemanticResult */ });

// outcome.Handled  -> did the code generator handle it?
// outcome.Analysis -> the NLE SemanticResult (null if NLE failed)
```

## Dependencies

Project references only — `CodeGenerationEngine` and `NaturalLanguageEngine`.

## Build

```bash
# from the repository root
dotnet build NaturalLanguageBridge/NaturalLanguageBridge.csproj
```

Bridge behavior is covered by tests under `CodeGenerator.Tests/Bridge/`.

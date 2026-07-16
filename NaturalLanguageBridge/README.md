# NaturalLanguageBridge

The **command front**: the single place where user text becomes a structured
`ParsedCommand`. It sits between the
[`NaturalLanguageEngine`](../NaturalLanguageEngine/README.md) (NLE) and the
[`CodeGenerationEngine`](../CodeGenerationEngine/README.md), and owns every piece
of knowledge that connects the two.

This is a class library (`net10.0`) with no entry point of its own. The
[`CodeGenerator`](../CodeGenerator/README.md) CLI drives it.

## Why it exists

NLE and the code generator each have their own notion of a command, and neither
should learn the other's vocabulary. NLE stays a general-purpose NLP library —
it never learns what a "class" is. The code generator stays focused on
generation, and **never re-parses raw text**: it receives a `ParsedCommand` and
acts on it. Everything in between lives here.

## How a command is parsed

`CommandFront.Parse(input, workspace)` is the whole contract, and the order is
load-bearing:

```
user text
    │
    ▼
LiteralCommandTable       exact match — meta-commands that are not natural
    │                     language at all, and must never enter the NLP path
    ▼
residual pattern guards   the few forms the literal table cannot express
    │                     (see below)
    ▼
NlpCommandMapper          NLE analysis → CodeGenerationEngine IntentType
    │
    ▼
CommandEntityExtractor    pull the fields (names, types, targets) out of the prose
    │
    ▼
ParsedCommand             intent + fields + confidence + the original sentence
```

The **residual guards** are regexes read from the raw sentence, ahead of the
mapper, each earning its place by a case the layers around it get wrong:

| Guard | Why it can't live elsewhere |
| --- | --- |
| `list/show/display classes in <project>` | NLE drops the trailing `in`, so the mapper loses the project. |
| `\bvalidator\b` | A bare substring match; the literal table only does whole-input equality. |
| `add validation <args>`, `show/list/view validation <args>` | Argument-bearing forms the exact table can't cover and the mapper has no entry for — without these, `add validation Email should not be blank on Email in Customer` fell through to `Unknown`. |
| `use/select/switch/open/activate project <name>` | The mapper's `UseProject` rule only covers the bare form with no name. The bridge captures the name so the handler reads a field instead of slicing the sentence itself. |

> **Note:** `CommandEntityExtractor` parses the **normalized raw sentence with
> regexes**, not NLE's entity output. That is deliberate and measured — NLE's
> entities proved too lossy for structured field extraction. It lives here rather
> than in `CodeGenerationEngine` so that raw text is parsed in the bridge and
> nowhere else.

## Types

| Type | Role |
| --- | --- |
| `CommandFront` | The entry point above. `Parse(input, workspace) → ParsedCommand`. Stateless. |
| `LiteralCommandTable` | Exact-match commands that must skip the NLP path: learning/view meta-commands and the demo short-circuit. Trimmed, case-insensitive, whole-input. Mirrors the score-999 regex guards in `IntentAnalyzer`. |
| `NlpCommandMapper` | NLE's domain-neutral analysis → an `IntentType`. Where code-generation vocabulary lives. Stateless. |
| `CommandEntityExtractor` | Prose → the `ParsedCommand`'s fields. Regex over the sentence. Stateless. |
| `NlpActionProvider` | Builds the NLE analyzer once (lazily) and reuses it. `Analyze` is serialized with a lock, because the pipeline shares mutable stage instances across calls. Returns `null` for blank input or if NLE throws — never throws itself. `GetPrimaryAction` extracts the lowercased action verb, falling back to the intent name (`"AddIntent"` → `"add"`) and ignoring the generic `DescribeIntent`. |
| `NaturalLanguagePipeline` / `PipelineOutcome` | NLE-led handling of one command: analyze once, surface the analysis via a callback, then delegate to `CommandHandlingService`. |
| `BatchCommandDetector` | Turns the lines of a batch into structured actions **without executing any of them**, by reconstructing the chain `CommandHandlingService.ParseInput` runs privately. |
| `DetectedAction` | One batch line plus the command it was understood to be. The raw `Sentence` — not `Preview` — is what executes, by being re-parsed against live state at execution time. |
| `ActionFormatter` | Renders a `DetectedAction` as one line for the review list. Presentation only, kept apart because it churns most. |
| `ApprovalReply` / `ApprovalReplyParser` | The meaning of one answer at a batch approval prompt. Lives here so it can be unit-tested — `CodeGenerator.Tests` cannot reference the CLI executable. |

## Usage

```csharp
using NaturalLanguageBridge;

// Parse only — no execution, no state.
ParsedCommand command = CommandFront.Parse("create class Customer", workspaceRoot);

// Or the NLE-led path that parses and then generates.
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

## Build & test

```bash
# from the repository root
dotnet build NaturalLanguageBridge/NaturalLanguageBridge.csproj
```

Bridge behavior is covered by tests under
[`CodeGenerator.Tests`](../CodeGenerator.Tests/README.md)`/Bridge/`.

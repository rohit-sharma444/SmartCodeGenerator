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

`CommandFront.Parse(input, workspace)` is the whole parse contract, and the order is
load-bearing:

```
user text
    │
    ▼
KeywordCorrector          step 0: near-miss spellings of business /
    │                     validation / validator are put right
    ▼
LiteralCommandTable       exact match — meta-commands that are not natural
    │                     language at all, and must never enter the NLP path
    ▼
CommandFront guards       regexes on the corrected sentence, in a fixed order
    │                     (22 of them, see below)
    ▼
NlpCommandMapper          NLE analysis → CodeGenerationEngine IntentType
    │
    ▼
CommandEntityExtractor    pull the fields (names, types, targets) out of the prose
    │
    ▼
ParsedCommand             intent + fields + confidence + the original sentence
```

Recognition has exactly three sources: `LiteralCommandTable`, the `CommandFront`
guards, and `NlpCommandMapper`. `IntentRepository` (in `CodeGenerationEngine`)
drives none of them; only the AI corpus harvester reads it.

**Literal table.** `LiteralCommandTable` holds 19 phrases: `show/list/view learning`,
`reset/clear/forget learning`, `show project`, `add validation`, `show/list/view
validation`, `add business validation`, `show/list/view business validation`,
`help`, and `validate/check/compile project`. Each must be the whole input.

**Guards.** Each guard is a regex read from the corrected sentence, ahead of the
mapper, and each earns its place by a case the layers around it get wrong. In
order: list classes in a project, `use/select/switch/open/activate project
<name>`, class inheritance, create project, create class, create interface, list
properties/methods of a class, `show/delete/remove/restore/recover/analyze
class <name>`, rename class, add method, add property, rename property, delete
method, delete property, rename method, change a property's type, remove a
validation (by field or by number, data or business), add business validation,
show business validation, add validation, show validation, and any sentence with
the word `validator` (generate validator). `CommandFront.GuardedIntents` lists
the intents the guards can produce, by hand, and a gate test reads it.

**Mapper.** `NlpCommandMapper` maps an NLE (verb, target) pair to an intent, for
project, class, property, method, interface and document commands that no guard
caught. `MappedIntents` lists what it can produce.

**Running.** `CommandFront.Run` and `RunDetailed` parse, then call
`CommandHandlingService.Execute`. `RunDetailed` returns a `RunResult` (`Handled`,
`Recognized`, `NeededAi`). It saves the parsing state first and restores it if the
command fails. The AI seam: when parsing gives `Unknown`, and both an
`IIntentResolver` and a `confirmAi` callback are supplied, the resolver is asked
once and its answer is used only if it is not `Unknown` and the user confirms. With
both null the behavior is the same as before the AI layer.

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
| `BatchCommandDetector` | Turns the lines of a batch into structured actions **without executing any of them**: each line goes through `CommandFront.Parse`, and `CommandHandlingService.IsSupported` marks an intent with no handler. It saves and restores the parsing state around the whole preview. |
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
    // optional: aiResolver, confirmAi, ct

// outcome.Handled  -> did the code generator handle it?
// outcome.Analysis -> the NLE SemanticResult (null if NLE failed)
```

## Dependencies

Project references: `CodeGenerationEngine`, `NaturalLanguageEngine` and
`CodeIntelligence.Abstractions` (the `IIntentResolver` seam).

## Build & test

```bash
# from the repository root
dotnet build NaturalLanguageBridge/NaturalLanguageBridge.csproj
```

Bridge behavior is covered by tests under
[`CodeGenerator.Tests`](../CodeGenerator.Tests/README.md)`/Bridge/`.

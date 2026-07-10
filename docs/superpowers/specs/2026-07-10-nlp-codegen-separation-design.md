# NLP / Code-Generation Separation — Design Spec

**Date:** 2026-07-10
**Status:** Approved (design)
**Branch:** `FixesAndEnhancements`

## Goal

Enforce one rule across the solution:

> `NaturalLanguageEngine` is responsible only for natural language processing.
> `CodeGenerationEngine` is responsible only for code generation.

`NaturalLanguageBridge` sits between them and owns the translation from generic
NLP output to code-generation concepts. When this lands, raw user text can no
longer reach `CodeGenerationEngine`, and `NaturalLanguageEngine` still contains
no knowledge of classes, properties, or C#.

## Background: where the boundary is broken today

The violation is entirely one-sided.

**`NaturalLanguageEngine` is already clean.** A search for `Roslyn`,
`CSharpSyntax`, `.csproj`, `CodeGen`, and phrases such as `"create class"`
returns nothing. Its `EntityType` enum is domain-neutral (`Person`,
`Organization`, `Location`, `Date`, `Time`, `Quantity`, `Action`, `Object`,
`Attribute`, `Value`) and `Intent` is a name plus a confidence score.

**`CodeGenerationEngine` carries a second, complete NLP stack**, parallel to and
independent of NLE:

| File | Lines | Duplicates NLE's |
|---|---|---|
| `Dictionary/Synonyms.cs` | 33,012 | `Knowledge/Models/SynonymSet.cs`, `EnglishLanguageProvider.LoadSynonyms()` |
| `Core/TextTokenizer.cs` | 495 | `Lexical/Tokenizers/*` |
| `Core/IntentAnalyzer.cs` | 494 | `Semantics/IntentRecognizer.cs` |
| `Core/EntityRecognizer.cs` | 80 | `Semantics/EntityRecognizer.cs` |
| `Core/RelationExtractor.cs` | 71 | `Semantics/RelationshipExtractor.cs` |
| `Core/EnglishLanguageModel.cs` | 69 | `Knowledge/Providers/EnglishLanguageProvider.cs` |
| `Core/SemanticAnalyzer.cs` | 40 | `Core/SemanticAnalyzer.cs` |

Roughly 34,200 lines.

**The bridge that should prevent this is a passthrough.**
`NaturalLanguagePipeline.Handle` calls NLE, then discards nearly all of the
result. It keeps one value — `NlpActionProvider.GetPrimaryAction`, a single
lowercased verb — and passes the **raw, unprocessed input** to
`CommandHandlingService.ProcessInput`, which re-tokenizes, re-normalizes,
re-recognizes entities and re-detects intent using the duplicate stack. NLE's
analysis is surfaced through `onAnalyzed` for display and otherwise dropped.

A consequence: **intent is detected twice per command.**
`SemanticAnalyzer.Analyze` calls `IntentAnalyzer.Detect(input, nlpAction)`
(`Core/SemanticAnalyzer.cs:34`), then `ParseInput` immediately calls the other
overload `IntentAnalyzer.Detect(semantic)`
(`Service/CommandHandlingService.cs:71`), discarding the first result.

## Evidence: NLE already produces what the bridge needs

Running NLE over the real command vocabulary
(`dotnet run --project NaturalLanguageEngine -- "…"`):

```
Input: create a class called Customer
  Intent: CreateIntent (76%)
  - [Action] "create" (80%)
  - [Object] "a class" (70%)
  - [Action] "called" (80%)
  - [Person] "Customer" (60%)
  Relationships: (none)

Input: add property Email to Customer
  - [Action] "add"      [Object] "property"
  - [Person] "Email"    [Person] "Customer"

Input: rename class Foo to Bar
  - [Action] "rename"   [Object] "class"
  - [Person] "Foo"      [Person] "Bar"

Input: generate a repository for Invoice
  - [Action] "generate" [Object] "a repository"
  - [Person] "Invoice"
```

The verb, the target kind, and the identifiers are all present.

### Do not add code-gen nouns to NLE's lexicon

`class`, `property`, `method`, `interface`, `controller`, `repository`,
`service`, `field`, `entity`, `model`, `dto`, and `validator` are all **absent**
from `EnglishLanguageProvider`. The tempting fix is to add them. That is the
very violation this spec removes — it would place code-generation vocabulary
inside the NLP engine.

It is also unnecessary. `PosTagger` (`Syntax/PosTagger.cs:71-77`) falls back to
`Noun` for unknown word-type tokens, which is exactly why `"class"` surfaces as
an `Object` entity despite not being in the lexicon.

## Design

### Dependencies

```
NaturalLanguageEngine     (no project references — pure NLP)
        ^
        |
NaturalLanguageBridge  ->  CodeGenerationEngine   (code generation only)
        ^
        |
   CodeGenerator (CLI)
```

Unchanged from today. `CodeGenerationEngine` must never reference the bridge or
NLE.

### The seam

`CommandHandlingService.ProcessInput` is the single chokepoint where text enters
the code generator. It is called from exactly two places:
`NaturalLanguagePipeline.cs:31` and `CodeGenerator.Tests/Core/CommandOutcomeTests.cs`.

Its signature changes:

```csharp
// before
public static bool ProcessInput(string input, ref ProjectMetaData? project, …, string? nlpAction = null)

// after
public static bool Execute(ParsedCommand command, ref ProjectMetaData? project, …)
```

After this change raw text cannot physically reach `CodeGenerationEngine`. The
bridge becomes the sole producer of `ParsedCommand`.

### `NlpCommandMapper` (new, in `NaturalLanguageBridge`)

Consumes `SemanticResult`, emits `ParsedCommand`. Rules, derived from the
observed output above:

- **verb** — first `Action` entity, lowercased and lemmatized (`"creates"` → `"create"`).
- **target kind** — first `Object` entity, determiner-stripped (`"a class"` → `"class"`).
- **identifiers** — the remaining entities, **in positional order**.
- `(verb, target-kind)` → `IntentType`, via one explicit table owned by the bridge.

Four behaviours the evidence forces, each handled in the bridge because each is
domain knowledge:

1. **Identifiers arrive as `EntityType.Person`.** `EntityRecognizer.Classify`
   returns `Person` for any capitalized noun-phrase head with no gazetteer hit,
   so `Customer`, `Email`, `Foo`, and `Bar` are all `Person` at 60%. In a
   code-generation context the bridge reads `Person` as "identifier". NLE is not
   changed to accommodate this.
2. **`Relationships` is empty** on every observed input. `rename class Foo to Bar`
   yields no `Foo → Bar` edge, so the mapper relies on positional order.
3. **`"called"` is tagged as a second `Action`.** Taking the *first* `Action`
   handles it — the behaviour `GetPrimaryAction` already has.
4. **Object text carries determiners** — strip leading determiners.

### Literal CLI commands bypass NLP entirely

`IntentType` has 52 members, but several are not natural language at all:
`Help`, `ShowProject`, `CurrentProject`, `ListProjects`, `ShowLearning`,
`ResetLearning`. `IntentAnalyzer` currently matches these through the same
494 lines of hint-phrase scoring as genuine prose.

These move to an exact-match command table consulted **before** the NLP path.
Cheaper, exact, and it shrinks the surface the mapper must cover.

## The coverage gate

`IntentAnalyzer.IntentHints` is a dictionary of `IntentType` → example phrases
(`"i need a class"`, `"generate model"`, `"repository for"`). It is a free,
high-quality corpus written by the code we intend to delete.

Phase 1 replays every hint phrase through `NlpCommandMapper` and reports which
`IntentType`s it fails to reach.

**The cutover is blocked until coverage is complete.** The gated step is
phase 3, not phase 4: flipping `ProcessInput(string)` to
`Execute(ParsedCommand)` is the moment every command must already route through
the mapper. Deletion in phase 4 is mechanical cleanup that follows.

Concretely, phase 3 may not begin until, for every `IntentType` currently
reachable through `IntentAnalyzer`, the characterization test proves it is
reachable through either the literal command table or `NlpCommandMapper`. No
command may become unreachable at any commit.

This makes the gate a measured condition rather than a judgement call. Phases 1
and 2 are additive — they add the mapper and its tests alongside the existing
path — so they can land independently while the old stack still serves every
command.

## Phases

1. **Measure.** Add `NlpCommandMapper` and a characterization test in
   `CodeGenerator.Tests` seeded from `IntentHints`. Nothing is deleted. Output:
   a coverage report naming every unreachable `IntentType`.
2. **Close gaps.** Lemmatization, determiner-stripping, positional identifiers.
   Move literal CLI verbs to the exact-match table. Repeat until the coverage
   gate passes. Additive: the old path still serves every command.
3. **Flip the seam** (gated on the coverage gate passing).
   `ProcessInput(string, …)` → `Execute(ParsedCommand, …)`. Update the bridge
   and the two test call sites. This is the behavioural cutover.
4. **Delete** (mechanical, once step 3 is green): `Synonyms.cs`, `TextTokenizer`,
   `IntentAnalyzer`, `EntityRecognizer`, `RelationExtractor`,
   `EnglishLanguageModel`, `SemanticAnalyzer`, `VocabularyRepository`,
   `VocabularyStore`, `SemanticPipeline`, `Tokenizer`. Build and tests green.
5. The double intent detection disappears as a consequence of step 3; no
   separate work.

## Testing

- **Characterization test (phase 1, permanent):** every `IntentHints` phrase maps
  to the `IntentType` that `IntentAnalyzer` assigns it today. This is the gate.
- **Mapper unit tests:** the four wrinkles above — `Person`-as-identifier,
  positional order for `rename A to B`, first-`Action` selection against
  `"called"`, determiner stripping.
- **Existing suites** (`CodeGenerator.Tests`, `NaturalLanguageEngine.Tests`,
  `DomainDiscovery.Tests`) must stay green at every commit.

Note: `dotnet test` leaves the CLI's `bin` stale. Rebuild `CodeGenerator` before
any end-to-end CLI verification.

## Error handling

`NlpActionProvider.Analyze` returns `null` when NLE throws or the input is
blank, and it never throws. `NlpCommandMapper` preserves that contract: a `null`
`SemanticResult`, or one the mapper cannot resolve to an `IntentType`, produces
a `ParsedCommand` with `IntentType.Unknown`, which the existing
`HandleUnknown` handler already reports as a failure. Unrecognized input keeps
behaving exactly as it does today.

## Out of scope

- **`DomainDiscovery`** is a second NLE consumer that already follows this
  pattern correctly — it depends on NLE, not the reverse. Leave it.
- **`NaturalLanguageEngine.csproj` is `OutputType=Exe`** to support its demo
  `Program.cs`. Converting it to a library is an unrelated cleanup.
- `Program.cs` writes `prompts.json` to the working directory as a side effect
  of running the demo. Noted, not addressed here.

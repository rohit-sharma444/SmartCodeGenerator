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

The bridge becomes the sole producer of `ParsedCommand`, and
`CodeGenerationEngine` stops *analyzing* text.

**Precise claim.** The original sentence does not vanish from
`CodeGenerationEngine` — three handlers still slice it:
`ProjectManager.ListClassesInProject(ctx.RawInput)` (line 90),
`ProjectManager.UseProject(ctx.RawInput)` (line 95), and
`EntityExtractor.ExtractClassName(ctx.RawInput, command)` (line 309). It travels
inside `ParsedCommand.OriginalSentence`, which already exists.

Retiring those three is the mapper's job: it fills `ProjectName` and `ClassName`
so the handlers read fields instead of re-parsing prose. Until then, the
sentence is carried as provenance, not re-analyzed. Also note
`ProcessInput` short-circuits the literal string `"demo"` before parsing — that
belongs in the literal command table.

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

### `EntityExtractor` is the dominant cost

`Core/EntityExtractor.cs` is **1,238 lines**. `EntityExtractor.Extract(semantic,
intent, workspace)` reads `semantic.NormalizedText` and regex-extracts class
names, property lists, method signatures, inheritance, and interface names. It
is pure code-generation domain extraction driven by prose, and under the rule in
this spec it belongs in the bridge, not in `CodeGenerationEngine`.

It also calls `TextTokenizer.Tokenize`, so `TextTokenizer` cannot be deleted
until `EntityExtractor` moves.

This is larger than the intent mapper and independently testable, so **the work
splits into two plans**:

- **Plan 1 — intent classification.** `NlpCommandMapper` resolves
  `SemanticResult` → `IntentType`, measured against the coverage gate. Purely
  additive; the existing path keeps serving every command. This is the plan that
  produces the coverage number.
- **Plan 2 — entity extraction, cutover, deletion.** Move `EntityExtractor` to
  the bridge and rebase it on NLE entities, flip the seam, delete the duplicate
  stack.

Plan 2 cannot be written responsibly until Plan 1 reports coverage: if the
mapper reaches only a fraction of the 24 intents, Plan 2's shape changes. Plan 1
is a prerequisite, not merely a first step.

One hazard to record: `EntityExtractor.Extract` mutates
`SessionContext.CurrentClass` as a side effect of extraction. Moving it changes
where that write happens; Plan 2 must preserve the ordering.

### Literal CLI commands bypass NLP entirely

Several intents are not natural language at all. `IntentAnalyzer.Detect(string)`
resolves them with regex guards that return score 999 before any scoring runs
(`Core/IntentAnalyzer.cs:100-176`):

| Guard | Intent |
|---|---|
| `^\w+\s+(inherit\|inherits\|extends)\s+\w+$` | `ClassInherit` |
| `^(show\|list\|view)\s+learning$` | `ShowLearning` |
| `^(reset\|clear\|forget)\s+learning$` | `ResetLearning` |
| `TryDetectClassListing` | `ListClasses` / `ListClassesInProject` |
| `\bproject\s+report\b` | `GenerateDocumentation` |
| `\b(create\|new\|generate\|make\|scaffold)\b.*\bclass\s+lib(rary)?\b` | `CreateProject` |
| `\b(add\|create)\s+(a\|an\|new\s+)?property\b` | `CreateProperty` |
| `\b(remove\|delete\|drop)\s+property\b` | `DeleteProperty` |
| `\b(list\|show\|view)\s+propert(y\|ies)\b` | `ListProperties` |
| `\badd\s+validation\b` | `AddValidation` |
| `\b(show\|list\|view)\s+validation\b` | `ShowValidation` |
| `\bvalidator\b` | `GenerateValidator` |

Plus the literal `"demo"` short-circuit in `ProcessInput`.

These move to an exact-match command table consulted **before** the NLP path.
Cheaper, exact, and it shrinks the surface the mapper must cover. Several exist
only to stop the scorer misfiring (`"class list"` was being read as `CreateClass`
and generating a class named `List`); once intent comes from NLE's parsed verb
and object rather than substring scoring, those guards should become
unnecessary. Plan 1 records which ones are still load-bearing.

## The coverage gate

**Corpus source.** `IntentRepository.GetDefaultRules()` is a static list of
`IntentRule { Intent, Priority, Actions[], Targets[] }` covering **31**
`IntentType`s. The `Actions × Targets` cross-product generates phrases with a
known expected intent (`["create","add","generate"] × ["class"]` →
`CreateClass`). This is the corpus.

(`IntentAnalyzer.IntentHints` is *not* the corpus. It covers only 7 intents
across 34 phrases and is referenced once, in `GetEnglishHintScore` at
`Core/IntentAnalyzer.cs:360`, where it contributes a +60 scoring bonus. It is
not the detection table.)

**Coverage target.** `IntentType` has 52 members, but that is not the target.
Cross-referencing the rule table against the dispatch table in
`CommandHandlingService`:

- **24 intents have both a default rule and a handler.** These are the gate.
- **7 have a handler but no rule** — `AddValidation`, `GenerateValidator`,
  `ResetLearning`, `ShowLearning`, `ShowProject`, `ShowValidation`, `Unknown`.
  These are reached by the regex guards, and move to the literal command table.
- **7 have a rule but no handler** — `ClassInherit`, `CreateApi`,
  `CreateController`, `CreateRepository`, `CreateService`, `RenameProperty`,
  `ValidateProject`. These are dead today: `IntentAnalyzer` resolves them and
  `Dispatch` prints "not supported yet". `CommandOutcomeTests` pins this,
  asserting `create a repository for Customer` returns `false`. **The mapper
  must preserve this**, not accidentally fix it.

So the gate is 24 intents via the mapper plus 6 via the literal table — not 52.

Phase 1 replays the cross-product through `NlpCommandMapper` and reports which
of the 24 it fails to reach.

**The cutover is blocked until coverage is complete.** The gated step is the
seam flip (phase 4), not the deletion (phase 5): flipping `ProcessInput(string)`
to `Execute(ParsedCommand)` is the moment every command must already route
through the mapper. Deletion is mechanical cleanup that follows.

Concretely, phase 4 may not begin until the characterization test proves that
each of the 24 gate intents is reachable through `NlpCommandMapper`, and each of
the 6 guard intents through the literal command table. No command may become
unreachable at any commit.

This makes the gate a measured condition rather than a judgement call. Plan 1
(phases 1–2) is additive — it adds the mapper and its tests alongside the
existing path — so it lands independently while the old stack still serves every
command.

## Phases

### Plan 1 — intent classification (additive, no deletion)

1. **Measure.** Add `NlpCommandMapper` (`SemanticResult` → `IntentType`) and a
   characterization test in `CodeGenerator.Tests` seeded from the
   `Actions × Targets` cross-product of `IntentRepository.GetDefaultRules()`.
   Output: a coverage report naming every one of the 24 gate intents the mapper
   cannot reach.
2. **Close gaps.** Lemmatization, determiner-stripping, positional identifiers.
   Add the literal command table. Repeat until the coverage gate passes.

Throughout, the existing path serves every command; nothing is removed. Plan 1
ends with a coverage number and a passing gate.

### Plan 2 — extraction, cutover, deletion (written after Plan 1 reports)

3. **Move `EntityExtractor`** to the bridge, rebased on NLE entities. Preserve
   the `SessionContext.CurrentClass` write ordering. Retire the three
   `ctx.RawInput` handler call sites.
4. **Flip the seam.** `ProcessInput(string, …)` → `Execute(ParsedCommand, …)`.
   Update the bridge and the two test call sites. This is the behavioural
   cutover, and it is what the coverage gate guards.
5. **Delete** (mechanical, once step 4 is green): `Synonyms.cs`, `TextTokenizer`,
   `IntentAnalyzer`, `EntityRecognizer`, `RelationExtractor`,
   `EnglishLanguageModel`, `SemanticAnalyzer`, `VocabularyRepository`,
   `VocabularyStore`, `SemanticPipeline`, `Tokenizer`. Build and tests green.
6. The double intent detection disappears as a consequence of step 4; no
   separate work.

The seven rule-but-no-handler intents stay dead. Preserving that is a
requirement, not an oversight.

## Testing

MSTest throughout (`CodeGenerator.Tests` runs with
`[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`).

- **Characterization test (phase 1, permanent):** every `Actions × Targets`
  phrase from `GetDefaultRules()` maps to that rule's `Intent`. This is the gate.
  The corpus is copied into the test project as a fixture rather than read from
  `IntentRepository`, so it survives phase 5's deletion.
- **Mapper unit tests:** the four wrinkles above — `Person`-as-identifier,
  positional order for `rename A to B`, first-`Action` selection against
  `"called"`, determiner stripping.
- **Regression pin:** `create a repository for Customer` still returns `false`
  (`CreateRepository` has no handler).
- **Existing suites** (`CodeGenerator.Tests`, `NaturalLanguageEngine.Tests`,
  `DomainDiscovery.Tests`) must stay green at every commit.

Two environment hazards, both previously observed:

- `dotnet test` leaves the CLI's `bin` stale. Rebuild `CodeGenerator` before any
  end-to-end CLI verification.
- `IntentRepository` persists to `intents.json` in `AppDomain.BaseDirectory` and
  *merges* accumulated rules into the defaults, so `GetAll()` at runtime is not
  `GetDefaultRules()`. The corpus must be built from `GetDefaultRules()`, and
  tests must not depend on a stale `intents.json` in the test `bin`.

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

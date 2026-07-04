# Design: Augment CodeGenerator intent detection with NaturalLanguageEngine

**Date:** 2026-07-04
**Status:** Approved design, pending implementation plan

## Goal

Integrate the `NaturalLanguageEngine` (NLE) project into the `CodeGenerator`
project by using NLE to **strengthen intent detection**. The integration is an
*augmentation*: NLE adds a signal to CodeGenerator's existing rule-based intent
scoring. It does **not** replace CodeGenerator's `SemanticAnalyzer`,
`IntentAnalyzer`, or `EntityExtractor`, and it does **not** change domain
entity extraction (class names, typed properties, methods, project types).

### Non-goals

- Replacing CodeGenerator's NLP pipeline with NLE.
- Using NLE for entity recognition, normalization/lemmatization, or
  confidence/diagnostics surfacing. (Explicitly out of scope for this change.)
- Any structural change to NLE (its pipeline, stages, or public API). The **only**
  NLE change is a data addition to its English verb lexicon (see below).

### Empirical finding driving the design (2026-07-04)

Running NLE's own console (`EngineBuilder.Build`) against real CodeGenerator
commands showed NLE's POS tagger only tags a verb as an `Action` when that verb
is present in its lexicon (`EnglishLanguageProvider.LoadWords`, "Common verbs").
That "compact lexicon" is missing most command verbs:

| Command | NLE result as-shipped |
|---------|-----------------------|
| `create a Customer class` | `CreateIntent`, Action `create` (works) |
| `add method Save to Order` | `DescribeIntent`, no Action |
| `list projects` | no intent, `NoEntities` |
| `generate a service for Payment` | `DescribeIntent`, no Action |
| `delete class Foo` | `DescribeIntent`, no Action |
| `rename class A to B` | `DescribeIntent`, no Action |

Adding the missing verbs to the lexicon (verified by re-running the same
commands) makes every command tag correctly: `AddIntent`/`ListIntent`/
`GenerateIntent`/`DeleteIntent`/`RenameIntent`/`AnalyzeIntent`, each with the
Action verb at 0.80 confidence. These are ordinary English verbs a
general-purpose engine should know, so the addition improves NLE broadly rather
than making it CodeGenerator-specific. Without this addition the action signal
is too sparse to be worth wiring in; **the lexicon extension is a prerequisite,
not optional.**

## Background

CodeGenerator parses a command through one pipeline
(`CommandHandlingService.ParseInput`):

1. `SemanticAnalyzer.Analyze(input)` -> `SemanticModel`. Internally this calls
   `IntentAnalyzer.Detect(input)` (the **string** overload, keyword scoring) and
   stores the result on `model.Intent`.
2. `IntentAnalyzer.Detect(SemanticModel)` -> `IntentType`. This just reads back
   `semantic.Intent`.
3. `EntityExtractor.Extract(...)` -> `ParsedCommand`.

Therefore **all intent logic funnels through one method:**
`IntentAnalyzer.DetectWithConfidence(string sentence)`. It scores every
`IntentRule` (each rule has `Actions`, `Targets`, `Keywords`, `Priority`) and
picks the best; scores below 40 collapse to `IntentType.Unknown`.

NLE exposes `ISemanticAnalyzer.Analyze(string) -> SemanticResult`. `SemanticResult`
carries a generic `Intent { Name, Confidence }` (verb-derived, e.g.
`"CreateIntent"`) and generic `Entities` (Person/Org/Location/Date/Quantity/
Action/...). The action verb is exposed as the **surface form** of the first
`Action` entity (`EntityRecognizer` uses `Token.Text`, not the lemma), and
`IntentRecognizer` names the intent `"{Verb}Intent"`.

### Honest value-add

NLE is a general-purpose English NLP engine; its generic intents/entities cannot
produce a domain `ParsedCommand`. What it *can* contribute to intent detection is
a **POS-confirmed primary action verb**: the engine identifies which token is
genuinely the command's action verb (via POS tagging + phrase detection), rather
than CodeGenerator's current "does any action keyword appear as a substring
anywhere in the sentence." We do **not** claim lemmatization value, because the
lemma is not exposed through NLE's public `SemanticResult`.

## Approach

### 0. Extend NLE's verb lexicon (prerequisite)

In `NaturalLanguageEngine/Knowledge/Providers/EnglishLanguageProvider.cs`, add
the missing base/third-person/past forms of CodeGenerator's command verbs to the
"Common verbs" array in `LoadWords()`:

```
add/adds/added, list/lists/listed, generate/generates/generated,
delete/deletes/deleted, remove/removes/removed, rename/renames/renamed,
analyze/analyzes/analyzed, validate/validates/validated, use/uses/used,
restore/restores/restored
```

Verbs already present (`create`, `build`, `show`, `open`, `set`, `find`, `need`,
`make`, `run`) are left as-is. This is a pure data addition; no NLE logic
changes. NLE's existing xUnit test suite must still pass afterward.

### 1. Project reference & seam

- Add a `ProjectReference` from `CodeGenerationEngine.csproj` to
  `NaturalLanguageEngine.csproj`. NLE stays an `Exe`; referencing an Exe project
  is supported by the .NET SDK and the two entry points do not conflict. NLE is
  left untouched and remains runnable standalone.
- Introduce a single adapter, `Core/Nlp/NlpActionProvider.cs`, in
  `CodeGenerationEngine`, that fully isolates the NLE dependency. Public surface:

  ```csharp
  public static class NlpActionProvider
  {
      // Returns the POS-tagged primary action verb (lowercased), or null.
      public static string? GetPrimaryAction(string input);
  }
  ```

  Internally:
  - Holds a lazily-built singleton analyzer:
    `private static readonly Lazy<ISemanticAnalyzer> Analyzer = new(() => EngineBuilder.BuildDefault().Analyzer);`
    (The knowledge-base build is not free; build once, reuse across the REPL.)
  - Calls `Analyzer.Value.Analyze(input)`, reads the verb from the first
    `Action` entity (fallback: strip the `"Intent"` suffix from
    `result.Intent.Name`), lowercases it, and returns it.
  - Every NLE type stays inside this file; the rest of `CodeGenerationEngine`
    only ever sees a `string?`.

### 2. Blend mechanics (in `IntentAnalyzer.DetectWithConfidence(string)`)

- Resolve the NLE action once per call, before the rule loop:
  `var nlpAction = NlpActionProvider.GetPrimaryAction(sentence);`
- Thread `nlpAction` into `CalculateScore(...)` and add one new **purely
  additive** contribution, `GetNlpActionScore(nlpAction, rule)`:
  - If `nlpAction` is non-null and case-insensitively equals one of
    `rule.Actions`, award a fixed bonus (proposed **+25**, in line with the
    existing `ContainsPhrase` action bonus of +30 / phrase bonus of +25).
  - Otherwise award 0.
- **Additive bonus only** (confirmed decision): NLE can only *raise* a matching
  rule's score. It never overrides keyword scoring and never rescues an
  otherwise-`Unknown` result. If `nlpAction` is null or unrecognized, every
  score is identical to today's behavior, so the change **cannot regress** any
  existing command by construction.

### 3. Failure handling & lifecycle

- All NLE interaction is wrapped in try/catch inside `NlpActionProvider`. NLE
  throws `ArgumentException` on null/empty/whitespace input, so guard that up
  front and return `null`. Any exception -> return `null` -> scoring untouched.
- The analyzer is a lazy singleton. The console REPL is single-threaded, so
  there is no contention; `Lazy<T>` default thread-safety is sufficient.

## Components

| Component | Change | Responsibility |
|-----------|--------|----------------|
| `NaturalLanguageEngine/Knowledge/Providers/EnglishLanguageProvider.cs` | edit | Add missing command verbs to the lexicon (prerequisite) |
| `CodeGenerationEngine.csproj` | edit | Add `ProjectReference` to NLE |
| `CodeGenerator.Tests.csproj` | edit | Add `ProjectReference` to CodeGenerationEngine (no reference exists yet) |
| `Core/Nlp/NlpActionProvider.cs` | new | Isolate NLE; expose `GetPrimaryAction(string) : string?` |
| `Core/IntentAnalyzer.cs` | edit | Resolve `nlpAction` once; add additive `GetNlpActionScore` contribution to `CalculateScore` |

The only NLE change is the lexicon data addition above.

## Data flow

```
input
  -> IntentAnalyzer.DetectWithConfidence(sentence)
       nlpAction = NlpActionProvider.GetPrimaryAction(sentence)   // NLE, once
         -> EngineBuilder singleton -> ISemanticAnalyzer.Analyze  // POS verb
       for each IntentRule:
         score = ScoreActions + ScoreTargets + PhraseBonus
               + EnglishHintScore + ContextScore
               + GetNlpActionScore(nlpAction, rule)               // NEW, additive
       pick best; < 40 -> Unknown
```

## Testing (CodeGenerator.Tests)

1. **Regression** — a table of existing phrasings ("create a Customer class",
   "add method Save to Order", "list projects", ...) still resolve to the same
   `IntentType` as before.
2. **Value-add** — a phrasing where the confirmed action verb tips a previously
   ambiguous/weak case to the correct intent.
3. **Adapter** — `NlpActionProvider.GetPrimaryAction` returns the expected verb
   for representative commands, and returns `null` gracefully for empty /
   whitespace / non-actionable input (no throw).

## Risks & mitigations

- **Startup cost of building the NLE engine** — mitigated by the lazy singleton
  (built on first command, reused thereafter).
- **NLE returns a verb that collides with the wrong rule's action** — bounded by
  the additive-only design and the modest +25 bonus; it can nudge but not
  single-handedly select an intent against otherwise-zero keyword evidence.
- **Referencing an Exe project** — validated as supported; entry points are
  per-assembly and do not conflict.

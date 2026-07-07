# Design: Make free-form descriptions extract seed entities

**Date:** 2026-07-07
**Status:** Approved (pending spec review)
**Area:** NaturalLanguageEngine → DomainDiscovery `build app:` pipeline

## Problem

Typing a natural-language description at `build app:` (e.g. `payroll application`)
produces a **0-entity candidate model** — the discovery pipeline degrades to
seed-only with no recognized topic and nothing to review.

## Root cause

The blocker is part-of-speech tagging of unknown words, not just a short lexicon:

1. `payroll` and `application` are lowercase and absent from the bundled
   ~185-word lexicon (`EnglishLanguageProvider`).
2. `PosTagger.InferFromPattern` only infers `Noun` for **capitalized** words;
   any other unrecognized word that isn't a number or a `-ed`/`-ing` verb
   falls back to `PartOfSpeech.Unknown`.
3. `PhraseDetector.TryReadNounPhrase` builds noun phrases only from
   `Noun`/`Pronoun` heads, so `Unknown` tokens are skipped entirely.
4. No phrases → no entities → 0 seed entities.

Note: a lowercase noun-phrase head already classifies as `EntityType.Object`
(which `SeedEntityExtractor` keeps) **regardless of the lexicon** — so the fix
belongs at the POS step, with the lexicon as a precision aid.

## Design

Two complementary changes.

### 1. POS-tagger fallback (`PosTagger.InferFromPattern`)

Change the final default for **`Word`-type tokens** from `Unknown` to `Noun`.
Non-word tokens (`Punctuation`, `Symbol`, etc.) keep `Unknown`, so punctuation
never becomes a noun. This is the standard "unknown word ≈ noun" baseline and is
what lets arbitrary lowercase common nouns (`payroll`, `application`,
`inventory`, `tracker`, …) flow through `PhraseDetector` into `Object` entities.

Existing ordered heuristics are preserved and still run first:
numeral → capitalized-Noun → `-ed`/`-ing`-Verb → **(new) Word-Noun / else Unknown**.

The guard uses `TokenAnalysis.Token.Type == TokenType.Word`.

### 2. Curated business vocabulary (`EnglishLanguageProvider.LoadWords`)

Add ~40–60 common business-domain nouns (e.g. payroll, employee, salary, wage,
invoice, payment, customer, client, supplier, product, order, account,
transaction, department, manager, timesheet, tax, deduction, expense, budget,
inventory, warehouse, shipment, user, role, application, system, …). Two reasons
beyond the fallback:

- **Precision** — known words get an exact noun tag and become available to
  lemmatization/synonyms later.
- **Rescues `-ing`/`-ed` nouns** — the verb-suffix heuristic runs *before* the
  Word-Noun default, so nouns like `billing`, `shipping`, `accounting`,
  `invoicing`, `onboarding` would otherwise be mis-tagged as **verbs** and
  dropped from noun phrases. Adding them to the lexicon fixes that. (This is why
  the fallback alone is insufficient.)

## Scope boundaries (YAGNI)

- **Not** changing `WikipediaTopicRecognizer` (the per-term vs. single-phrase
  search improvement noted separately) — seed extraction is the blocker here.
- **Not** adding code generation from the approved model — still a later phase.
- **Not** expanding verbs/adjectives/other POS classes — nouns only.

## Testing & regression gate

- Update `Tag_UnknownLowercaseWord_FallsBackToUnknown` to assert the new `Noun`
  behavior (rename to reflect it).
- Add unit tests:
  - unknown `Word` token → `Noun`;
  - unknown `Punctuation`/`Symbol` token → `Unknown` (guard holds);
  - a `-ing` business noun (`billing`) → `Noun` via the lexicon.
- Add a DomainDiscovery-level test: `"payroll application"` yields ≥1 seed
  entity (via `SeedEntityExtractor` over a real NLE analysis).
- **Regression gate:** run the full `NaturalLanguageEngine.Tests`,
  `DomainDiscovery.Tests`, and `CodeGenerator.Tests` suites. The fallback change
  is broad and can shift phrase detection in existing sentences — this is the
  primary risk and must pass before merge.

## Files touched

- `NaturalLanguageEngine/Syntax/PosTagger.cs` — Word-Noun fallback.
- `NaturalLanguageEngine/Knowledge/Providers/EnglishLanguageProvider.cs` —
  business nouns.
- `NaturalLanguageEngine.Tests/Syntax/PosTaggerTests.cs` — update + new tests.
- `DomainDiscovery.Tests/` — new seed-extraction test for a free-form description.

## Risks

- **Over-tagging:** unknown words that are genuinely verbs/adjectives now tag as
  nouns (e.g. `manage payroll` → two `Object` entities). Acceptable — the review
  loop lets the user `remove` spurious entities, and it beats a 0-entity model.
- **Test drift:** existing NLE/bridge tests may assert behavior that depended on
  unknown words being dropped; the regression gate covers this.

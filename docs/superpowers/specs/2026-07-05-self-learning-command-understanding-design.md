# Self-Learning Command Understanding — Design

Date: 2026-07-05
Status: Approved (pending spec review)
Area: `CodeGenerator/CodeGenerationEngine` (intent detection) + `NaturalLanguageEngine`

## 1. Goal

Make the natural-language command engine improve from normal usage: it should
learn which interpretation of a command was correct and get better at
classifying similar commands over time — **without** the drift that the
accumulated `intents.json` already caused (e.g. `list classes` losing to
`GenerateComments`, `class list` read as `CreateClass`).

Two learning behaviors, both driven by usage (no explicit "teach" step required):

1. **Learned intent memory** — associate a command's phrase signature with the
   intent that succeeded; reinforce on success, weaken on failure / quick
   rephrase.
2. **Vocabulary learning** — record unknown words as the engine encounters them,
   wiring up the existing but currently-unused `IntentLearningEngine`.

## 2. Guiding principle

**Learning nudges; it never dictates.** The deterministic guards added earlier
(`create … class` → `CreateClass`, `list/show classes` / `class list` →
`ListClasses`, direct inheritance) and the base scored rules always win. Learned
memory can only tip *borderline* cases. Its maximum influence is capped strictly
below the weight of a deterministic guard, so learning can never override a
confident base decision.

## 3. Current state (what exists)

- `IntentAnalyzer.DetectWithConfidence(sentence, nlpAction)` — deterministic
  guards, then scored rules from `IntentRules.Rules => IntentRepository.GetAll()`
  (loaded/accumulated from `intents.json` in the exe's base dir).
- `ILearningEngine` (NLE) — interface `Learn`/`Correct`, **no implementation, no
  callers**.
- `IntentLearningEngine.Learn(sentence, command)` (CodeGen) — stores unknown
  tokens into `VocabularyRepository`. **Never called.**
- `PromptStore` (NLE) — persists prompts; not used for learning decisions.

The learning layer builds on these rather than replacing them; base `intents.json`
rules are left pristine.

## 4. Components

### 4.1 `LearnedIntentMemory` (new, `CodeGenerationEngine.Core`)

Separate store, independent of the base rules.

- **Entry**: `{ Signature, Intent, SuccessCount, FailureCount, Confidence, LastUsedUtc }`.
- **Signature**: a canonical form of the normalized command — salient tokens
  (stop/keyword-filtered) sorted and joined — so `class list` and `list class`
  map to the same signature. Deliberately coarse to generalize across phrasings.
- **API**:
  - `IntentType? Suggest(string sentence, out double confidence)` — best learned
    intent for the signature, or null.
  - `void Reinforce(string sentence, IntentType intent)` — success.
  - `void Weaken(string sentence, IntentType intent)` — failure / rephrase.
  - `void Decay()` — apply time-based decay on load.
  - `IReadOnlyList<...> Entries` — for `show learning`.
  - `void Reset()` — clear all.
- **Confidence**: `success / (success + failure)`, scaled by a saturating factor
  on total observations, clamped to `[0, 1]`. Old entries decay toward 0.

### 4.2 `IntentAnalyzer` integration

New order inside `DetectWithConfidence`:

1. Deterministic guards (unchanged) — return immediately if matched.
2. Base scored rules (unchanged) → `(baseIntent, baseScore)`.
3. **Learned nudge**: if `LearnedIntentMemory.Suggest` returns an intent with
   confidence ≥ `LearnThreshold`, add a bounded bonus
   `min(LearnedBonusCap, confidence * LearnedBonusCap)` to *that* intent's score,
   then re-pick the winner. `LearnedBonusCap` is strictly below the guard/context
   weights (e.g. 35, vs the +120 create-class guard) so it can only change the
   outcome when the base scores are close.

Learning is consulted only when enabled (see §7).

### 4.3 Feedback capture (`CommandHandlingService` / pipeline)

After a command runs, the outcome is known:

- Handled successfully (intent recognized and its handler returned success) →
  `Reinforce(sentence, detectedIntent)` and run vocabulary learning.
- `Unknown`, unsupported, or handler failure → `Weaken(sentence, detectedIntent)`.
- **Quick rephrase**: keep the last `(sentence, intent, timestampUtc)`. If the
  next command arrives within `RephraseWindow` (e.g. 20s) and its signature is
  highly similar (token-overlap ≥ 0.6) but not identical, treat the previous as a
  misfire → `Weaken(previousSentence, previousIntent)`.

Feedback is recorded through a single `LearningCoordinator` so the pipeline has
one call site and the policy lives in one place.

### 4.4 Vocabulary learning

On successful commands, call `IntentLearningEngine.Learn(sentence, command)`
(already implemented) so unknown tokens accumulate in `VocabularyRepository` with
bounded confidence. This is the "intent + vocabulary" scope chosen.

## 5. Data flow

```
command
  → NLE analysis (nlpAction)
  → IntentAnalyzer:
       deterministic guards ─(match)→ intent
       else base scoring → learned nudge → intent
  → handler → outcome
  → LearningCoordinator:
       success → Reinforce + vocabulary learn
       failure/Unknown → Weaken
       quick-rephrase of prior → Weaken(prior)
  → persist
```

## 6. Persistence

- **Location**: `<workspace>/.codegen/learned.json` (intent memory) and the
  existing vocabulary store, **per-workspace**, NOT the `bin` folder. This avoids
  the fragility of the accumulated `intents.json`-in-`bin` and keeps learning
  scoped and discoverable.
- Base `intents.json` rules are untouched.
- Written after each feedback event (small file) or on a debounced flush.

## 7. Guardrails against drift

- **Capped influence**: `LearnedBonusCap` < deterministic guard weights; learning
  can never override a confident base/guard decision.
- **Decay**: confidence decays over time and on failures, so stale/bad
  associations fade instead of compounding.
- **Deterministic guards always win**: the create-class / list-classes /
  inheritance guards short-circuit before learning is consulted.
- **On/off switch**: learning is **on by default**; setting `CG_NO_LEARN=1`
  (env var) disables both consultation and capture.
- **Inspect**: `show learning` prints learned signatures, their intent, counts,
  and confidence.
- **Reset**: `reset learning` clears the learned intent memory (and optionally the
  learned vocabulary).

## 8. New commands

Two exact-match commands (added as intents with deterministic detection, so they
themselves are never misclassified or learned over):

- `show learning` → `ShowLearning` intent → prints the learned memory.
- `reset learning` → `ResetLearning` intent → clears it, prints confirmation.

## 9. Testing

- **Unit (`LearnedIntentMemory`)**: reinforce raises confidence; weaken lowers it;
  decay reduces stale entries; confidence clamped `[0,1]`; signature canonicalizes
  word order (`class list` == `list class`); persistence round-trips.
- **Unit (`IntentAnalyzer` nudge)**: a borderline base decision flips after the
  learned intent crosses threshold; a deterministic guard is **never** overridden
  regardless of learned confidence (`create class Foo` stays `CreateClass` even if
  `CreateMethod` is heavily reinforced for a similar phrase).
- **Integration (feedback)**: a successful command reinforces its intent; an
  `Unknown`/failed command weakens it; a quick rephrase weakens the prior intent.
- **Control**: `CG_NO_LEARN=1` disables consult + capture; `reset learning`
  empties the store; `show learning` reflects recorded entries.

## 10. Out of scope (this version)

- Entity-extraction learning (types, property/method phrasings).
- Manual `teach`/`forget` commands (only `show`/`reset`).
- LLM-based interpretation.
- Cross-workspace/global sharing of learned data.

## 11. Open risks

- Coarse signatures may over-generalize; mitigated by the capped bonus and the
  fact that guards/base rules dominate.
- Quick-rephrase heuristic can mis-fire on legitimately different follow-up
  commands; mitigated by requiring high token overlap and a short window, and by
  the bounded effect of a single weaken.

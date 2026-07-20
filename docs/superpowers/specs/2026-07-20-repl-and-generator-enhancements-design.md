# REPL & generator enhancements — design

**Date:** 2026-07-20
**Status:** Approved (design)
**Branch:** `feat/repl-and-generator-enhancements`

## Goal

Seven independent enhancements to the interactive CodeGenerator REPL, the code
generator, and the domain-discovery recognizer. Each is implemented **one at a
time** as its own test-driven commit on a single branch. Items are ordered so
the low-risk, high-value REPL fixes land first and the deeper generator/NLE
changes last.

Full `dotnet test` must be green before every commit. Per prior lessons, the
CodeGenerator project is rebuilt before any CLI-level verification (test runs
leave the CLI's `bin` stale).

---

## 1. Feedback on unrecognized commands

**Problem.** `CodeGeneratorStarter.Run()` calls `_pipeline.Handle(...)` and
**discards** the returned `PipelineOutcome.Handled` flag. When the NLE does not
understand the input, the loop simply reprints the prompt — the user gets no
signal that nothing happened. `RunOnce` captures the flag but likewise prints
nothing on failure.

**Design.** Capture the outcome in `Run()`. When `Handled == false` (and the
input was not already consumed by `exit`/`clear`/`build app:`/`batch`), print:

```
Command not recognized: '<input>'. Type 'help' for the command list.
```

followed by the did-you-mean line from item #2 when there are close matches.
`RunOnce` prints the same "not recognized" message before returning `false`.

**Boundaries.** The message is emitted only for genuinely unhandled input;
blank input continues silently (matching today's behavior). No change to the
pipeline contract — we only stop ignoring its result.

**Testing.** The message-building is extracted to a pure, testable helper
(`UnrecognizedCommand.Describe(input, suggestions)`) so the wording and the
"with/without suggestions" branches are unit-tested without a console.

---

## 2. "Did you mean?" for commands

**Problem.** Type-name typos get suggestions (`TypeValidator`: "Did you mean
'datetime'?"), but command typos (`craete class`, `lst projects`) get nothing.
`GetCommandHints` only does `StartsWith`/`Contains`, which cannot catch
transpositions or a wrong leading character.

**Design.** Add a small, pure Levenshtein helper and a
`CommandSuggester.Closest(input, max)` that ranks `CommandDictionary.Commands`
by edit distance against the first token / whole line, returning the top few
within a distance threshold (≤ 3, tuned so `craete`→`create` matches but
unrelated words do not). Used by item #1 to fill the did-you-mean line.

**Testing.** Levenshtein correctness (known pairs); `Closest` returns the
expected command for representative typos and returns empty for gibberish that
is far from every command.

---

## 3. Delete dead suggestion stack

**Problem.** Two parallel command-suggestion systems exist. The live one is
`CommandDictionary.Commands` (used by `CommandLineEditor`). The other —
`InteractiveConsole` + `CommandSuggestionService` + `CommandCatalog` — has
**zero live references** (`InteractiveConsole` is never instantiated) and its
30-command catalog has already drifted from the ~40 in `CommandDictionary`.

**Design.** Delete `InteractiveConsole.cs`, `CommandSuggestionService.cs`,
`CommandCatalog.cs`, and any tests that exercise only those types. Confirm the
build and full suite stay green (proves they were dead).

**Boundaries.** If a test references these types for shared helpers, that test
is re-pointed at `CommandDictionary`/`CommandSuggester` rather than kept alive.

---

## 4. `history` command

**Problem.** History is persisted and `↑`/`↓`-navigable, but there is no way to
*view* it.

**Design.** Add `HistoryCommand.IsHistory(input)` (mirrors `ClearCommand`),
recognizing `history` (exact, case-insensitive, trimmed). Dispatched in `Run()`
alongside `clear`: prints the in-memory `_commandHistory` as a numbered list
(oldest→newest), capped at the last 20 entries, then `continue`s. Registered in
`CommandDictionary` under Utility. **Excluded from history persistence** (added
to `ShouldRecordInHistory` beside `clear`/`cls`): `history` is a transient
display command, so repeated presses should not flood `↑` recall.

**Testing.** `IsHistory` recognition table (like `ClearCommandTests`); the
formatting of the numbered list is extracted to a pure helper
(`HistoryView.Format(entries, max)`) and unit-tested (empty, < max, > max).

---

## 5. `cls` TAB-completable

**Problem.** Only canonical `clear` is in `CommandDictionary`, so `cl`+TAB
surfaces `clear` but never the advertised alias `cls`.

**Design.** Add a `cls` entry to `CommandDictionary` under Utility
(`Description: "Alias for clear (clears the screen; history is kept)."`). This
is the smallest change and keeps `GetCommandHints` untouched. `help` will list
both, which is acceptable and honest.

**Testing.** A `CommandDictionary` test asserting both `clear` and `cls` are
present and that a `cl` prefix hint returns both.

---

## 6. Richer, compile-safe generated method bodies

**Problem.** `MethodBodyGenerator` emits `// TODO` stubs for Get/Update/Delete/
Exists/Count/GetById/FindBy. The GetById/FindBy comments reference a `_items`
field.

**Constraint (decisive).** `MethodBodyGenerator.Generate` is called **only**
from `RoslynClassGenerator.CreateMethod`, on the **entity class itself**
(`model.Name` + `model.Properties`). That class has no repository/backing
collection, so emitting real `_items.FirstOrDefault(...)` would reference a
non-existent field and **break compilation** (`GeneratedProjectBuildTests`).

**Design.** Keep every body **guaranteed-compilable** while making the guidance
signature- and property-aware:

- Replace bare `// TODO: Implement retrieval logic` etc. with a short,
  specific comment naming the method's intent and the parameters/return type it
  was given, followed by the existing valid default return.
- For `GetById`/`FindBy`, keep the example as a comment (it already is) but make
  it reference the actual entity/property/parameter names consistently, and
  ensure the trailing statement is a valid default for the declared return type.
- `Update`/`Delete`/`Exists`/`Count` gain intent-specific comments plus a valid
  default (`Delete`/`Update` may be `void` → no return; `Exists` → `false`;
  `Count` → `0`).

No body may emit a statement referencing an undeclared symbol. The output stays
a clear stub the developer fills in — just a more informative one.

**Testing.** `MethodBodyGeneratorTests` (new/extended): for each recognized
verb, assert (a) the body contains the improved guidance, and (b) the emitted
lines parse and the non-comment statements are valid for the return type
(no `_items`-style references). A round-trip through `RoslynClassGenerator` on a
sample model still compiles (guarded by the existing generated-build test
patterns).

---

## 7. Disambiguation-recovery via broader detection

**Problem (as it actually stands).** `WikipediaTopicRecognizer` already refuses
disambiguation pages (`RemoveUnusableTopics` drops them, `SelectBest` then picks
the best of the rest — the recovery path exists). The real gap is **detection
coverage**: `DisambiguationLead` is `\brefers?\s+to:\s*$`, which matches
"X may refer to:" but **misses** common Wikipedia forms that do not end exactly
on "to:", e.g. "X may refer to any of the following:" (ends on "following:") and
"X may mean:". Such pages are not refused, can win the vote, and enrich to
nothing — the documented "one-word seeds land on disambiguation pages" failure.

**Design.** Broaden the disambiguation-lead recognition (a small set of anchored
patterns, still tight enough not to catch ordinary prose that merely says what a
term "refers to"):

- `refers?/means?/stands? for ... :` closing forms, including a trailing
  "any of the following:" / "the following:" tail.
- Keep it a single compiled regex (or a tiny ordered set) with the same
  end-anchored discipline as today, so a real definitional sentence
  ("A point is ...") is never matched.

The recovery itself is unchanged — it already works once the page is refused.
This item only closes the detection gap so recovery actually fires.

**Testing.** Extend the recognizer's lead-detection tests with the newly-covered
disambiguation phrasings (positive) and a set of real definitional leads
(negative) to guard against over-matching. No live store or network needed —
these are string-level tests over lead text, matching the existing
`WikipediaTopicRecognizerTests` style.

---

## Cross-cutting

- **Order of commits:** 1 → 2 → 3 → 4 → 5 → 6 → 7. (1 depends on 2's helper; 2
  is self-contained; 3 is independent cleanup; 4/5 are independent REPL adds;
  6 and 7 are independent of the REPL work.)
- **New internal seams** (`UnrecognizedCommand`, `CommandSuggester`,
  `HistoryCommand`, `HistoryView`) live in `CodeGenerator/App` beside
  `ClearCommand`, exposed to `CodeGenerator.Tests` via the existing
  `InternalsVisibleTo`.
- **No behavior change** to any currently-passing test except where item #3
  removes dead types.

## Files touched (anticipated)

- `CodeGenerator/App/CodeGeneratorStarter.cs` — capture outcome, dispatch
  `history`, print not-recognized feedback.
- `CodeGenerator/App/UnrecognizedCommand.cs`, `CommandSuggester.cs`,
  `HistoryCommand.cs`, `HistoryView.cs` — new.
- `CodeGenerator/App/CommandLineEditor.cs` — exclude `history` from persistence.
- `CodeGenerationEngine/Core/CommandDictionary.cs` — `cls` + `history` entries.
- `CodeGenerationEngine/Core/MethodBodyGenerator.cs` — richer compile-safe stubs.
- `DomainDiscovery/Recognition/WikipediaTopicRecognizer.cs` — broader
  disambiguation detection.
- Deletions: `CodeGenerationEngine/Helper/InteractiveConsole.cs`,
  `CommandSuggestionService.cs`, `CommandCatalog.cs` (+ dead tests).
- Tests: new/updated under `CodeGenerator.Tests` and `DomainDiscovery.Tests`.

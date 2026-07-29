# Decision Analysis for `build app:`

**Date:** 2026-07-29
**Status:** Approved for planning

## Problem

`build app:` decides what is and is not a domain class, and tells the user almost nothing about
why. The review loop prints the surviving classes and a bare list of rejected names:

```
Not classes: Lending, Administration
```

Every other decision is silent. A user who types "track lending, borrowers and fines" and gets no
`Lending` class has no way to find out whether it was judged and refused, quietly demoted to a
suggestion, or never recognized as a noun in the first place. Those three fates need three
different responses from the user, and today they look identical.

This makes the generator feel arbitrary in exactly the moment the user is deciding whether to
trust it.

## Goal

Every candidate name that dies during discovery carries a recorded reason, and the user can ask
for it: `why <name>`.

Explicitly **not** in scope: scoring or recommending the build options (target, persistence),
weighted trade-off matrices, and emitting ADRs into the generated app. Those are different
features that happen to share the phrase "decision analysis".

## Where candidates die

Established by reading the pipeline, not assumed:

| Site | What dies there | Visible today |
|---|---|---|
| `SeedEntityExtractor.cs:25` | noun the NLE typed as something other than Object/Person/Organization/Location | no |
| `SeedEntityExtractor.cs:28` | name that normalizes to empty | no |
| `SeedEntityExtractor.cs:31` | duplicate of a name already taken | no |
| `DomainEnricher.cs:39` | wikilink whose name is not a valid identifier | no |
| `DomainEnricher.cs:88-92` | candidate the article's lead does not mention → Suggestion | no — the print is commented out |
| `DomainEnricher.cs:126` | harvested suggestion already claimed as a seed class → dropped | no |
| `EntityShaper.cs:145-147` | `ShapeVerdict.Abstract` → Rejected | name only, no reason |
| `EntityShaper.cs:169-170` | `ShapeVerdict.Unknown`, non-seed → Suggestion | no |

`EntityHarvester`'s internal stopword and frequency census is **out of scope**. It discards
thousands of nouns per article; recording them would bury the signal this feature exists to
surface.

The duplicate-wikilink check at `DomainEnricher.cs:42-44` is also **not** recorded. A candidate
that is discarded because it is already in the list has not died — it is present under the same
name, and its own record already explains it. Recording the duplicate would put two contradictory
outcomes under one name.

## Architecture

### Why a log on the model

`DomainEnricher.Merge` mutates and returns the *seed* model (`DomainEnricher.cs:135`), so the
single `CandidateDomainModel` instance created by `SeedEntityExtractor` is the same object that
reaches the review loop. A collection hung on that object survives the whole pipeline with no
plumbing.

Two alternatives were considered and rejected:

- **Enriching the existing collections** (`List<string> Rejected` → `List<RejectedCandidate>`)
  breaks a public type with live callers, has nowhere to put drops that never reach either
  collection (the seed gate), and cannot record a *kept* decision.
- **An `IDiscoveryObserver` the stages notify** means constructor churn across five classes for
  one consumer, and mid-pipeline callbacks are harder to assert on than a value on the result.

The log wins because the trail becomes data on the result: a test asserts
`result.Model.Decisions` directly, and the VS Code extension gets it with no protocol change.

### Data model

New file `DomainDiscovery/Model/DecisionRecord.cs`:

```csharp
public enum DecisionStage  { SeedExtraction, Enrichment, Shaping }

public enum DecisionOutcome { Kept, Suggested, Rejected, Skipped }

public sealed class DecisionRecord
{
    /// <summary>The candidate as it appeared — "Cash register", not CashRegister.</summary>
    public required string Name { get; init; }
    public required DecisionStage Stage { get; init; }
    public required DecisionOutcome Outcome { get; init; }

    /// <summary>A stable phrase from <see cref="DecisionReasons"/>, never an ad-hoc literal.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// The concrete facts behind the reason — head noun, article title, NLE entity type,
    /// confidence — as one already-composed line, rendered verbatim by the report. A record may
    /// carry several facts here; the reason stays the constant, the specifics stay here.
    /// </summary>
    public string? Evidence { get; init; }
}
```

`CandidateDomainModel` gains:

```csharp
public List<DecisionRecord> Decisions { get; } = new();

public void Record(string name, DecisionStage stage, DecisionOutcome outcome,
                   string reason, string? evidence = null);
```

`Rejected` and `Suggestions` are left exactly as they are. Nothing downstream changes, and no
existing test churns.

`Skipped` and `Rejected` are deliberately distinct: `Rejected` means the pipeline judged the
candidate and said no; `Skipped` means it never reached judgement. The user's remedy differs —
a rejected name can be forced back with `add`, whereas a skipped one usually means the
description needs rewording.

Reason phrases live as constants in `DomainDiscovery/Model/DecisionReasons.cs` so that tests
assert against the same constant the pipeline emits, rather than a copy of the string.

### Recording points

| Site | Outcome | Reason constant | Evidence |
|---|---|---|---|
| `SeedEntityExtractor`, non-entity type | `Skipped` | `NotRecognizedAsAThing` | the NLE `EntityType` |
| `SeedEntityExtractor`, empty name | `Skipped` | `NameNormalizedToNothing` | — |
| `SeedEntityExtractor`, duplicate | `Skipped` | `AlreadyNamed` | — |
| `SeedEntityExtractor`, accepted | `Kept` | `YouNamedIt` | confidence |
| `DomainEnricher`, invalid identifier | `Skipped` | `NotAUsableClassName` | wikilink title |
| `DomainEnricher`, lead-demote | `Suggested` | `LeadDoesNotMentionIt` | article title |
| `Merge`, suggestion already a seed class | `Skipped` | `AlreadyAClassFromYourDescription` | — |
| `EntityShaper`, `Shaped` | `Kept` | `DefinedAsADomainClass` | role, head noun, source article |
| `EntityShaper`, `Abstract` | `Rejected` | `DefinitionResolvesToAnAbstraction` | head noun |
| `EntityShaper`, `Unknown` + seed | `Kept` | `YouNamedItNoDefinitionFound` | — |
| `EntityShaper`, `Unknown` + non-seed | `Suggested` | `NoDefinitionWeRecognize` | head noun / article |

Three mechanical consequences, each a known edit rather than a discovery left for implementation:

1. `SeedEntityExtractor.Extract` currently filters with
   `.Where(e => EntityTypes.Contains(e.Type))`, so rejected types are never iterated. The loop
   must walk every entity and record the skip.
2. `DemoteWhatTheDefinitionDoesNotName` does not receive `articleTitle`. It needs it for the
   evidence field.
3. `Merge` must append `harvested.Decisions` to `seed.Decisions`. Without this the entire
   enrichment stage's trail is silently lost — the single most likely bug in this feature, and
   therefore its own test.

`EntityShaper` pass 2c acts on `resolved[entity.Name]` and no longer has the head noun in scope.
A name→head dictionary built from the existing `candidates` list supplies it.

### Surface

Formatting lives in a new `CodeGenerator/App/DecisionReport.cs`, a pure function
`(CandidateDomainModel, string? name) → string[]` with no `Console` dependency. The review loop
prints what it returns. This is what makes the feature testable without driving the loop.

**Grouped summary in `PrintModel`** — capped, so a run that drops thirty nouns does not push the
model off screen:

```
Not classes: Lending, Administration — defined as an abstraction
Also found (add <name> to include): CreditCard, SelfCheckout, Receipt  (+12 more — try `why`)
```

Names are grouped by reason. Each group shows at most 8 names before the `(+N more)` tail.

This re-enables the suggestions display currently commented out at `BuildAppCommand.cs:251-257`.
That is deliberate: lead-demoted candidates are one of the categories being explained, and they
are invisible today.

**Two new review-loop verbs:**

- `why <name>` — the full trail for one name in stage order, whatever its fate, ending with the
  action available (`add <name>` when the name was suggested or rejected).
- `why` — the whole report, grouped by outcome.

Example:

```
> why Lending
Lending
  seed extraction  kept       you named it (confidence 0.82)
  shaping          rejected   its definition resolves to an abstraction
                              head noun: practice (from "Lending")
  Add it anyway with: add Lending
```

Name matching is canonical — lowercased with non-alphanumerics stripped — so `why cashregister`,
`why "Cash register"` and `why CashRegister` all reach the same record. This matters because the
record holds the article spelling while the user sees the class name.

An unmatched name prints `No decision recorded for 'x'.`

The help line in the review loop gains `why [name]`.

## Error handling

Nothing in this feature does I/O or can fail. The failure modes are all "nothing to say":

- No records at all (no topic recognized, seed-only run) → `No decisions recorded.`
- A name with no matching record → `No decision recorded for '<name>'.`
- An entity added by hand with `add` has no record; `why` on it reports that it was added in the
  review loop rather than discovered.

Records are append-only and are never consulted by scaffolding, so a defect here cannot corrupt a
model or affect what `approve` generates.

## Testing

Every test written red-then-green — a green assertion that was never seen to fail has repeatedly
hidden real defects in this repo.

**DomainDiscovery.Tests**

- One test per recording point: asserts exactly one record with the expected `Stage`, `Outcome`
  and `Reason` constant.
- `Merge` preserves `harvested.Decisions` — the trail-loss bug, isolated.
- A seed-only run (no topic recognized) still records its seed-extraction decisions.
- Recording never changes `Entities`, `Suggestions` or `Rejected` — the existing model assertions
  still hold.

**CodeGenerator.Tests**

- `DecisionReport` output for a rejected, suggested, kept and unknown name.
- Canonical name matching across the three spellings above.
- The group cap: 20 names in one group renders 8 plus a `(+12 more)` tail.
- The grouped summary is absent when there is nothing to report.

**Live verification**

A real `build app:` probe against the dump store, driven over piped stdin, confirming `why` on a
name actually dropped in that run. A green suite has hidden this class of bug here before; the
probe is not optional.

## Success criteria

1. Every death point in the table above produces exactly one `DecisionRecord`.
2. `why <name>` explains any name the user saw, in any of the three spellings.
3. Rejected names print with their reason instead of bare.
4. Demoted suggestions are visible again.
5. `approve` generates byte-identical output to before this change.
6. Full `SmartCodeGenerator.slnx` Release `-warnaserror` build clean, whole suite green.

# Validation phase 4: the Business stage

Phase 3 gave the pipeline the Entity stage and the review-card mechanism for a
rule the data merely supports. It is merged (PR #133, `main` = `885e008`,
4259 tests green).

Phase 4 delivers requirements 8, 9 and 14 from the parent spec's taxonomy:
Business Rule, Cross-Field, and Calculation. Two jobs, one branch, for the same
reason phase 3's two jobs were one branch — they meet:

1. **Wire the existing hand-stated business rule command into `build app`.**
   `add business validation` already parses natural language into
   `ComparisonRule`/`ArithmeticRule` objects that know how to emit their own
   C# check (`CodeGenerationEngine/Core/Validation/Rules/`), and `show business
   validation` already reads them back. None of this reaches a generated app
   today — `AppScaffolder`/`ValidatorTemplate` never reference
   `ValidationRegistry` or any of the fifteen `IValidationRule` implementations
   anywhere. Requirement 8 is answered by an existing, tested command that
   silently does not apply to the thing this whole initiative builds.
2. **Add the two inferable proposals** (Cross-Field, Calculation) that ride the
   same emission path once #1 exists — an accepted proposal and a hand-typed
   rule become the identical object.

The parent spec is `2026-09-06-validation-architecture-design.md`; this
document does not restate it. `2026-09-11-validation-phase-3-design.md` is the
most recent sibling and its own conventions (register-by-addition, complete-
scan proposals, conditional stage presence) are followed throughout rather
than re-derived.

## Decisions taken before this was written

| Question | Decision |
|---|---|
| Does phase 4 also wire the dead `add business validation` command in, or leave it as a filed-and-parked gap like #131/#132? | Wired in. A phase named "the Business stage" that ships with no way to state a business rule into it would be phase 4 in name only. |
| Do inferred proposals get their own emission shape, or reuse `ComparisonRule`/`ArithmeticRule`? | Reused. One emission path for hand-stated and inferred; `show business validation` describes an accepted proposal for free. |
| New proposal type, or widen the existing field-scoped `RuleProposal`? | New, separate `EntityRuleProposal` type. `RuleProposal`/`PropertyRule` are untouched — same discipline as phase 3 adding `EntityStageRules` beside `EmittableRules` rather than editing it. |
| Where does the registry lookup get wired in? | Both a fresh `build app` run and the incremental resume path (`AppScaffolder` and `IncrementalScaffolder`), through one shared helper. Cheap once the helper exists, and covers hand-creating a class and adding a rule before the first build. |

## What's NOT inferable, and stays exactly as it is

**Business rules (8) in general are not inferable — data cannot show intent.**
`add business validation`'s own parser catalog (comparisons, required-when,
either-or, arithmetic, sameness) is the right shape for everything a user
states outright, and phase 4 does not touch the parser, the registry's
storage, or `show business validation`. The only change is that the rules it
already collects now reach a generated app's validator.

## 1. Reaching a generated app: the registry lookup

`ValidationRegistry.Get(className, ValidationCategory.Business)` returns every
`IValidationRule` a user has stated for a class, in the current process. A new
helper — `BusinessRulesFor(entityName)`, likely on `ValidatorTemplate` itself
or a small new type beside it — merges that list with the entity's own
accepted `EntityRuleProposal`s (see below), each converted to the same
`ComparisonRule`/`ArithmeticRule` shape, and returns one combined list.

Both `AppScaffolder.cs` (fresh `build app`) and `Incremental/
IncrementalScaffolder.cs` (`add to an existing app`) call this helper once per
class before invoking `ValidatorTemplate.Generated(...)`, exactly where they
already gather the Entity stage's lookup data.

**No persistence gap.** `ValidationRegistry` is in-memory, cleared per process
— but every command in this CLI, including `build app` and `add business
validation`, runs inside the same long-lived interactive or piped session.
Typing `add business validation EndDate must be after StartDate in Booking`
and then `build app: ...` (or resuming an existing one) in the same session is
the whole intended shape; no cross-process persistence is needed or added.

## 2. The Business stage in the emitted validator

`ValidatorTemplate.cs` gains a `Business` stage method, conditional on
presence — mirroring exactly how `Entity` is only emitted when
`entityBlocks.Count > 0`:

```
Stages = [ Security, Field, (Entity,) (Business,) ]
```

Ordered after `Entity`: a cross-field or arithmetic check should trust that
the record's own fields and foreign keys are already valid before comparing
them to each other.

**The adapter, so the fifteen existing `IValidationRule` classes need zero
changes.** Their `Emit(ValidationEmitContext ctx)` methods write
`errors.Add("message")` against a `List<string>` — the older single-class
validator's shape, not the pipeline's `List<ValidationError>`. The generated
`Business` method declares a local `List<string> businessMessages = new();`,
runs every rule's `Emit` against a `ValidationEmitContext` wrapping that local
list and the entity's `PropertyDefinition`s, then:

```csharp
foreach (var m in businessMessages)
    errors.Add(new ValidationError(null, m));
```

`null` for the property, matching how the Entity stage's own delete-refusal
message already has no single property to attribute to.

## 3. The two inferable proposals

Both run in `DataModelBuilder.AddProposals`, which already has each entity's
full `TabularTable` at the point it currently loops field-by-field for
`RuleProposals.For`. A new pass runs alongside it, entity-wide rather than
per-field, and writes into a new `CandidateEntity.Proposals` list (parallel to
each field's own `Proposals`) rather than any one field's.

**Cross-Field (9).** For every pair of same-typed columns on one entity (both
date, or both numeric), test whether `left <= right` holds for every
non-blank row in both columns. Zero counter-examples across the full table,
never a sample — the same discipline `RuleProposals` already applies, and for
the same reason: a rule inferred from row *contents* is a guess about rows
not yet seen. On accept, the proposal becomes a `ComparisonRule` fed into the
same registry-merge helper as a hand-stated one.

**Calculation (14).** For every eligible triple of numeric columns (a target
and two operands) on one entity, test `target == left + right` and
`target == left * right` within decimal tolerance, plus the percentage form
`Total = Net * (1 + Rate)` as its own shape — the three forms the parent spec
names, and no wider search. Same zero-counter-example gate. On accept, becomes
an `ArithmeticRule`.

Both scans are capped against entities with many numeric columns, to keep the
triple search's cost bounded — the exact cap is sized during planning against
a real generated app's typical column count, not guessed here.

## 4. The review card

Both surfaces — the console loop (`rules` / `accept <n>` / `approve <target>`)
and `ReviewStage.razor` — already number every field's proposals in one flat
list; entity-scoped proposals join the same list, keyed to their entity
rather than a field. The evidence line names every field involved:

> *Booking: EndDate is always on or after StartDate, in all 40 rows*
> *OrderLine: LineTotal always equals Quantity times Price, in all 214 rows*

Nothing here changes for the three existing proposal kinds.

## Testing

Same rigor as phases 1-3, not a lighter version of it:

- The emitted `Business` stage is proven by Roslyn-compiling real generated
  source and invoking it, the same way `Field`/`Entity` already are — never a
  string assertion standing in for a compile.
- The two new scans get a unit test proving the zero-counter-example gate
  with a synthetic `TabularTable`, mirroring `RuleProposalsTests`.
- Per task 15's own lesson from phase 3 (four of five real defects were
  invisible to a 4,000+ test suite and found only by running a generated
  app), this phase's own task-15-equivalent step generates a real SQL-backed
  app with a genuine cross-field or arithmetic relationship in its seed data,
  clicks through both a hand-stated and an accepted-proposal rule live, and
  is budgeted as part of the plan rather than assumed away.

## Coverage this phase closes

| # | Category | Before phase 4 | After phase 4 |
|---|---|---|---|
| 8 | Business Rule | Stated via `add business validation`, never reaches a generated app | Stated the same way, now emitted into the Business stage |
| 9 | Cross-Field | Not delivered | Proposed where a comparison holds in every row; user-stated otherwise |
| 14 | Calculation | Not delivered | Proposed where an arithmetic identity holds in every row |

## Corrections this design makes to the parent spec

None yet identified by reading the code — unlike phases 2 and 3, this design
was written after tracing the actual `ValidationRegistry`/`IValidationRule`
wiring (or lack of it) rather than from the parent spec's prose alone, so no
correction is owed here. Any further correction found during planning or
implementation gets recorded the same way phase 3's were: in the plan, and
carried into this file when the branch merges.

# Validation phase 5: the Workflow stage

Phase 4 gave the pipeline the Business stage and closed requirements 8, 9 and
14. Phase 5 delivers requirement 16 (Workflow) from the parent spec's
taxonomy: `docs/superpowers/specs/2026-09-06-validation-architecture-design.md`
("Coverage of the seventeen", `## 6. Workflow`). This document does not
restate that background; `2026-09-17-validation-phase-4-design.md` is the
most recent sibling and follows the same shape.

## What already exists — investigated before proposing anything

The parent spec's own coverage table lists requirement 11 (Status) as
"Field stage, confirmed status column" and requirement 16 (Workflow) as
"Where a status exists." Phase 4's history taught this branch not to trust
a coverage claim without checking the code (requirement 8 was claimed
"already covered" and was not). This one checks out:

- **Requirement 11 is genuinely done.** `DataIngestion/RuleProposals.cs`
  already proposes `RuleProposalKind.Status` (distinct count ≤ 50, longest
  value ≤ 30 chars — the exact thresholds the parent spec states), which
  becomes an `AllowedValuesRule` on acceptance. `RuleProposal.Kind` is a
  plain property that survives acceptance — nothing needs to be added to
  find "which field is a confirmed status" later.
- **The wide context Workflow needs already exists.** `ValidationContext<T>`
  has carried a `Stored` field since an earlier phase
  (`AppScaffolding/ValidationContextTemplate.cs:70-82`), and
  `AppScaffolding/RepositoryTemplate.cs:115-123` already reads the on-disk
  record before every `Update` and passes it through:
  `var stored = GetById(GetId(item));` — with a comment naming exactly this
  phase: *"An immutable-field rule and a status transition are
  inexpressible without it."* No new plumbing is needed to get `Stored` to
  a rule; a stage method already receives the whole `context`, not just
  `context.Item` (`Business`'s own signature is
  `Business(ValidationContext<T> context, List<ValidationError> pipelineErrors)`).
- **The stage-splice mechanism is established** (phase 3's `Entity`, phase
  4's `Business`): a conditional `{{stageName}}` fragment built from an
  `IValidationRule` list, spliced into the template only when non-empty,
  appended to `stageList` the same way.

What genuinely does not exist: a `Workflow` pipeline stage, a rule kind that
compares two records instead of one, and any ordering/reordering UI
anywhere in this codebase (console or Blazor).

## Decisions (answered directly by the user before this was written)

1. **Scope: the two core rules only.** Forward-order and no-move-from-final.
   Approval — a role required for a specific transition — is NOT built in
   this phase. The parent spec calls it "expressible once the transition
   order and the permission matrix both exist," which after this phase is
   true, but building it is deferred to its own future increment. Nothing
   in this design blocks it: a later phase adds a role-per-transition map
   and one more Security-stage-style check, without touching anything here.
2. **Skip-ahead is allowed.** A move is legal whenever the new status's
   position in the stated order is **≥** the stored status's position —
   not strictly "the next one." Example: order `Draft, Approved, Shipped,
   Cancelled(final)` — `Draft → Shipped` is legal (skips `Approved`).
   Matches the parent spec's literal wording ("a move must go forward in
   the stated order") and needs no invented notion of "next."
3. **Transition editor UI: numbered re-type + checkbox, not drag-and-drop.**
   Console: `order status <v1,v2,v3,...> in <class name>` (comma list = the
   full sequence, all discovered values must be present) and
   `final status <v1,v2,...> in <class name>` (comma list, so more than one
   final in one call). Both end with `in <class name>`, matching the
   existing `add business validation <rule> in <class name>` grammar
   exactly — not `<verb> <class> <args>`. Blazor: the status's discovered
   values listed with up/down-arrow buttons to reorder and a checkbox per
   value for "final" — no native drag-and-drop, which is harder to
   bUnit-test reliably and inconsistent with this project's existing
   checkbox-driven review-card style.

## Data model

**`RuleProposal` gains two properties, not a new type.** Unlike phase 4's
`EntityRuleProposal` (a genuinely different SHAPE — entity-scoped, more than
one field), this is the *same* proposal a `Status`-kind acceptance already
produced, with two more facts filled in once the user runs `order`/`final`.
Widening beats a parallel type here because there is nothing to parallel:

```csharp
public sealed class RuleProposal
{
    public required RuleProposalKind Kind { get; init; }
    public required PropertyRule Rule { get; init; }
    public required string Evidence { get; init; }
    public bool Accepted { get; set; }

    /// <summary>The stated transition order, most-recent call to `order status` wins. Null
    /// until set; only meaningful for a Status-kind proposal that is Accepted.</summary>
    public IReadOnlyList<string>? Order { get; set; }

    /// <summary>Values a record at rest may not leave. Empty until `final status` names one;
    /// only meaningful for a Status-kind proposal that is Accepted.</summary>
    public IReadOnlySet<string> FinalValues { get; set; } = new HashSet<string>();
}
```

`order`/`final` only act on a proposal that is (a) `Kind == Status` and (b)
already `Accepted` — a status column must be confirmed before its
transitions can be stated, mirroring "once a status column has been
confirmed" in the parent spec. Naming a class with no accepted Status
proposal, or a value `order`/`final` doesn't recognise, is refused the same
way `accept <n>` on an out-of-range number already is (`ProposalReport`'s
existing `"There is no proposal {argument}."` pattern).

## New rule: `WorkflowRule`

A 17th rule class (`CodeGenerationEngine/Core/Validation/Rules/WorkflowRule.cs`),
`Category => ValidationCategory.Workflow` (a new third enum value, beside
`Data`/`Business` — used for the same reason `Business` is: keeping this
rule out of any other stage's list). Built only by the scaffolder from an
accepted, ordered Status proposal — never hand-typed, never through
`IValidationRuleParser`, the same way `PercentageRule` is only ever built by
the Calculation scan.

`WorkflowRule(string property, IReadOnlyList<string> order, IReadOnlySet<string> finalValues)`.
Unlike every existing rule, `Emit` must compare TWO records, not one — the
existing `this.` → `item.` rewrite trick doesn't apply here, so `Emit`
writes its own self-contained block referencing both `item.{property}` and
`stored.{property}` directly (both names are already in scope in the
`Workflow` stage method, built the same way `Business`'s `item` local is):

```csharp
if (stored is not null)
{
    var __order = new[] { "Draft", "Approved", "Shipped", "Cancelled" };
    var __from = System.Array.IndexOf(__order, stored.Status);
    var __to = System.Array.IndexOf(__order, item.Status);

    if (__from >= 0 && __to >= 0 && __to < __from)
        errors.Add("Status cannot move backward from 'Approved' to 'Draft'.");
    else if (__from >= 0 && new[] { "Cancelled" }.Contains(stored.Status))
        errors.Add("Status cannot change once it is 'Cancelled'.");
}
```

(Exact emitted text is the implementation plan's job; this shows the shape,
not the final strings.) `stored is null` (Insert, or a caller that cannot
supply it) skips both checks — nothing to compare against, and a fresh
record cannot violate a transition it has not made.

## Wiring: `ClassDefinition.WorkflowRules`, `DomainModelMapper.AddWorkflowValidation`

Mirrors `BusinessRules`/`AddEntityValidation` from phase 4 exactly:

```csharp
public List<IValidationRule> WorkflowRules { get; set; } = [];
```

`AddWorkflowValidation(classes, model, names)`, called from
`DomainModelMapper.ToClassDefinitions` beside `AddEntityValidation`: for
each entity, for each field's accepted Status proposals with a non-null
`Order`, build one `WorkflowRule` and add it to the matching class's
`WorkflowRules`. This layer stays general — nothing here assumes exactly
one ordered Status proposal per entity, so an entity with two
independently-ordered status-like columns gets two `WorkflowRule`s, same
list. The restriction to "exactly one" lives ONLY in the console command's
own targeting logic (see Console and Blazor surfaces, below), because a
bare `order status ... in <class>` has no other way to pick a field; the
Blazor page has no such restriction, since it renders one `.workflow-order`
block per accepted Status proposal and the user clicks the one they mean.

## `ValidatorTemplate.Generated`: the `Workflow` stage

Gains a new optional trailing parameter, `IReadOnlyList<IValidationRule>? workflowRules = null`,
exactly like `businessRules` was added in phase 4 (every existing call site
keeps compiling). The `Workflow` static method is built the same way
`Business` is — its own `StringBuilder`/`ValidationEmitContext` per rule,
each wrapped in its own try/catch (same CS0128 lesson phase 4 already
learned and fixed for `Business`) — but its body needs BOTH `context.Item`
and `context.Stored` in scope, not just `item`:

```csharp
private static void Workflow(ValidationContext<{{definition.Name}}> context, List<ValidationError> pipelineErrors)
{
    var item = context.Item;
    var stored = context.Stored;
    if (item is null)
        return;

    var errors = new List<string>();

{{workflowBody}}
    foreach (var m in errors)
        pipelineErrors.Add(new ValidationError(null, m));
}
```

Spliced in as `{{entityStage}}{{businessStage}}{{workflowStage}}`, after
`Business` — a workflow transition should trust that every other stage,
including hand-stated and inferred business rules, already passed.
`stageList` gains `"\n        Workflow,"` when `workflowBody.Length > 0`,
same conditional pattern as `Business`.

## Console and Blazor surfaces

**Console** (`CodeGenerator/App/`, alongside the existing
`add business validation`/`ProposalReport` command handlers):
`order status <v1,v2,...> in <class name>` and
`final status <v1,v2,...> in <class name>`. Neither names which FIELD —
both target "the" accepted Status proposal on that class. Both refuse (with
a message naming what's wrong, not a stack trace) when: the class doesn't
exist, the class has no accepted Status proposal, **the class has MORE THAN
ONE accepted Status proposal** (a real but expected-to-be-rare case — one
status field per entity is the normal shape; refusing here rather than
guessing which field is deliberate, not a gap silently left open), or the
value list doesn't exactly match the discovered set (order must be a
permutation of every discovered value — a partial or extra value is
refused, since a workflow with an unlisted value the data actually
contains would silently never refuse a move into or out of it).

**Blazor** (`ReviewStage.razor`, beside phase 4's `.entity-proposals`
block): once a field's Status proposal is `Accepted`, a new
`.workflow-order` list appears showing its discovered values with
`[↑]`/`[↓]` buttons (swap-with-neighbour, disabled at the ends) and a
`final` checkbox per value — the same `.proposal-row`-style markup
conventions phase 4 established, new class names only for what's genuinely
new (`.workflow-order`, not a rename of anything phase 3/4 already built).
Saves through `Session.ModelEdited()` on every change, same as every other
review-card edit.

## Testing rigor

Same standard as phases 1-4, not a lighter version of it:

- The emitted `Workflow` stage is proven by Roslyn-compiling real generated
  source and invoking it against a real `ValidationContext` with both
  `Item` and `Stored` populated — never a string assertion standing in for
  a compile, same discipline `BusinessStageEmissionTests` already
  established.
- `WorkflowRule.Emit` is unit-tested directly the way `PercentageRuleTests`
  tests `PercentageRule.Emit` — asserting on the real emitted text's
  characteristic fragments, not just unrelated getters (a lesson phase 4's
  own review loop had to enforce after the fact; this phase's plan states
  it as a requirement up front).
- The skip-ahead and final-state rules each get a dedicated compile-and-
  invoke test proving the boundary: a legal skip (`Draft → Shipped`), an
  illegal backward move (`Approved → Draft`), and a move attempted from a
  final status.
- **A real-generated-app run is budgeted as part of the plan, not assumed
  away** — the same lesson every phase on this branch has had to relearn.
  Phase 4's own version of this step was stopped partway through by explicit
  user decision and never completed; this phase does not inherit that gap
  silently — its own plan states the live-run task as required, and the
  decision to skip or shorten it (if made again) is the user's to make at
  that time, not mine to assume in advance.

## Out of scope, explicitly

- **Approval / role-per-transition** — deferred, per the Decisions section.
- **Branching workflows** (a status reachable from more than one earlier
  status via different rules, or a transition graph rather than a single
  stated order) — the parent spec's own design is a linear order with
  finals, not a graph; this phase does not go further than that.
- **Multiple independently-tracked status columns on one entity, via the
  console** — the rule-generation layer supports it generically (see
  Wiring, above), and Blazor's per-field rendering has no trouble with it,
  but the console's bare `order status ... in <class>` command explicitly
  refuses rather than guessing which field (see Console and Blazor
  surfaces). Not a scenario this phase's testing specifically targets
  beyond that refusal, since no current generated-app pattern produces two
  status-like columns on one entity.

## Corrections this design makes to the parent spec

None identified by reading the code — same as phase 4's own finding here.
The parent spec's requirement-16 section (`Stored` needed, order stated on
the card, two rules) matches what phase 4 already built in anticipation of
it (the `Stored` field, the `RepositoryTemplate` comment naming this exact
phase) almost exactly. Any correction found during planning or
implementation is recorded in the plan and carried into this file when the
branch merges, per this branch's own standing convention.

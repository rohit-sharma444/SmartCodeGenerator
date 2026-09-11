# Validation phase 3: the counter-example scan and the Entity stage

Phase 2 built the machine: `ValidationContext<T>`, `ValidationOperation`, the
seven-stage pipeline with its short-circuiting, `IEntityLookup`, the Security
stage outside `novalidation`, and per-property attribution on `SaveResult`. It
is merged (PR #130, `main` = `81bc4db`, 4183 tests green).

Phase 3 gives the machine something to enforce. Two jobs, one branch:

1. **The counter-example scan** — read the rows the tool has already loaded,
   find the rules that hold in every one of them, and offer them to the user.
2. **The Entity stage** — the three checks that need a record other than the one
   in hand: reference exists, uniqueness, and referential integrity on delete.

They are one branch because they meet: an accepted uniqueness proposal is the
scan's output and the Entity stage's input, and splitting them would land a
proposal with nothing to enforce it or an enforcer with nothing to enforce.

The parent spec is `2026-09-06-validation-architecture-design.md`; this document
does not restate it. Corrections made to it as part of this design are listed at
the end.

## Decisions taken before this was written

| Question | Decision |
|---|---|
| One branch or two? | One, run the way phase 2 ran: a spec correction, a plan, subagent-driven tasks. |
| Where does the scan run? | Over the rows already read, in memory. Not pushed into SQL. The unbounded `SELECT *` is its own ticket, not this branch's problem to solve. |
| Does a proposal arrive accepted? | No. It waits to be picked. Typing `approve` takes none of it. |

The last one is the load-bearing decision of the whole phase. A rule inferred
from data *contents* is a guess about the future, however many rows support it;
a rule from a declared `nvarchar(50)` is a fact the source stated. The first
kind must be picked; the second kind already lands silently and continues to.

## Where the scan runs

`DataModelBuilder.Build(tables, findings)`. Both surfaces pass through it —
`BuildAppCommand` on the console side, `DiscoveryStep.BuildFromData` in the
browser — and at that point the complete `TabularTable`s are in hand. Its two
overloads converge (`Build(tables)` is `Build(tables, [])`), so there is one
place to attach the scan and no second path that quietly skips it.

Everything downstream is a sample. `TabularImport.ToImportedData` caps each
entity at `ImportedData.MaxSeedRows = 1000`. Scanning there would infer rules
from the first thousand rows, which is the exact failure the counter-example
principle exists to prevent, and it would do so invisibly — the resulting app
would reject data its own source contains. **The scan reads `TabularTable`, never
`ImportedData`**, and a test asserts it by scanning a table whose fifth status
value first appears at row 50,000.

A discovered domain — Wikipedia or Ollama — has no tables. It gets no scan, no
proposals, and must still generate. Tested.

### The scanner

A new `ColumnScan` in `DataIngestion`, one pass per column, producing:

| Output | Used by |
|---|---|
| distinct values, stopping at a 51st | status |
| longest value length | status (the ≤30 bound that separates a status from a notes column) |
| count of values occurring more than once | uniqueness |
| per-format match counts | pattern |
| total non-blank rows | the evidence sentence |

One pass, all outputs — not one pass per candidate. Blank cells count towards
the row total but not towards distinct values; a status column with blanks is
still a status column, and `required` is the Field stage's business.

## What a proposal is

```csharp
public sealed class RuleProposal
{
    public ProposalKind Kind { get; init; }   // Status, Unique, Pattern
    public PropertyRule Rule { get; init; }   // what it becomes if accepted
    public string Evidence { get; init; }     // "holds in all 12,431 rows"
    public bool Accepted { get; set; }        // false until the user says otherwise
}
```

It hangs off `CandidateField`, beside `Facts`, for the reason `Facts` is there:
a rename in the review loop must not orphan it. `CandidateField.Facts` is
documented as "carried from the reader so a rename in the review loop cannot
orphan it", and proposals inherit that guarantee by sharing the carrier.

Only accepted proposals become rules, and they do it in
`DomainModelMapper.InferValidation` — the one place in the tool where facts
become `PropertyRule`s. Nothing else about how a rule reaches a generated app
changes. `InferValidation`'s own remarks carry the warning that governs this:
every rule recorded there must appear in `ValidatorTemplate.EmittableRules`
**paired with the type it is recorded on**, or it is dropped silently. Each new
proposal kind needs its pairing and a test that proves the pairing, not just the
rule.

### The three kinds, and what is not proposed

**Status.** Exact distinct count ≤ 50, longest value ≤ 30 characters, string
column. Becomes an allowed-values rule.

**Unique.** No value occurs twice. Masters only — a transaction line's product
code repeats by design, and proposing uniqueness there would be proposing a rule
that forbids a second sale of the same product. Where the source is a database
and a real `UNIQUE` or primary key already says so, that is used directly and no
proposal is needed.

**Pattern.** Only where every value matches a format the catalog already has: the
five `NamedFormatRuleParser` knows — phone, URL, postcode, GUID, IP — and email,
which has its own `EmailRuleParser`. Each is "deliberately loose where the
real-world format is", which is what makes it safe to propose from data. **No
regex is invented from data.** A pattern derived from 12,000 references is a
pattern that rejects the 12,001st, and it is exactly the kind of rule a user
cannot evaluate from the card: "matches `^[A-Z]{3}-\d{4}$`, holds in all 12,431
rows" reads like evidence and is a trap.

Cross-field comparisons and calculations are Business-stage work and are not in
this phase.

## The two cards

Both surfaces already show a model; neither shows rules today.

**Console review loop.** Proposals print under their field with the evidence, all
unticked, and a verb accepts them. `approve` alone advances the build taking
none of them — the answer to "does it arrive accepted".

**Blazor `ReviewStage`.** A checkbox per proposal. The existing comment at line
191 — "The rules are not restated here — no control is pre-disabled by
second-guessing `RemoveField` or `AddField`" — stays true: a proposal is an
offer, not a restatement of what the field already is, and ticking one disables
nothing.

**Resume.** `BuildSnapshot` persists the accept/reject *decisions* only, keyed by
entity and field name. It already omits rows deliberately (they are re-read) and
the connection string (the manual promises it is never written to `.codegen/`).
The scan re-runs on resume; a decision survives it. A decision whose field no
longer exists is dropped, not restored onto a stranger.

## The Entity stage in the generated app

Three checks, all needing a record other than the one in hand.

**Reference exists.** Moved out of the Field stage, where phase 2 left it, into
stage 3 where the parent spec assigns it. The move changes which errors a record
reports — the pipeline stops between stages, so a record with a bad field and a
bad foreign key stops reporting both — and the stage-order tests are re-reasoned
with it, not merely re-run. At the same time the emitted check loses its zero
escape: today `ValidatorTemplate.cs:157` emits
`if (item.{target}Id != 0 && !_lookup.Exists<{target}>(item.{target}Id))`, so a
sale line with no product at all is considered valid. For a transaction or a
document line the foreign key must be set *and* resolve; a master's optional
foreign key keeps the zero escape, because optional means optional.

**Uniqueness.** Needs a third member on `IEntityLookup` — the second and last
widening of that interface — answering "does any other record of this type hold
this value for this property". Other: an update must not collide with itself.
`StoreEntityLookup` implements it the way it implements the other two, by
scanning `StoreFactory.Create<TEntity>().GetAll()`.

**Referential integrity on delete.** `CountReferencing` per relationship
`RelationshipDetector` knows about; a non-zero count refuses with the count in
the sentence: *"This Customer cannot be deleted — 3 Invoices refer to it."* This
stage is `CountReferencing`'s first caller, so it owns the runtime test for its
early return — the early return is what stops a full scan of a large store, and
nothing has ever executed it.

## Entry conditions, absorbed

The parent spec's *Phase 3 entry conditions* section records seven rulings. They
are this branch's opening tasks, in this order:

1. **The three grid delete call sites move onto a pipeline call**
   (`MasterScreenTemplate.cs:379`, `ViewModelTemplates.cs:293`,
   `BlazorScreenTemplates`' `MayDelete`) — **before** any delete-time referential
   check lands. Otherwise the deferred-save grid removes a row the pipeline
   would have refused and says nothing, which is precisely what row three of the
   enforcement table exists to prevent.
2. **The WPF transaction screen gets its status line.** `TransactionTemplate`
   binds `Status` nowhere, so a refusal written there cannot be seen at all. A
   refusal no one can see is not a refusal.
3. **`AppHasLogin`.** Emit the const from the flag `PermissionsTemplate` already
   sees, and refuse a null role only when it is true. Hardening, not a live hole
   — and the permissive default stays required for the four targets with no auth
   core.
4. **The `CountReferencing` early-return runtime test**, owned by the
   referential-integrity task.
5. **One emitted `Validation/ActingUser.cs`** in place of five `CurrentRole()`
   copies in three shapes.
6. **Shared test helpers** for the six mixed-line-ending loops and the four
   `Occurrences`-shaped counters.
7. **A unit test for the plain grid's delete guard** (`ViewModelTemplates.Delete`).

Items 1 and 2 are hard prerequisites of the Entity stage. Items 5 to 7 are
cleanup and may land anywhere on the branch.

## Testing

Red before green on every task; the phase-1 and phase-2 record is that a green
suite proves less here than anyone would like.

- **The scan is tested against a table that breaks at row 50,000**, not row 5.
  A sample would have passed, and that is the whole point.
- **A rule that would reject the source's own data is never proposed.** One
  direct test per kind.
- **`approve` with proposals pending takes none of them**, on both surfaces.
  This is the decision, so it gets a test that fails loudly if it is ever
  reversed by convenience.
- **Emitted code is compiled with `CompileSources` in real namespaces.**
  `EmittedSource.Flatten` strips `using` and `namespace`, so a flattened fixture
  proves nothing about cross-namespace resolution. Both partial halves compiled
  together, never a concatenation — that is how CS0260 hid before.
- **Each proposal kind is asserted present in `EmittableRules` paired with its
  property type**, because the failure mode there is a rule silently dropped.
- **Delete refusal at each enforcement point**, since no single one covers every
  front end.
- **A real generated app is run.** Generate from SQL with a status column, tick
  the proposal, run the app, enter a bad status, and try to delete a referenced
  master. Four of five defects in the entry-screens round were invisible to 4,000
  passing tests and were found only by running the app; the same was true of the
  Ollama round. The budget for this is planned, not hoped for.

## Out of scope

- **Bounding the unbounded read.** `SqlClientSchemaSource.cs:125` issues
  `SELECT * FROM [schema].[table]` with no cap. It is filed as its own issue.
  This branch depends on the rows being in memory and does not make that worse.
- Composite unique keys and composite foreign keys, for the parent spec's
  reason: half of a composite is wrong rather than incomplete.
- Inventing regexes from data.
- The Business, Transaction, Workflow and Database stages, including the
  transition editor for a confirmed status. A status confirmed in this phase
  yields an allowed-values rule and nothing more.
- Anything inferred from a file source landing silently. A file states no
  bounds; its contents may propose and may never constrain.

## Corrections made to the parent spec

1. **The counter-example scan's mechanics.** It described SQL counting queries
   and claimed the distinct scan "never reads a 47-million-row column through".
   Both are moot: the reader already loads every row of every picked table.
   Replaced with the in-memory pass, plus the explicit prohibition on scanning
   `ImportedData`.
2. **`ValidationInference.For(...)` does not exist.** The left branch of the
   convergence diagram is `DomainModelMapper.InferValidation`, and phase 3 adds
   no new inference class.
3. **Status moves from the Entity stage to the Field stage.** An allowed-values
   check is answerable from the record in hand; leaving it in Entity would report
   a bad status a stage later than a bad field, because the pipeline stops
   between stages. The Workflow stage's transition check, which does need
   `Stored`, is unaffected. Coverage row 11 and both stage headings updated.
4. **Two decisions added** to the decisions table: where the scan runs, and that
   a proposal waits to be picked.

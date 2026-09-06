# A validation architecture for generated apps

Generated apps validate one record at a time, in isolation, against rules guessed
from five property names. This design replaces that with an ordered pipeline of
seven stages, each seeing the whole picture — the record, the operation, the
stored version, the child lines and the signed-in user — and each rule inferred
only from evidence the data actually contains.

It covers the seventeen validation categories in the requirement, and says
plainly which of them the data can support, which need something typed, and
which deliver nothing until an app happens to contain the right entity.

## The problem

Three separate problems, all live today.

**The seam is too narrow to express most of the requirement.** The whole
validation contract in a generated app is:

```csharp
public interface IValidator<T>
{
    IReadOnlyList<string> Validate(T item);
}
```

One record. No user, no previous version, no child lines, and no knowledge of
which operation is running. Six of the seventeen categories cannot be written
against that signature at any cost:

| Category | Needs |
|---|---|
| 10 Transaction / Document | the header and its lines together |
| 11 Status-Based | the stored status, to compare against the incoming one |
| 12 Permission | the signed-in user |
| 15 Referential Integrity | to know it is a *delete*, and who references the record |
| 16 Workflow | the old value and the new one |
| 17 Security | the signed-in user |

**Inference is five lines of name-matching over strings alone.** The whole of it
is `DomainModelMapper.InferValidation` (lines 240-262):

```csharp
if (property.Type != "string")
    return false;                       // int, decimal, bool, DateTime: nothing at all

var isNameField = string.Equals(property.Name, "Name", ...)
    || string.Equals(property.Name, "Title", ...);
if (isNameField)
    property.Rules.Add(new PropertyRule { Name = "required" });

property.Rules.Add(new PropertyRule { Name = "maxlength", MaxLength = 200 });

if (property.Name.Contains("email", ...))
```

Every non-string property is returned on before any rule is considered, so a
`decimal` price, an `int` quantity and a `DateTime` order date carry no
validation of any kind. `required` reaches exactly two property names. And the
`Contains` matching is why a substring rule would let `Package`, `Storage` and
`Message` match a word like `Age`.

**The pipeline reads real facts and discards them.** `SqlClientSchemaSource`
selects `IS_NULLABLE` into `SqlColumnInfo` and `TabularColumn` drops it.
`SqlTypeMap` collapses `tinyint`, `smallint` and `bigint` to `int` and
`decimal`, `money` and `float` to `decimal`, keeping neither bound nor
precision. The columns query never asks for `CHARACTER_MAXIMUM_LENGTH` at all.
The consequence is a shipped defect: every string is bounded at 200 regardless
of its column, so a generated app accepts 120 characters into an `nvarchar(50)`
and the database rejects the insert.

**Permission enforcement is decoration.** The only permission check in a
generated app is in a view model:

```csharp
// AppScaffolding/Screens/MasterScreenTemplate.cs:347
if (!Session.IsAdmin)
{
    Status = "Only an administrator can delete a record.";
    return;
}
```

`RepositoryTemplate`, `EfStoreTemplate` and `JsonStoreTemplate` contain zero
references to `Session`, `IsAdmin` or `Role`. The Web API front end, the console
menu and any direct repository call delete a master record with no check at all.
This is requirement 17 — UI validation alone is not security — failing in the
shipped product.

## What already exists

Worth stating, because several categories are closer than they look.

| Machinery | Where | State |
|---|---|---|
| Users, roles, login | `AppScaffolding/Auth/` | Full: `User`, `AppRole` (Admin, User), `Session.Current`, password hashing, login screens for WPF, WinForms and Blazor. Optional behind an `auth` flag. |
| Validation layer | `ValidatorTemplate` | `IValidator<T>`, `IEntityLookup`, `ValidatorFactory`. Optional behind a `validation` flag. |
| 18 rule kinds | `CodeGenerationEngine/Core/Validation/Rules/` | Required, length, range, comparison, regex, allowed-values, arithmetic, either-or, required-when and more. All emit `this.X` checks. |
| Entity roles | `AppScaffolding/Screens/EntityRoles.cs` | Classifies Master, Transaction, DocumentLine. Correctable on the Review card via `ScreenOverrides`. |
| Relationships | `CodeGenerationEngine/Core/Relationships/` | `RelationshipDetector`, and `ForeignKeys.TargetsOf` for who points at whom. |
| Value scanning | `DataIngestion/Schema/ColumnTypeInference.cs` | Scans values to infer type. Samples 200 non-empty values. |

Genuinely absent, in the scaffolding and in the domain lexicon alike: document
status lifecycles, workflow states, financial periods, stock, balances, and
branch or company scoping.

## Decisions

| Question | Decision |
|---|---|
| How far does the tool go? | It enforces only what the data already shows. Nothing is invented. If an Invoice has no status column, the status and workflow categories stay silent for that app. |
| What makes a column a status? | Its values. A column whose values repeat from a small fixed set is proposed as a status, whatever it is called, and confirmed per entity on the Review card. |
| How is that counted? | Exactly, capped at 51 distinct values. Never sampled. |
| What shape is the engine? | An ordered pipeline of stages, each stage receiving a wide context. |
| Where does Security run? | First, before every other stage. |
| Is Security behind `novalidation`? | No. `novalidation` drops the other six stages; permission and role checks are always emitted. |
| Where do workflow transitions come from? | The user orders the discovered status values on the Review card and marks the final ones. The values come from data; only the order is stated. |
| What does Calculation do? | Verifies arithmetic the data proves, such as `LineTotal = Quantity * Price`. The field stays stored and editable. |
| When is a rule proposed? | Only when a counter-example scan over the source returns zero rows. See below. |

## The counter-example scan

This is the principle the whole inference layer rests on, and the answer to the
sampling trap that would otherwise poison every inferred rule.

`ColumnTypeInference` samples 200 values. Inferring a rule from a sample is
unsafe in one specific, silent way: a status column whose rare fifth value
`Reversed` first appears at row 50,000 would yield a whitelist of four, and the
generated app would then **reject data the source itself contains**. The same
trap applies to a proposed uniqueness rule, a proposed pattern, a proposed
cross-field comparison and a proposed calculation.

So no rule is ever proposed from a sample. Every candidate rule is expressed as
a query that counts the rows breaking it, and the rule is proposed only when
that count is zero.

| Candidate | Counter-example query |
|---|---|
| `Status` is one of a fixed set | `SELECT DISTINCT TOP 51 Status` — more than 50 means it is not a status |
| `Code` is unique | `SELECT COUNT(*) FROM (SELECT Code FROM T GROUP BY Code HAVING COUNT(*) > 1)` |
| `LineTotal = Quantity * Price` | `SELECT COUNT(*) WHERE LineTotal <> Quantity * Price` |
| `StartDate <= EndDate` | `SELECT COUNT(*) WHERE StartDate > EndDate` |
| `Reference` matches a pattern | `SELECT COUNT(*) WHERE Reference NOT LIKE ...`, or a streamed regex count |

For file sources the same counts come from a single streaming pass, which the
readers already make. For SQL Server the counts are one scan each, and the
distinct scan stops as soon as a 51st value appears — so it never reads a
47-million-row column through unless that column really is a status.

The card shows the evidence with the proposal: *"holds in all 12,431 rows"*.
That is what makes a wrong inference arguable rather than mysterious, and it is
why the user is being asked to confirm rather than to guess.

An LLM-discovered domain has no rows at all. It gets only the rules that follow
from the declared type, and must still generate.

## Architecture

### The context

```csharp
public enum ValidationOperation { Insert, Update, Delete, Post }

public sealed record ValidationContext<T>(
    T Item,                  // the record as submitted
    ValidationOperation Op,  // which operation is being attempted
    T? Stored,               // what is on disk now; null on Insert
    IEntityLookup Lookup,    // existence, and reference counting
    User? User);             // Session.Current at the call site, null when auth is off
```

Each field earns its place by unlocking a category that is otherwise
inexpressible: `Stored` for 11 and 16, `Op` for 15, `User` for 12 and 17, and
the lookup's new counting method for 15.

`IEntityLookup` grows exactly one method:

```csharp
public interface IEntityLookup
{
    bool Exists<TEntity>(int id) where TEntity : class, new();

    /// <summary>How many records of this type carry this foreign key value.</summary>
    int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new();
}
```

A document validation carries its lines alongside the header, through a
`DocumentValidationContext<THeader, TLine>` holding both, so requirement 10 can
run the whole pipeline over the header and every line before anything is
written.

### The stages

The requirement's own architecture tree, in order, with Security lifted to the
front:

```
1. Security      Permission, Role, Company, Branch
2. Field         Required, Data Type, Length, Range, Pattern, Format
3. Entity        Unique, Reference Exists, Status
4. Business      Business Rules, Cross Field, Cross Entity, Calculations
5. Transaction   Stock, Financial Period, Balance, Posting
6. Workflow      State, Transition, Approval
7. Database      Foreign Key, Unique Constraint, Check Constraint, Integrity
```

Security runs first so that an unauthorised operation is refused without running
the other six stages, and without reporting which fields were wrong — which
would tell a user who may not perform the operation what the record contains.

**Short-circuit rule: a stage collects every error it finds, and the pipeline
stops at the first stage that found any.** Collecting within a stage is what
lets a form show all its bad fields at once. Stopping between stages is what
prevents a range check running against a value that is not a number, and what
makes requirement 10 a property of the design: a `Post` commits only if all
seven stages returned nothing.

A stage with no rules emits no code. For a simple three-entity app most stages
are empty and the generated output is no heavier than today's.

### Where inference meets the existing rules

Both paths converge on `IValidationRule`, not on text:

```
 schema facts + counter-example scans        user types "Price must be positive"
                |                                          |
                v                                          v
     ValidationInference.For(...)              ValidationRuleCatalog.Parse
                |                                          |
                +---------------> IValidationRule <--------+
                                        |
                                        v
                                  stage assignment
                                        |
                          +-------------+-------------+
                          |                           |
                          v                           v
              answerable from the record     needs other records or the user
              model's Validate() partial     <Model>Validator.Generated.cs
              (all 18 rule kinds work         beside the lookup and the
               untouched: they write this.X)  pipeline's stage list
```

Inference constructs rule objects directly rather than producing English
sentences for the catalog to re-parse. Round-tripping through text was
tempting — it would let inference own no emit logic — but it carries a failure
that is silent and severe: a sentence no parser understands is dropped, and the
tool then reports success over a check that is not there. Converging at
`IValidationRule` removes that failure mode entirely and still reuses all 18
existing rule implementations unchanged.

A typed rule wins for its property; inference fills the rest.

## Stage by stage

### 1. Security

Generalises the single hardcoded `Session.IsAdmin` check into a permission
matrix, and — the point of the exercise — moves enforcement from the view model
to the store boundary, where every front end passes through it.

The default matrix preserves today's behaviour exactly: an Admin may do
anything; a User may do anything except delete a master record or reach the
Users screen. Permissions are not in anyone's data, so nothing here is inferred;
what changes is that the rule is now enforced rather than merely displayed.

Emitted whether or not `novalidation` is given. When the `auth` flag is off there
is no user, and this stage is empty.

Company and Branch scoping from the requirement's tree deliver nothing unless an
app happens to contain such an entity. Where one exists and records carry a
foreign key to it, the stage restricts a user to their own — but the user's own
company is not something the data supplies, so this remains inert until an app
has both the entity and a way to say who belongs where. Stated here as a known
limit rather than a promise.

### 2. Field

Requirements 1, 2, 3, 4 and 5. Everything here comes from carrying through the
type facts the pipeline currently reads and discards.

| Fact | Rule |
|---|---|
| `IS_NULLABLE = NO` | required |
| `nvarchar(50)` | max length 50 |
| `nvarchar(max)` | no length bound |
| `decimal(18,2)` | at most 2 decimal places, at most 16 digits before the point |
| `tinyint` | between 0 and 255 |
| `smallint` | between -32768 and 32767 |
| `int` | whole number, int range |
| `datetime` | on or after 1753-01-01, SQL Server's own floor |
| `date` | date only, no time component |
| `bit` | none; a bool cannot be invalid |
| `CHECK` constraint | its condition, where it parses to a supported rule |

Ranges come from *declared* bounds only, never from observed minimum and
maximum. Tomorrow's data may legitimately exceed today's, and a rule inferred
from an observed range would reject it.

For file sources the same facts come from evidence rather than declaration:
longest value seen becomes the length bound, blank cells seen decide
nullability, and greatest decimal places seen sets the scale.

Two name-based exceptions are grandfathered, matched on whole PascalCase words
rather than by substring: `Email` and `Phone`. The current implementation does
`Name.Contains("email")`; substring matching is why a word like `Age` would
match `Package`, `Storage` and `Message`, and whole-word matching closes that
class of defect. No other name patterns.

A pattern may also be proposed where every value in a column matches one, under
the counter-example scan, and confirmed on the card.

### 3. Entity

Requirements 6, 7 and 11.

**Uniqueness.** From a real `UNIQUE` constraint or primary key where the source
is a database. Otherwise proposed where a counter-example scan finds no
duplicates, and confirmed per entity on the card. Masters only.

**Reference exists.** From real foreign keys. Transactions and document lines
require their foreign keys to be set *and* to resolve. The current check
(`ValidatorTemplate.cs:157`) emits
`if (item.{target}Id != 0 && !_lookup.Exists<{target}>(item.{target}Id))`,
where a zero skips
the check entirely, so a sale line with no product is considered valid. Masters
keep foreign keys that are optional but must resolve when set.

**Status.** A string column whose exact distinct count is 50 or fewer and whose
longest value is 30 characters or less is proposed as a status with the values
found. The length bound is what separates a status from a low-cardinality
description or a notes column that happens to repeat. On
confirmation it yields an allowed-values rule. Where the source is a database
and a `CHECK` constraint or a foreign key to a status master already declares
the set, that is used directly and no scan is needed.

### 4. Business

Requirements 8, 9 and 14.

**Business rules (8) are not inferable.** Data cannot show intent. These remain
stated through the existing `add business validation` command, which already
covers comparisons, required-when, either-or and arithmetic. Recorded here so
the requirement's item 8 is answered honestly rather than silently skipped.

**Cross-field (9)** is partly inferable: a comparison holding in every row —
`StartDate <= EndDate` — is proposed with its evidence and confirmed on the card.
Anything not of that shape stays user-stated.

**Calculations (14)** are verified, not computed. Where `LineTotal` equals
`Quantity * Price` across every row, the check is proposed. This matches the
requirement's own wording — verify the calculated value rather than trust the
value supplied by the UI — and needs no name-guessing at all, since the
arithmetic is discovered rather than assumed from what a field is called. The
field stays stored and stays editable; nothing the source supplied is discarded.

Candidate expressions are limited to two-operand products and sums over numeric
columns of the same entity, plus a percentage form (`Total = Net * (1 + Rate)`).
Searching a wider space would cost more scans than it is worth and would start
to produce coincidences.

### 5. Transaction

Requirement 10, and the period half of 13.

**Posting (10)** needs no inference. A `Post` runs the full pipeline over the
header and every line, and commits only if all seven stages return nothing. This
is the ordering guarantee the requirement asks for, expressed as a property of
the design rather than as a check someone must remember to write per document.

**Financial period (13)** delivers only where an app contains a period entity
with a date range and an open or closed status. Where one exists, a transaction
dated into a closed period is refused. Where none exists, this is silent.

**Stock and balance** from the requirement's tree are in the same position: they
need a ledger or a balance entity that the tool does not create and most
generated apps will not have. Where such an entity exists and a relationship to
it is detected, the checks are offered; otherwise nothing. No stock or balance
concept is invented.

The date half of 13 — a `datetime` at or after SQL Server's floor, a `date` with
no time component — is a Field-stage rule and always applies.

### 6. Workflow

Requirement 16.

The data reveals which statuses exist. It does not reveal that `Cancelled`
cannot return to `Draft`; no data source contains a transition graph. So the
Review card shows the values discovered for a confirmed status column and the
user drags them into sequence and ticks the ones that are final. The values
remain data-derived; only the ordering is supplied.

That yields two rules per status column: a move must go forward in the stated
order, and a record at a final status may not move again. Both compare `Stored`
against `Item`, which is why they need the wide context.

Approval, in the requirement's tree, is a permission question about a particular
transition — who may move a record into a given status. It is expressible once
the transition order and the permission matrix both exist, and is offered on the
card as a role per transition where a status column has been confirmed.

### 7. Database

The backstop. Constraints are emitted into the schema so the database enforces
them independently of the application, and a constraint violation coming back
from the store is translated into the same sentence the corresponding rule would
have produced — so a unique violation racing past the Entity stage reads as
*"A Customer with that code already exists"* rather than as a raw
`SqlException`.

This is what makes the pipeline safe under concurrency. Two users inserting the
same code simultaneously both pass the Entity stage; the database refuses one,
and the message is the same either way.

## Coverage of the seventeen

| # | Category | Delivered by | Strength |
|---|---|---|---|
| 1 | Required | Field stage, `IS_NULLABLE` / blanks seen | Full |
| 2 | Data Type | Field stage, carried-through type facts | Full |
| 3 | Length | Field stage, `CHARACTER_MAXIMUM_LENGTH` / longest seen | Full |
| 4 | Range | Field stage, declared bounds only | Full |
| 5 | Format / Pattern | Field stage: `CHECK`, email, phone, scanned patterns | Good |
| 6 | Lookup / Reference | Entity stage, real foreign keys | Full |
| 7 | Uniqueness | Entity stage, constraint or zero-duplicate scan | Full |
| 8 | Business Rule | Existing `add business validation` command | User-stated |
| 9 | Cross-Field | Business stage, comparison holding in all rows | Partial |
| 10 | Transaction / Document | Pipeline ordering over header and lines | Full |
| 11 | Status-Based | Entity stage, confirmed status column | Where a status exists |
| 12 | Permission | Security stage, matrix moved to the store | Full |
| 13 | Date / Period | Field stage always; period where an entity exists | Split |
| 14 | Calculation | Business stage, verified arithmetic | Good |
| 15 | Referential Integrity | Entity stage on `Delete`, via `CountReferencing` | Full |
| 16 | Workflow | Workflow stage, order stated on the card | Where a status exists |
| 17 | Security | Security stage first, outside `novalidation` | Full |

Ten are delivered in full, two well (5 and 14), one partially (9), one is split
across stages (13), one stays deliberately user-stated (8), and two are
conditional on the app containing a status column (11 and 16). Nothing on this
list is silently dropped.

## Referential integrity in detail

Requirement 15 gets its own note because it is the one category that is both
fully deliverable and entirely absent today.

On a `Delete`, the Entity stage asks the lookup how many records reference this
one, for each relationship `RelationshipDetector` knows about. A non-zero count
refuses the delete with the count in the message: *"This Customer cannot be
deleted — 3 Invoices refer to it."*

The relationships are already known, so this needs no new domain concepts and no
inference. It needs only `CountReferencing`, the `Delete` operation reaching the
validator at all, and the store calling the pipeline on delete — which today it
does not do for any check whatsoever.

## Emission

**Generated app.** Rules answerable from the record in hand are emitted onto the
model's own `Validate()` partial; the 18 existing rule kinds already write
`this.X`, so they work there untouched. Rules needing other records, the user or
the stored version go into `<Model>Validator.Generated.cs` beside the lookup.
The stage list and the short-circuit ordering live in a single
`ValidationPipeline` file per app.

Generated models gain `partial`, which shows in every diff.

**Store boundary.** `RepositoryTemplate`, `EfStoreTemplate` and
`JsonStoreTemplate` call the pipeline on insert, update, delete and post.
Delete is a new call site: nothing validates a delete today.

**Class path.** `create class` and `generate validator` have no store, so
uniqueness and reference counting cannot run. They emit a
`Validate(IEnumerable<T> existing)` overload carrying those checks, and
`RoslynClassGenerator` needs `PartialKeyword` — the `create class` path already
has `ClassValidationWriter.EnsurePartial`.

**`SaveResult`** gains a per-property attribution so a front end can highlight
the field rather than only listing sentences. `Errors` stays as it is for
compatibility, and `AsDictionary` keeps its current shape.

## The Review card

Each entity's card gains a validation section listing every inferred rule with
its reason and its evidence — *"max length 50, the column is nvarchar(50)"*,
*"Code is unique, holds in all 12,431 rows"* — and each is removable. Proposals
needing confirmation (status, uniqueness on a file source, a pattern, a
comparison, a calculation) are unticked until the user ticks them.

A confirmed status column expands to the transition editor: the discovered
values in a drag-order list, with a "final" tick per value.

`novalidation` still omits the whole layer, except the Security stage.

## Testing

- **Round-trip over the whole inference table.** Every fact in every stage table
  above produces a rule that emits code that compiles. This is the test that
  stops a rule being silently skipped.
- **Compile both partial halves with `CompileSources`**, never a concatenation —
  a concatenation hides CS0260 and has done before.
- **Counter-example scans are tested against sources that break at row 50,000**,
  not row 5. The whole point is that a sample would have passed.
- **A rule that would reject the source's own data must never be proposed.**
  Direct test per candidate kind.
- **Delete refusal** tested through the store, not the view model, since the
  store is where the check newly lives.
- **Security stage tested with `novalidation`**, proving permissions survive it.
- **Every front end**, since the store boundary is shared but the surfacing is
  not.

Roslyn compilation, not substring assertions: emitted code is tested by
compiling it.

## Behaviour changes

All deliberate, all visible in existing generated apps.

- Strings move from a blanket 200 to the real column length. This fixes apps
  that currently accept data the database rejects.
- Non-nullable columns infer `required`, where previously only `Name` and
  `Title` did. Some generated apps become stricter, correctly but noticeably.
- Transactions reject an unset foreign key that they previously accepted.
- Deletes are validated for the first time, and can now be refused.
- Permission checks are enforced at the store, so a Web API or console caller
  that previously deleted a master record freely is now refused.
- `novalidation` no longer means "no validation code at all" — the Security
  stage survives it. The manual and `docs/commands.md` both need this, and
  chapter 18's appendix is generated from `CommandDictionary`, so both
  regenerate together or `ManualAppendixDocTests` goes red.
- Generated models gain `partial`.

## Risks

- **A wrongly recognised status is the most damaging inference here**, because
  it produces a whitelist. The counter-example scan and the card confirmation
  are both aimed squarely at it, and neither may be skipped for speed.
- **Get-only calculated properties throw at runtime, not compile time** — every
  editor reflects and calls `SetValue`. This design keeps calculated fields
  stored and editable partly to avoid that trap entirely.
- **The distinct scan touches large tables.** Capped at 51 and stopping early,
  but it is a new cost on the build path and needs measuring against a real
  47-million-row table, not a fixture.
- **Seven stages is more machinery than a three-entity CRUD app needs.** Empty
  stages emitting nothing is the mitigation, and it must be verified by
  generating a small app and reading the output, not assumed.

## Out of scope

- Composite unique and composite foreign keys. `ForeignKeys` already excludes
  composite foreign keys for the same reason: the model cannot express the pair,
  and half of one is wrong rather than incomplete.
- Inventing a status, a period, a stock ledger or a branch where the data has
  none. This is the central decision, restated here so a later reader does not
  mistake an empty stage for a defect.
- More than two roles. `AppRole` has Admin and User; a richer role model is its
  own piece of work.
- Editing an inferred rule's bounds on the card. Rules can be removed, not
  retuned; `add validation` restates one where a different bound is wanted.
- Reading a SQL Server computed column definition, and stating a calculation by
  hand. Both are natural extensions once verified arithmetic is in.
- Recalculating stored values. Calculations are verified, never rewritten.

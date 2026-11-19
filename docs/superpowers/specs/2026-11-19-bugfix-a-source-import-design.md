# Bug fixes A — source import (SQL, CSV, JSON) — design

Branch: `bug-hunt-specs`. Date: 2026-11-19. Status: draft for review.
Base: `main` = 10224d0. Projects: `DataIngestion`, `AppScaffolding/DomainModelMapper.cs`.

Evidence key: **PROVEN** = reproduced by a run (probe console that drives
`SqlServerReader → NormalizationPlan → DataModelBuilder → DomainModelMapper → TabularImport →
SeedFileWriter` with a fake `ISqlSchemaSource`, or a piped CLI run). **TRACED** = read in code only.

## A1. Normalization drops `TabularTable.SourceName` — wrong foreign key (PROVEN, high)

**Where:** `DataIngestion/Normalization/NormalizationPlan.cs:261-290` `WithoutColumns`. It copies
`Name`, `SourceLabel`, `IsPartial`, `SourceRowCount`, `ForeignKeys`, `Notes`, but not `SourceName`
(added by PR #171, set in `SqlServerReader.cs:52` when a table is renamed for its schema).
`DataModelBuilder.cs:247` uses `SourceName` to recognise the table's own key.

**Failure:** `dbo.Note` and `hr.Note`, both with PK `NoteID`; `hr.Note` has `Tag1..Tag3`; the user
accepts the repeating-group split. `HrNote` gets `int NoteId` + `Note Note`, `Note` gets
`List<HrNote> HrNotes`, and HrNote's key values are renumbered through dbo.Note's keys. With
overlapping key ranges, hr rows link silently to the wrong dbo.Note rows; otherwise the links are
cleared with "no row … carries the key". Without normalization the output is correct.

**Fix:** add `SourceName = table.SourceName` in `WithoutColumns`. Then review every
`new TabularTable` in `NormalizationPlan.cs` (lines 160, 212, 270) against the full member list
of `Schema/TabularTable.cs`, and add a reflection test so the next new member cannot be lost:
build a table with every settable member set, run it through `WithoutColumns`, and assert every
member except `Columns`/`Rows` is equal.

**Tests (red, then green):**
- `dbo.Note` + `hr.Note` (Tag1..Tag3), apply the `NormalizationAnalyzer` findings: `HrNote` has no
  `NoteId` field and no relationship to `Note`.
- The reflection copy test above.

## A2. CHECK range kept on an `int` foreign key after renumbering (PROVEN, high)

**Where:** `DataIngestion/DataModelBuilder.cs:853` — `if (field.Type == "int") continue;` keeps the
link field's `Facts`, including `MinValue`/`MaxValue` from `SqlServerReader.CarryConstraints`.
The text→int branch below it already drops them. `DomainModelMapper.AddIntegerRange`/`AddRange`
then emit range rules.

**Failure:** `Employee.DepartmentID` FK with `CHECK ([DepartmentID]>=(100) AND [DepartmentID]<=(999))`,
Department PKs 100 and 200. Output: `DepartmentId` rules `minvalue:100,maxvalue:999`, seed holds
`1` and `2`. Every seeded Employee fails "DepartmentId must be 100 or more", and the generated
`A_populated_Employee_is_valid` test fails (Fill gives the FK row Id 1).

**Fix:** for every link field (int included), clear `MinValue`, `MaxValue` and `AllowedValues`
from `Facts`, as the text→int branch does. Keep `IsNullable` and `HasMissingValues`. A range on
source key values says nothing about the renumbered Ids.

**Tests:** the Employee/Department case: no min/max rule on `Employee.DepartmentId`; every seeded
row passes the generated validator.

## A3. Text foreign keys match case-sensitively (PROVEN, medium)

**Where:** `TabularImport.RewriteSourceKeys` / `PositionsOf` (ordinal dictionary; `KeyText` only
trims); `DataModelBuilder.NamesARow` (ordinal).

**Failure:** Customer PK `CustomerCode` = `C001`, `C002`; Orders FK values `c001`, `C002`. SQL
Server's default (CI) collation accepts this. Order 1 is seeded with no `CustomerId`, with the
note "no row in your data carries the key".

**Fix:** for the SQL source, match exactly first, then case-insensitively
(`StringComparer.OrdinalIgnoreCase`) when there is no exact match. When two parent keys differ
only by case (a CS-collation database), keep the exact match only and add a note. CSV/JSON sources
keep ordinal matching (no collation to follow).

**Tests:** the Customer/Orders case: Order 1 gets `CustomerId == 1`. A second case with parents
`a1` and `A1`: child `a1` links to the exact row.

## A4. CHECK value list enforced case-sensitively (PROVEN at model + seed; emitted check TRACED, medium)

**Where:** `ValidatorTemplate.cs` ~939 emits `System.Array.IndexOf(new[]{...}, item.X) < 0`
(ordinal); `SqlCheck` and `SqlServerReader.Bounded` intersect lists with `StringComparer.Ordinal`.

**Failure:** `CHECK ([Status]='Closed' OR [Status]='Open')` with a stored row `open` (CI database).
Rule `allowedvalues:Closed|Open`, seed holds `open`; that record cannot be saved until retyped.

**Fix (recommended):** when the seed is written, map each value to the listed spelling it equals
case-insensitively (`open` → `Open`) and add one note per column ("3 values were re-cased to match
the CHECK list"). The emitted check stays ordinal, so the app keeps one spelling per value.
Values that match no listed entry case-insensitively stay as they are (they were already invalid).

**Tests:** generate from the Status case, compile, validate every seeded row: all valid; the note
is present.

## A5. A DEFAULT that does not fit the type is dropped without a note (PROVEN, low)

**Where:** `SqlServerReader` notes a default only when `SqlDefault.Parse` returns null;
`DomainModelMapper.Initializer` returns null silently when the value does not fit the type.

**Failure:** `datetime DEFAULT ((0))` (SQL: 1900-01-01) and `date DEFAULT ('20200101')`
(`DateTime.TryParse` rejects `yyyyMMdd`). No initializer, no note; new records start at
0001-01-01.

**Fix:**
1. Parse `yyyyMMdd` (and `yyyyMMdd HH:mm:ss`) with `ParseExact`, invariant culture.
2. For `datetime`/`smalldatetime`, read a numeric default as days since 1900-01-01.
3. When `Initializer` still returns null for a non-null `DefaultValue`/`DefaultFunction`, add a
   "default not carried: <text>" note to the class.

**Tests:** the two inputs above get initializers `new DateTime(1900,1,1)` and
`new DateTime(2020,1,1)`; a default of `'abc'` on an `int` gives a note.

## A6. SQL `uniqueidentifier` seeded lowercase (TRACED, low)

**Where:** `DataIngestion/Schema/Readers/Sql/SqlValueText.cs:33` — a `Guid` takes the
`IFormattable` branch, `ToString(null)` gives lowercase "D" format; SSMS shows uppercase.

**Failure:** the unique check compares ordinally (`StoreEntityLookup`), so a GUID pasted from SSMS
in uppercase saves as a second record with the same GUID; searches with SSMS text miss.

**Fix:** add `Guid g => g.ToString("D").ToUpperInvariant()` before the `IFormattable` arm. Check
`SqlDefault`'s `newid()` initializer and use the same casing. Better still for the unique check:
compare Guid-typed properties as `Guid`, not text (separate, optional).

**Tests:** `Render(Guid.Parse("6F9619FF-8B86-D011-B42D-00C04FC964FF"))` equals the uppercase text.

## A7. `nulls.csv` makes entity `Null` (PROVEN, low)

**Where:** `CsvReader.cs:24` names the table from the file; `NameNormalizer.ToUserTypedEntityName`
gives `Null`; `DomainModelMapper.ReservedNames` (lines 22-56) lacks it. The generated
`NullValidator` sits beside the built-in `NullValidator<T>` in `ValidatorFactory.cs`
("NullValidator" is already reserved for that reason). It compiles, but the name is meaningless.

**Fix:** add `Null`, `Nil`, `Void`, `Undefined` to `ReservedNames`, so the class becomes
`NullItem` / `NullItemValidator` through the existing reserved-name path.

**Tests:** `DomainModelMapperTests`: entity `Null` maps to class `NullItem`.

## A8. File-sourced dates get no required rule — Web API saves 0001-01-01 (PROVEN, medium)

**Where:** `AppScaffolding/DomainModelMapper.cs:693` and `:723` add `required` only when
`facts?.IsNullable == false`. Only the SQL reader sets that fact; `DataModelBuilder.EffectiveFacts`
/ `WithMissingValues` never set it for CSV/JSON; `RuleProposals.For` never proposes `required`.

**Failure:** JSON with `DueOn` filled in every row → non-nullable `DateTime DueOn`, no check.
`POST /taskitem {"title":"x","qty":1}` returns `"dueOn":"0001-01-01T00:00:00"`.

**Fix:** in `DataModelBuilder.EffectiveFacts`, for a `DateTime`/`DateOnly` column with no blank
values in the source rows, return facts with `IsNullable = false`. The existing
"today's default date counts as unfilled for the blank check" rule (PR #170) stays.

**Tests:** `DataModelBuilderTests`: CSV with a fully filled date column → mapped property has a
`required` rule. A generated-validator compile test refuses a record whose date is `default`.

## Order of work

A1, A2 (wrong data, silent) → A8, A3, A4 → A5, A6, A7. After all: full DataIngestion and
CodeGenerator suites, then one live `build app ... from sql` and one CSV run, and the app-check
harness.

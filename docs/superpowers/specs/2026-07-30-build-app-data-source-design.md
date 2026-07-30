# `build app` from existing data — design

**Date:** 2026-07-30
**Status:** approved, not yet implemented

## Problem

`build app: <description>` derives a domain model from free text: the NLE extracts seed
entities, `WikipediaTopicRecognizer` picks an article, `DomainEnricher` harvests more classes
from it, `EntityShaper` decides what is a class at all. That is the right pipeline when the
user has nothing but an idea.

It is the wrong pipeline when the user already has the data. A CSV of orders states the
domain exactly — its columns are the fields, its values are the types, its rows are the
starting data — and guessing at Wikipedia instead throws all of that away.

## Goal

When the user has data, read the schema from the data, check it for normalization problems,
and generate the app from what the data actually says. When they do not, change nothing.

## Scope

**This branch:** the interview, the reader seam, CSV / TXT / JSON, value-based type
inference, the full normalization analyser, and importing rows as the generated app's seed
data.

**Deliberately not this branch:** Excel, SQL Server, and any live connection from the
generated app back to the user's database. The seam below is shaped so each of those is an
additive reader and nothing else. Choosing `excel` or `sql` at the prompt reports that it is
not in this build yet and names what to use instead — it does not crash and does not silently
fall back to Wikipedia.

## The seam

Every reader produces one shape:

```
IDataSourceReader :  DataSourceSpec  ->  TabularTable[]
```

`TabularTable` is `(Name, TabularColumn[], Rows)`. It has three consumers — the normalization
analyser, the model builder, and the seed-data importer — and they all work on it alone. This
is the whole reason Excel and SQL Server are cheap later: they add a reader and touch nothing
downstream.

## Flow

```
build app[: description]
   |
   +- inline `from <format> <loc>` present? --> DataSourceSpec, no prompts
   +- stdin redirected, no inline source?    --> current Wikipedia path (unchanged)
   +- interactive --> "Do you have existing data? [y/N]"
                        n --> current Wikipedia path (unchanged)
                        y --> "Format [sql/excel/txt/json/csv]"
                              --> location, or connection + database + tables
                              --> read                 --> TabularTable[]
                              --> normalization check  --> n / n <ids> / k / d <id>
                              --> CandidateDomainModel + ImportedData
   v
RunReviewLoop   (verbs unchanged: add remove rename field unfield why approve cancel)
   v
AppScaffolder.Scaffold(..., importedData)
```

The data path makes **no Wikipedia call**. The schema is ground truth, so there is nothing to
recognize, enrich or shape; the description survives only as input to `DomainAppName`.

### Why redirected stdin never prompts

`BuildAppCommand.ResolveTarget` already sets this precedent, and existing piped scripts and
E2E tests feed the review loop directly from stdin. Prompting them would break every one of
them. So redirected stdin with no inline source takes the "no data" path and behaves exactly
as it does today, and scripts opt in explicitly with the inline form.

## Command surface

| Input | Behaviour |
|---|---|
| `build app: order tracking` | today's behaviour when piped; interview when interactive |
| `build app` (no colon) | interactive: interview. Piped: usage message |
| `build app: order tracking from csv D:\orders.csv` | no prompts, scriptable |
| `build app from csv D:\orders.csv` | no description; app name from the file name |
| `... from csv <path> normalize` | applies every normalization finding non-interactively |

`from` is recognized only at the **last** occurrence followed by a known format keyword, so a
description containing the word "from" is not truncated.

**One source per run.** A file path names one file; a JSON file may still yield several tables
through nesting. Multiple CSVs in one run is out of scope — run `build app` again.

With no description, the source's base name (`orders.csv` -> `orders`) is passed to
`DomainAppName.FromDescription`, so app naming has exactly one rule rather than a second one
invented for this path.

A location that does not exist re-prompts rather than aborting, matching `ResolveTarget`'s
re-prompt on an unknown target key. Inline, a missing file is an error and the command stops.

`BuildAppCommand.IsBuildApp` widens from the `build app:` prefix to `build app`. This is safe:
`build app` appears in no recognition source — not `LiteralCommandTable`, not a `CommandFront`
guard, not `NlpCommandMapper` — only in the `CommandDictionary` help catalog, which this
branch updates (there is an audit test over that catalog).

## Project layout

A new project `DataIngestion` with tests in `DataIngestion.Tests` (xunit, matching
`DomainDiscovery.Tests`). Both are added to `SmartCodeGenerator.slnx`; CI restores, builds and
tests the whole slnx, so no workflow change is needed.

It is its own project rather than a folder inside `DomainDiscovery` for two reasons.
`DomainDiscovery` means *Wikipedia* in this repo and carries `Microsoft.Data.Sqlite` and
`SharpZipLib` with it; and this project's own coming dependencies (`DocumentFormat.OpenXml`,
`Microsoft.Data.SqlClient`) must not leak into the discovery path. It references
`DomainDiscovery` for `CandidateDomainModel`, `NameNormalizer` and `DecisionRecord`.

```
DataIngestion/
  DataSourceKind.cs            enum { Sql, Excel, Txt, Json, Csv }
  DataSourceSpec.cs            Kind, Location, SqlDatabase?, SqlTables?
  DataSourceInterview.cs       prompts over TextReader/TextWriter (testable)
  InlineDataSource.cs          parses the `from <format> <loc>` tail
  Schema/
    TabularTable.cs  TabularColumn.cs  IDataSourceReader.cs
    ColumnTypeInference.cs
    Readers/
      CsvReader.cs             RFC4180-ish: quotes, embedded delimiters and newlines
      DelimitedTextReader.cs   TXT, with delimiter sniffing
      JsonTableReader.cs       array-of-objects, plus nested object/array as child tables
      ExcelReader.cs           follow-on branch
      SqlServerReader.cs       follow-on branch
  Normalization/
    NormalizationFinding.cs  NormalizationAnalyzer.cs  NormalizationPlan.cs
  DataModelBuilder.cs         TabularTable[] -> CandidateDomainModel
  ImportedData.cs             entity name -> rows, carried to the scaffolder
```

## Types

Inferred from **values**, not from the column name. Real data is better evidence than
`TypeInference.Infer(name)`, which stays only as the fallback for a column with no non-empty
values.

Only the five types in `ModelEditor.SupportedTypes` are producible — `string`, `int`,
`decimal`, `bool`, `DateTime` — so a column of GUIDs lands as `string` and integers beyond
`int.MaxValue` as `decimal`. A sample of up to 200 non-empty values is tried in order:

1. all parse as `int` -> `int`
2. all parse as `decimal` -> `decimal`
3. all in `{true false yes no y n}` -> `bool`
4. all parse as `DateTime` -> `DateTime`
5. otherwise -> `string`

`bool` requires those word forms. A bare `0/1` column stays `int`, because a column wrongly
typed `bool` cannot round-trip a `2`, while one wrongly typed `int` loses nothing — and the
review loop can correct it either way.

`yyyymmdd` dates parse as `int` and will be typed `int`. Accepted: it is visible in the
review loop and fixable with `field`.

## Names

Entity name from the table or file name, singularized: `orders.csv` -> `Order`.

Two collisions that existing code proves are fatal, and that real data will hit:

- an `Id` or `<Entity>Id` primary-key column is **dropped**. `DomainModelMapper` prepends
  `int Id` itself only when no Id field exists, so keeping a string Id compiles and then
  `JsonStore` — which assigns Id by reflection — throws the first time the user clicks Add.
  This is the same trap `ModelEditor.AddField` already guards.
- a column whose normalized name equals its entity's name (`Product` in `Products.csv`) is
  renamed to `<Entity>Name`, falling back to `<Entity>Value` if that collides.
  `class Product { public string Product { get; set; } }` is CS0542.

`Provenance` gains a third value, `Data`, so the review loop prints `- Order [Data]` instead
of mislabelling a real column as `[Seed]`. `Provenance` is only ever used as `.Seed`/`.Wiki`
with no exhaustive switch anywhere, so this is additive.

## Normalization analyser

Four finding kinds. Each carries the columns it is about, the entity it proposes, and the
evidence behind it.

| # | Kind | Detection | Proposal |
|---|---|---|---|
| 1 | Repeating group (1NF) | >=2 columns sharing a stem with an ordinal suffix: `Item1/Item2/Item3`, `Phone_1/Phone_2` | child entity `<Stem>`, one row per non-empty cell, FK to parent |
| 2 | Non-atomic values (1NF) | a string column where >=60% of non-empty values contain one consistent separator (`,` `;` `\|`) **and** at least one split part appears in 2 or more rows | child entity `<Column>` singularized, FK to parent |
| 3 | Partial / transitive dependency (2NF/3NF) | for a non-key column D with between 2 and rowCount/2 distinct values, every D value maps to exactly one value in each of columns C1..Cn (n >= 1) | extract `<D-stem>` entity holding D + C, parent keeps an FK |
| 4 | Derived column (3NF) | a numeric column exactly equal to the product or the sum of two others on every row | drop it as computed |

**Recurrence is what separates finding 2 from prose.** A `Notes` column full of commas splits
into parts that never repeat, so it is never offered.

**Finding 3 needs a second signal, or it is noisy.** A determinant with one dependent is
reported only when D and that dependent share a name prefix (`CustomerName` / `CustomerCity`);
with two or more dependents, the dependency itself is signal enough. Without this, any column
that happens to determine one other column gets flagged.

**Finding 3 needs rows to be true of.** Under 5 rows the analyser reports "too few rows to
test dependencies" rather than manufacturing a split out of three coincidences.

Every threshold above — 60%, 2 recurrences, rowCount/2, 5 rows, the 200-value type sample,
`MaxSeedRows` — is a named constant, so the tests pin the same value the analyser uses rather
than a copy that can drift.

### Prompt

```
Normalization check on Orders.csv (500 rows):

  1 ! Repeating group: Item1, Item2, Item3
        -> split into OrderItem (many per Order)
  2 ! Partial dependency: CustomerName, CustomerCity
        -> split into Customer (Order references it)
  3 ! Non-atomic: "Tags" holds comma-separated values
        -> split into Tag

  [n]ormalize   n <1 3> subset   [k]eep as-is   [d]etail <n>
```

- `n` applies every finding; `n 1 3` applies that subset; `k` keeps the flat table
- `d 2` prints the rows behind finding 2 — the `CustomerName` values and the single `City`
  each maps to

`d` exists because a finding the user cannot inspect is a finding they cannot trust. It is the
same principle as the `why` verb, which this repo already built for exactly that reason.

Non-interactive runs print the findings and **apply nothing** unless `normalize` was passed
inline, so scripts stay deterministic.

Every applied split writes a `DecisionRecord`, so `why OrderItem` in the review loop answers
"extracted from the repeating group Item1, Item2, Item3 in Orders.csv". This needs a new
`DecisionStage.Normalization` and four new `DecisionReasons` constants — constants, never
ad-hoc literals, per that file's own rule.

## Importing rows as seed data

`ImportedData` (entity -> rows) travels with the model into `AppScaffolder.Scaffold`.

- the scaffolder writes `Data/Seed/<Entity>.json` per entity, registered in the csproj as
  `Content` with `CopyToOutputDirectory=PreserveNewest`
- `SeedDataTemplate` gains a second mode: **if a seed file exists, deserialize it and
  `store.SaveAll(rows)`; otherwise emit the three reflective sample rows exactly as today.**
  It goes through the `IStore<T>` seam, so JSON and SQLite persistence both work with no
  further change
- Ids are assigned 1..N in file order. FK columns produced by a normalization split are
  rewritten to the parent's assigned Id **at generation time**, so the emitted JSON already
  holds correct ints and nothing resolves keys at runtime
- rows are capped at `MaxSeedRows` (1000), and the cap is **stated**:
  `Imported the first 1000 of 48,213 rows as seed data.` A silent truncation would read as
  "all your data is in there".

## Testing

Unit, in `DataIngestion.Tests`:

- CSV: quoted fields, embedded delimiters, embedded newlines, ragged rows, BOM, CRLF and LF
- TXT: delimiter sniffing across tab / pipe / semicolon / comma; a non-tabular file rejected
  with a message that says what to do
- JSON: array-of-objects to one table; nested object and nested array to child tables with FKs
- `ColumnTypeInference` as a value-set -> expected-type table, including `0/1 -> int` and the
  all-empty name fallback
- naming: the Id drop, the `Product.Product` rename, plural table to singular entity
- each finding kind positive **and** negative, plus the too-few-rows guard
- FK rewrite, and the row cap

Interview and inline parsing driven by `StringReader`/`StringWriter`, the pattern
`BuildAppTargetSelectionTests` already uses — including a description containing the word
"from", and redirected stdin taking the unchanged Wikipedia path.

End-to-end in `CodeGenerator.Tests`: scaffold from a fixture CSV and **compile the generated
project** through the existing `GeneratedProjectBuildTests` path, then assert the seed JSON is
present and parses. Substring assertions over generated source do not catch CS0023 or an
injected literal; compiling does.

Every test written red first. An inverted `DoesNotContain`, or an assertion on a name the
fixture itself declares, passes green while proving nothing.

## Follow-on branches

1. **Excel** — `ExcelReader` over `DocumentFormat.OpenXml` (Microsoft-published, no COM, no
   Excel install, permissive licence; `.xlsx` only, not legacy `.xls`). One sheet per table.
2. **SQL Server** — `SqlServerReader` over `Microsoft.Data.SqlClient`. Connection, database
   and table selection at the prompt; real declared types and real foreign keys, so it feeds
   `CandidateRelationship` directly rather than inferring relationships.

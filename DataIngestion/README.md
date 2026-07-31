# DataIngestion

Reads a data source the user **already has** and turns it into the same candidate
domain model [`DomainDiscovery`](../DomainDiscovery/README.md) produces from a
description — plus the rows, carried through as the generated app's seed data.

A CSV of orders states the domain exactly: its columns are the fields, its values
are the types, its rows are the starting data. When the user has that, guessing at
Wikipedia instead throws all of it away. This project is the other input path.

This is a class library (`net10.0`) with no console I/O. The
[`CodeGenerator`](../CodeGenerator/README.md) CLI drives it from `BuildAppCommand`,
which decides between this path and the unchanged discovery path.

## The seam

Every reader produces one shape, and only three things consume it:

```
IDataSourceReader :  DataSourceSpec  ->  TabularTable[]
                                             │
                     ┌───────────────────────┼───────────────────────┐
                     ▼                       ▼                       ▼
          NormalizationAnalyzer       DataModelBuilder         TabularImport
          (find + explain)            (→ CandidateDomainModel) (→ ImportedData)
```

`TabularTable` is `(Name, TabularColumn[], Rows)` and its rows hold **raw
strings**, because CSV, TXT and JSON all arrive as text and typing is an inference
over that text. Conversion happens once, at seed-writing time, so no two stages
have to agree about when a value became typed.

That single shape is why Excel cost one more `IDataSourceReader` and nothing
downstream, and why SQL Server — the one format left — will cost the same.

## Where it sits

```
build app[: description]
   │
   ├─ inline `from <format> <loc>` present? ──▶ DataSourceSpec, no prompts
   ├─ stdin redirected, no inline source?    ──▶ discovery path (unchanged)
   └─ interactive ──▶ "Do you have existing data? [y/N]"
                        n ──▶ discovery path (unchanged)
                        y ──▶ format ──▶ location
                              ──▶ read                ──▶ TabularTable[]
                              ──▶ normalization check ──▶ n / n <ids> / k / d <n>
                              ──▶ CandidateDomainModel + ImportedData
   ▼
review loop (CLI)   verbs unchanged: add remove rename field unfield why approve cancel
   ▼
AppScaffolding      writes Data/Seed/<Class>.json alongside the generated app
```

The data path makes **no Wikipedia call at all**. The schema is ground truth, so
there is nothing to recognize, enrich or shape; the description survives only as
input to `DomainAppName`.

## Formats

| Format | Key | Status |
| --- | --- | --- |
| CSV | `csv` | RFC4180-shaped: quoted fields, embedded delimiters and newlines, doubled quotes, ragged rows, BOM, CRLF/LF. |
| Delimited text | `txt` | Delimiter **sniffed** across tab / pipe / semicolon / comma. A non-tabular file is rejected with a message saying what to use instead. |
| JSON | `json` | Array-of-objects to one table; a nested array becomes a child table with a foreign key, a nested object is flattened onto the parent. |
| Excel | `excel` | One visible sheet per entity. Shared strings, inline strings, formula results, booleans and sparse rows; a date serial becomes a date and a `00000`-formatted code keeps its zeros. Hidden sheets skipped; legacy binary `.xls` refused with instructions. |
| SQL Server | `sql` | Not in this build. Named at the prompt and answered with what to use instead — never a crash, never a silent fallback. |

SQL is deliberately on the menu: a menu missing it reads as "we never thought of
SQL" rather than "SQL is next". Exactly one place — `DataSourceReaders` — knows
which formats have readers.

## Types

Inferred from **values**, not from the column name: real data is better evidence
than a name heuristic, which stays only as the fallback for a column with no
non-empty values at all.

Only the five types `ModelEditor.SupportedTypes` allows are producible, so a
column of GUIDs lands as `string` and an integer past `int.MaxValue` as `decimal`.
Up to `ColumnTypeInference.TypeSampleSize` (200) non-empty values are tried in
order: all `int` → `int`; all `decimal` → `decimal`; all in
`{true false yes no y n}` → `bool`; all `DateTime` → `DateTime`; otherwise
`string`.

A bare `0`/`1` column stays `int`. A column wrongly typed `bool` cannot
round-trip a `2`, while one wrongly typed `int` loses nothing — and the review
loop can correct either.

## Names

The entity name comes from the table or file name, singularized: `orders.csv` →
`Order`. Two collisions the existing code proves are fatal are handled here:

- an `Id` or `<Entity>Id` primary-key column is **dropped**. `DomainModelMapper`
  prepends `int Id` itself only when no Id field exists, so keeping a string Id
  compiles and then makes `JsonStore` — which assigns Id by reflection — throw the
  first time the user clicks Add.
- a column whose normalized name equals its entity's name (`Product` in
  `Products.csv`) is renamed to `<Entity>Name`, falling back to `<Entity>Value`.
  `class Product { public string Product { get; set; } }` is CS0542.

`DataModelBuilder.FieldNames` is the **single source of truth** for column-to-field
naming, and `TabularImport` uses it too. That matters: if the two computed names
separately, a renamed column's seed rows would be keyed `Order` while the generated
class declared `OrderName`, and `SeedFileWriter` — which walks the class — would
silently write nothing for it on every row.

Entities and fields read from data carry `Provenance.Data`, so the review loop
prints `- Order [Data]` rather than mislabelling a real column as `[Seed]`.

## Normalization

Four finding kinds. Each carries the columns it is about, the entity it proposes,
and the evidence behind it.

| # | Kind | Detection | Proposal |
| --- | --- | --- | --- |
| 1 | Repeating group (1NF) | ≥2 columns sharing a stem with an ordinal suffix: `Item1/Item2`, `Phone_1/Phone_2` | child entity `<Stem>`, one row per non-empty cell, FK to parent |
| 2 | Non-atomic values (1NF) | a string column where ≥`NonAtomicShare` (60%) of non-empty values contain one consistent separator **and** at least one split part recurs | child entity `<Column>` singularized, FK to parent |
| 3 | Partial / transitive dependency (2NF/3NF) | every value of a non-key column D maps to exactly one value in each of C1..Cn | extract `<D-stem>`, parent keeps an FK |
| 4 | Derived column (3NF) | a numeric column exactly equal to the product or sum of two others on every row | drop it as computed |

Two guards keep the report honest rather than noisy:

- **Recurrence is what separates finding 2 from prose.** A `Notes` column full of
  commas splits into parts that never repeat, so it is never offered.
- **Finding 3 needs a second signal.** A determinant with one dependent is
  reported only when the two share a name prefix (`CustomerName`/`CustomerCity`);
  with two or more dependents the dependency is signal enough. And under
  `MinRowsForDependencies` (5) rows the analyser reports *"too few rows to test
  dependencies"* rather than manufacturing a split out of three coincidences.

Every threshold is a named `public const` on the class that uses it, so a test
pins the same value the analyser uses rather than a copy that can drift.

### The prompt

```
Normalization check on orders.csv (500 rows):

  1 ! Repeating group: Item1, Item2, Item3
        -> split into OrderItem (many per Order)
  2 ! Partial dependency: CustomerName, CustomerCity
        -> split into Customer (Order references it)

  [n]ormalize   n <1 3> subset   [k]eep as-is   [d]etail <n>:
```

`n` applies every finding, `n 1 3` that subset, `k` keeps the flat table, and
`d 2` prints the rows behind finding 2. `d` exists because a finding the user
cannot inspect is a finding they cannot trust — the same principle as the `why`
verb. An out-of-range number is reported rather than ignored, so the user is never
told a split was applied that was not.

Every applied split writes a `DecisionRecord` under `DecisionStage.Normalization`,
so `why OrderItem` in the review loop answers for a class nobody named.

Non-interactive runs print the findings and **apply nothing** unless `normalize`
was passed inline, so scripts stay deterministic.

## Seed data

`TabularImport` produces `AppScaffolding.ImportedData` — entity name → rows, keyed
by final field name — which travels with the model into `AppScaffolder.Scaffold`.
Rows are capped at `ImportedData.MaxSeedRows` (1000) and the cap is **stated**:

```
Imported the first 1000 of 48,213 rows from orders.csv as seed data.
```

A silent truncation would read as "all your data is in there", which is a false
claim about someone's data.

## Layout

| Path | Responsibility |
| --- | --- |
| `DataSourceKind.cs` | `enum { Sql, Excel, Txt, Json, Csv }` |
| `DataSourceSpec.cs` | where the data is: kind, location, and the two SQL-only members |
| `InlineDataSource.cs` | parses the scriptable `from <format> <location> [normalize]` tail |
| `DataSourceInterview.cs` | the interactive prompts, over `TextReader`/`TextWriter` |
| `DataModelBuilder.cs` | `TabularTable[]` → `CandidateDomainModel`, and `FieldNames` |
| `TabularImport.cs` | `TabularTable[]` → `ImportedData` |
| `Schema/TabularTable.cs`, `TabularColumn.cs` | the one shape every reader produces |
| `Schema/IDataSourceReader.cs` | the seam |
| `Schema/ColumnTypeInference.cs` | values → one of the five types |
| `Schema/Readers/` | `CsvReader`, `DelimitedTextReader`, `JsonTableReader`, `ExcelReader` (with `ExcelWorkbook`, `ExcelSheetReader`, `ExcelNumberFormats`, `ExcelSerialDate`), and the `DataSourceReaders` registry |
| `Normalization/` | `NormalizationFinding`, `NormalizationReport`, `NormalizationAnalyzer` (+ `.Dependencies`), `NormalizationPlan`, `NormalizationPrompt` |

## Why its own project

Not a folder inside `DomainDiscovery`, for two reasons. `DomainDiscovery` means
*Wikipedia* in this repo and carries `Microsoft.Data.Sqlite` and `SharpZipLib`
with it; and this project's own coming dependencies (`DocumentFormat.OpenXml`,
`Microsoft.Data.SqlClient`) must not leak into the discovery path.

It references `DomainDiscovery` for `CandidateDomainModel`, `NameNormalizer` and
`DecisionRecord`, and `AppScaffolding` for `ImportedData`. Neither references it
back, so there is no cycle.

## Tests

[`DataIngestion.Tests`](../DataIngestion.Tests/README.md) (xUnit). The
compile-the-generated-app end-to-end test lives in
[`CodeGenerator.Tests`](../CodeGenerator.Tests/README.md), which is the only suite
that sees both this project and the scaffolder.

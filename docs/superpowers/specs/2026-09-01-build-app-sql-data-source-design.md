# `build app ... from sql` — design

**Date:** 2026-09-01
**Status:** approved, not yet implemented
**Branch:** `sql-data-source`

## Problem

`build app` can build a model from a CSV, a delimited text file, JSON or an Excel workbook. It
cannot build one from a database, which is where the data of every business this tool is aimed at
actually lives. A user with a running system has a schema; asking them to export it to CSV first is
asking them to throw away the two things the schema knows and a CSV does not — the column types and
the foreign keys.

The gap is advertised rather than hidden, which makes it worse. `DataSourceReaders.Keys` lists
`sql` first, `Aliases` accepts `sql`, `sqlserver`, `mssql` and `database`, and
`DataSourceSpec` already carries `SqlDatabase` and `SqlTables` members that nothing populates.
`BuildAppCommand.AppNameFor` already names the generated app from `source.SqlDatabase` when the kind
is `Sql`. `IDataSourceReader`'s own summary says "adding Excel or SQL Server is one more
implementation of this and no change anywhere downstream". Every part of the seam was built for
this except the reader, and a user who picks `sql` gets:

> Sql is not in this build yet — it lands in a follow-on branch.

The `docs/commands.md` row for `from <format> <location>` says the same in parentheses.

There is a second, quieter cost. Every existing reader produces strings and then asks
`ColumnTypeInference` what they were. That inference is the origin of the four documented ways this
pipeline can silently corrupt imported data — leading zeros read as integers, thousands separators
read as text, loose `DateTime.TryParse`, and per-entity row caps. A database column has a declared
type. Reading it means none of those questions gets asked on this path at all.

## Goal

Point `build app` at a SQL Server connection string, choose from the tables it finds, and get a
model whose classes carry the database's own column types and whose relationships are the
database's own foreign keys rather than a guess.

## Scope

**In:**

- A `Microsoft.Data.SqlClient` package reference on `DataIngestion`.
- `SqlServerCatalog` — lists base tables with column and row counts, reading no data.
- `SqlServerReader` — implements `IDataSourceReader` for the tables the user picked.
- `TabularForeignKey` on `TabularTable`, and its projection into `CandidateRelationship` in
  `DataModelBuilder`.
- A SQL branch on the workflow's Data stage: connect, pick tables, read.
- `tables A,B,C` on the `build app:` line, for piped runs.
- Redaction of any password in the connection string wherever it is echoed.
- The catalog and manual entries that currently say `sql` is unavailable.

**Out:**

- Any engine other than SQL Server. SQLite would need a second introspector for no new capability;
  a `.db` file is not what the `sqlserver`/`mssql` aliases promise.
- Views, stored procedures, and anything that is not a base table.
- Writing back to the database. This reads.
- Composite foreign keys — see *Risks*.

## Decisions taken before this was written

Each of these was a fork with a real alternative, settled with Rohit:

- **SQL Server, not SQLite.** `Microsoft.Data.Sqlite` is already referenced by `CodeIntelligence`
  and would have cost no new dependency, but it answers `sql` with the one database a business user
  is least likely to be handed, and the registered aliases would have had to start refusing. SQL
  Server needs `Microsoft.Data.SqlClient`, a genuinely new package. There is no hand-written
  alternative the way `ExcelReader` avoided a package by parsing a zip: TDS is a network protocol.
- **Pick the tables, do not take them all.** Northwind has 13 tables, a real ERP has hundreds, and
  both have migration-history tables nobody wants as a class.
- **All rows of the tables that were picked, with no cap.** A cap was considered and rejected. The
  picker shows each table's row count *before* anything is ticked, so the choice is informed per
  table rather than blind, and a silent truncation would be a fifth entry on the list of ways this
  pipeline can lie about imported data.

## Part 1 — reading the database

Two types, because the workflow asks two different questions at two different moments, over
one shared seam.

```csharp
public sealed record SqlTableInfo(
    string Schema, string Name, int Columns, long? Rows);   // null when unreadable, see Risks

public sealed record SqlColumnInfo(string Name, string SqlType, bool Nullable);

/// The whole of this feature's contact with ADO.NET. One thin adapter implements it over
/// SqlClient; everything above it is tested against a fake.
public interface ISqlSchemaSource
{
    IReadOnlyList<SqlTableInfo> Tables(string connectionString);
    IReadOnlyList<SqlColumnInfo> Columns(string connectionString, string schema, string table);
    IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString);
    IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table);
}
```

`Rows` returns strings because `TabularTable.Rows` holds `string?[]`; the adapter renders each
value from its known SQL type with `InvariantCulture`, which is what keeps this path clear of
`ColumnTypeInference` and the traps that live there.

### `SqlServerCatalog`

*What is in this database?* Must be cheap: it runs before the user has chosen anything, and a
picker that takes thirty seconds to appear reads as a hang.

Base tables only, from `INFORMATION_SCHEMA.TABLES` where `TABLE_TYPE = 'BASE TABLE'`.

Row counts come from `sys.dm_db_partition_stats`, not `COUNT(*)`. Exact counts across several
hundred tables are slow enough to defeat the purpose of the phase. Those counts are
engine-maintained and near-exact rather than exact, so the column is labelled to say so; claiming a
precision we did not pay for would be the wrong trade in the other direction.

The two-part name is kept. Two schemas may both hold `Orders`, and a picker that shows `Orders`
twice with no way to tell them apart is not a picker.

### `SqlServerReader : IDataSourceReader`

*Give me these tables.* Reads `spec.SqlTables`, all rows, into `TabularTable`s.

`spec.Location` is the connection string. `spec.SqlDatabase` is its `Initial Catalog`, parsed with
`SqlConnectionStringBuilder` rather than by splitting on `;` — `AppNameFor` already reads that
member to name the generated app.

Column types come from `SqlTypeMap` (below), so this reader never calls `ColumnTypeInference`.
Values are still rendered to `string?` because that is what `TabularTable.Rows` holds, but they are
rendered from a known type with an invariant format, which is the opposite direction of travel from
the file readers and is why the corruption traps do not apply.

A table named in `SqlTables` that no longer exists fails with `InvalidDataException` naming it,
rather than silently producing a smaller model than the user asked for.

### `SqlTypeMap`

An explicit table, not inference. The five types `ModelEditor.SupportedTypes` allows are the whole
target set.

| SQL Server | C# |
|---|---|
| `int`, `smallint`, `tinyint`, `bigint` | `int` |
| `decimal`, `numeric`, `money`, `smallmoney`, `float`, `real` | `decimal` |
| `bit` | `bool` |
| `date`, `datetime`, `datetime2`, `smalldatetime`, `datetimeoffset`, `time` | `DateTime` |
| everything else | `string` |

Two of those are lossy and are **reported to the user rather than hidden**, in the same place the
row counts are reported:

- `bigint` narrows to `int`. A key above `int.MaxValue` will not round-trip.
- `uniqueidentifier` becomes `string`, which is faithful for display and wrong for anything that
  wanted a `Guid`.

The generator has five types; this is where that constraint becomes visible, so it is said out loud
rather than discovered later in a generated app.

## Part 2 — foreign keys reach the model

`IDataSourceReader.Read` returns `IReadOnlyList<TabularTable>` and there was nowhere to put a
relationship. Three ways were considered:

- carry them on `TabularTable` — chosen;
- a second `IRelationalSourceReader` interface that discovery type-tests for, which creates a
  parallel path every later reader has to choose between, for one member's worth of information;
- name the columns `<Target>Id` and let `ForeignKeys.TargetsOf`'s existing convention find them,
  which throws away exact information in favour of re-guessing it, and breaks on the `CustID` and
  `fk_customer` spellings real schemas use.

```csharp
public sealed record TabularForeignKey(string Column, string TargetTable, string TargetColumn);
```

as `List<TabularForeignKey> ForeignKeys { get; }` on `TabularTable`, empty for every other reader.
Read from `sys.foreign_keys` joined to `sys.foreign_key_columns`.

`DataModelBuilder` projects each into a `CandidateRelationship`, and **drops any whose target table
was not among the ones picked.** A relationship to a class that will not exist does not compile —
the same rule `DomainModelMapper.AddNavigationProperties` already applies when a target did not
survive shaping.

This is the part that makes the feature worth building. `RelationshipMiner` infers relationships
from Wikipedia prose and column naming; here they are read from the constraint that enforces them.

## Part 3 — choosing the tables

### On the Data stage

When the chosen kind is `sql`, the stage asks for a connection string rather than a file, and gains
a second step in place of the file summary `DataPeek` gives today:

```
Data — a file to read, or discover from the web

  Northwind on .\SQLEXPRESS — 13 tables

  [x] dbo.Customers        11 cols     91 rows
  [x] dbo.Orders           14 cols    830 rows
  [x] dbo.Order Details     5 cols   2155 rows
  [ ] dbo.Employees        18 cols      9 rows
  [ ] dbo.__EFMigrations…   2 cols      1 row

  4 of 13 selected                          [ Read these ]
```

Nothing is read until *Read these*. Ticking sets `spec.SqlTables`.

### On the command line

```
build app: order tracking from sql "Server=.;Database=Northwind;Trusted_Connection=True" tables Customers,Orders
```

`tables A,B,C` parses after the connection string, comma-joined, quotable for names with spaces —
the same shape `transactions`/`masters` already use, so there is one convention for a list of names
rather than two. Naming no tables reads them all, which is the only sensible meaning for a piped run
with nobody to ask.

`InlineDataSource` currently treats everything after the format as the location. `tables` must be
stripped **before** the location is taken, for exactly the reason
`InlineCustomization.Split` runs before `InlineDataSource.Parse` in `BuildAppCommand.Handle`.

## Part 4 — the password

`BuildCommandEcho.cs:33` writes:

```csharp
line += $" from {source.Kind.ToString().ToLowerInvariant()} \"{source.Location}\"";
```

For SQL the location is the connection string, so a connection string carrying
`Password=hunter2` would be printed onto the Review page, into the console when the command
finishes, and into anything the user pastes that replay line into.

Redaction happens in one place — a `ConnectionSecret.Redact` used by the echo and by any other
surface that shows a location — rewriting the value of `Password` and `pwd` to `***` while leaving
the rest intact, so the line stays a usable template. The connection string is never written to
`.codegen/`.

Refusing an inline password outright and demanding `Trusted_Connection` was considered and
rejected: SQL auth is ordinary in the environments this tool is used in, and a refusal would send
people to a worse workaround.

## Testing

CI has no SQL Server, and pretending otherwise with a mock that proves the query text matches a
string would be a test that passes while proving nothing.

So the ADO.NET calls sit behind `ISqlSchemaSource`, with one thin adapter over `SqlClient`.
Everything of substance sits above that seam and is tested against a fake:

- `SqlTypeMap` — every row of the table above, both lossy cases asserted to be *reported*.
- The FK projection — including a foreign key whose target was not picked being dropped.
- `tables A,B,C` parsing, including a quoted name with a space, and interaction with a location
  that follows.
- `ConnectionSecret.Redact` — `Password`, `pwd`, casing, a password containing `;`, and a
  connection string with no password left byte-identical.
- The Data stage's SQL branch under bUnit: the picker lists what the catalog returned, ticking sets
  `SqlTables`, nothing is read before *Read these*, and a connect failure leaves the stage clickable.

**The adapter itself is covered only by an opt-in integration test**, skipped unless a connection
string is present in an environment variable. This is stated rather than papered over: the query
text against a real server is verified by a test Rohit can run locally and CI cannot.

As with every branch here, the page gets looked at in a browser before it is called done. Four
consecutive rounds have shipped a defect that compiled clean, passed its tests, and was visible
only in a running page.

## Risks

- **A new package.** `Microsoft.Data.SqlClient` is the first dependency added here in a while, and
  it is a large one with native components. `ExcelReader` was hand-written specifically to avoid a
  package; that option does not exist for a network protocol.
- **Composite foreign keys are out of scope.** A two-column key produces two
  `TabularForeignKey` rows, and the model has no way to express the pair. First pass: detect them
  and say they were skipped, rather than emitting a relationship that is half right.
- **All rows, no cap.** Chosen deliberately, mitigated by showing counts before selection. A user
  who ticks a 40M-row table will exhaust memory. If that turns out to bite, the fix is a warning at
  selection time, not a silent cap.
- **`sys.dm_db_partition_stats` needs `VIEW DATABASE STATE`.** A low-privilege login may be able to
  read tables but not that view. Fall back to showing no row count rather than failing the whole
  picker.

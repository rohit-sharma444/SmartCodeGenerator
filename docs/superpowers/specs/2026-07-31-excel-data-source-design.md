# `build app` from an Excel workbook — design

**Date:** 2026-07-31
**Status:** approved, not yet implemented
**Follows:** [`2026-07-30-build-app-data-source-design.md`](2026-07-30-build-app-data-source-design.md)

## Problem

The data-source branch shipped CSV, TXT and JSON and deliberately left Excel out, promising it
as "an additive reader and nothing else". Choosing `excel` today gets an honest refusal:

> Excel is not in this build yet — it lands in a follow-on branch. Working formats: csv, txt,
> json. Save the sheet as CSV and choose csv.

That refusal is the wrong answer for the commonest case there is. Business data lives in
workbooks, and "save it as CSV first" costs the user a manual step per sheet and silently
discards what the workbook knows that a CSV cannot: which cells are numbers, which are dates,
and where one table ends and the next begins.

## Goal

`build app: <description> from excel <path>` reads an `.xlsx` workbook directly — every visible
sheet as an entity — and feeds the existing pipeline. No new dependency, no downstream change.

## Scope

**This branch:** the `.xlsx` reader, multi-sheet support, Excel's own type information (shared
strings, booleans, date serials), the wiring in `DataSourceReaders`, and the docs that advertise
which formats work.

**Deliberately not this branch:**

- **Legacy binary `.xls`.** A different format entirely (OLE2 compound file, BIFF record
  stream), not a variant of `.xlsx`. It is detected and refused with instructions, because the
  `xls` alias already routes to `DataSourceKind.Excel` and a user who types it deserves a
  straight answer. A BIFF parser is its own project and every `.xls` can be resaved in seconds.
- **SQL Server.** Still the one unimplemented kind after this branch.
- **General number formats.** See [Numbers](#numbers-are-emitted-as-stored).

## The seam is already the right shape

```
IDataSourceReader :  DataSourceSpec  ->  TabularTable[]
```

Nothing upstream or downstream changes:

- `InlineDataSource.Parse` already parses `from excel|xlsx|xls|spreadsheet <path>` — the aliases
  were written with the reader in mind.
- `DataSourceInterview` already offers `excel` on its menu and already prompts for a path.
- `BuildAppCommand.CombineReports`, `DataModelBuilder.Build` and `TabularImport.ToImportedData`
  all take `IReadOnlyList<TabularTable>`, and `JsonTableReader` already returns several. Multiple
  sheets therefore need no new plumbing at all.

The only wiring change is one `case` in `DataSourceReaders.TryCreate`.

## Components

Three files in `DataIngestion/Schema/Readers/`, split by concern rather than one long one:

| File | Responsibility | Depends on |
|---|---|---|
| `ExcelReader.cs` | `IDataSourceReader`. Opens the zip, sniffs for legacy `.xls`, iterates sheets, delegates each record set to `CsvReader.ToTable`. | the other two |
| `ExcelWorkbook.cs` | The OPC plumbing. `workbook.xml` + `_rels` → sheets (name, part path, hidden); `sharedStrings.xml` → string table; `styles.xml` → date style ids and zero-padded style formats; the `date1904` flag. | `ZipArchive`, `XDocument` |
| `ExcelSheetReader.cs` | One worksheet XML → `IReadOnlyList<string?[]>` records. Owns every cell rule below. | `ExcelWorkbook` |

`CsvReader.ToTable` is reused rather than reimplemented. It is already `public static` so
`DelimitedTextReader` could share it; Excel is its third caller. That is what keeps column
naming, header squaring and `ColumnTypeInference` identical across all four formats by
construction rather than by discipline.

Parsing uses `System.IO.Compression.ZipArchive` and `System.Xml.Linq.XDocument` — both in the
BCL. This follows `CsvReader`'s own stated reason for being hand-written: "one file, no new
dependency, and every rule visible where it is tested."

## Cell rules

| Cell | Emitted string |
|---|---|
| `t="s"` | the shared string, concatenating `<r><t>` runs, honouring `xml:space="preserve"` |
| `t="inlineStr"` | the `<is>` text |
| `t="str"` (formula string result) | the `<v>` |
| `t="b"` | `"true"` / `"false"` |
| `t="e"` (`#REF!`, `#N/A`, `#DIV/0!`) | `null` |
| numeric, date-formatted style | `"yyyy-MM-dd"`, or `"yyyy-MM-dd HH:mm:ss"` when there is a time part |
| numeric, all-zeros format code (`00000`) | the number padded to that width |
| numeric, any other format | the raw `<v>`, invariant, exactly as stored |
| absent cell, or a cell with no `<v>` | `null` |

Booleans are spelled as words because `ColumnTypeInference.BooleanWords` matches `true`/`false`
and deliberately does not match a bare `0`/`1` — emitting the raw `0` would type the column
`int`.

An error cell is not a value. `null` is the honest reading and it is what an empty CSV cell
already means, so `ColumnTypeInference` skips it rather than being dragged to `string` by a
`#N/A` in row 900.

A formula cell is read from its cached `<v>`, which every writer that has calculated the sheet
stores. Nothing evaluates formulas; a workbook saved without cached values yields `null` there.

### Dates must be converted

Excel stores `2026-03-15` as the number `46096`. Left raw, every date column in every workbook
types as `int` and the user's dates become five-digit numbers — the silent corruption
`ColumnTypeInference`'s doc comment exists to prevent. Converting to `yyyy-MM-dd` hits its
`DateFormats` list, so the column types `DateTime` and round-trips.

Detection is by number format, which is the only place Excel records the distinction: the cell's
`s` attribute indexes `cellXfs`, whose `numFmtId` is either a builtin date id (14–22, 45–47) or
a custom `numFmt` whose code contains date or time tokens outside quoted literals.

Serial conversion: serial 1 is 1900-01-01, with Excel's phantom `1900-02-29`, so serials of 60
and above shift back a day. When `workbookPr/@date1904` is set, serial 0 is 1904-01-01 instead.
The fractional part is the time of day.

### Numbers are emitted as stored

A cell holding `1234.5` displayed as `£1,234.50` is emitted `1234.5`. Applying the display
format would inject a thousands separator that `ColumnTypeInference` refuses to parse by design
— `"1,2"` reads as twelve — turning a clean `decimal` column into `string`. The stored value is
what the user's file actually holds, so it is what gets imported.

Zero-padding is the one exception, because there the format *is* the meaning. A postcode column
of numeric cells formatted `00000` displays `01234`; emitted as `1234` the padding is gone and
the user's own data has changed. Emitted padded, it trips `ColumnTypeInference.IsZeroPadded` and
correctly stays `string` — the same rule that already protects a CSV's `007`. The check is
narrow on purpose: the format code must be nothing but `0`s.

## Sheet rules

- **One visible sheet is one table is one entity.** The sheet name becomes the entity name via
  `NameNormalizer.ToUserTypedEntityName`, following `JsonTableReader`.
- **Hidden and `veryHidden` sheets are skipped.** They hold scratch working and lookup lists, not
  entities. Generating a class per hidden helper sheet would be surprising in a way that omitting
  one is not.
- **Entirely-empty rows are dropped**, as `CsvReader.ParseFields` already drops blank lines. A
  workbook whose used range overruns its data is normal.
- **A sheet with no rows, or a header and no data rows, is skipped rather than fatal.** A blank
  `Sheet3` is normal, and one would otherwise abort a workbook that is otherwise fine.
- **If no sheet survives, that is an error** naming each sheet and why, so the user knows which
  file they pointed at and what was wrong with it:

  ```
  orders.xlsx has no sheet with data: Sheet1 has a header but no rows,
  Sheet2 is empty, Sheet3 is hidden.
  ```

Sparse cells are addressed, not counted: `<c r="C7">` lands in column C. A row's cells are
placed by decoding the A1 reference, because Excel omits empty cells entirely and appending in
document order would shift every value after the first gap into the wrong column.

## Error handling

Every failure is a `FileNotFoundException` for an absent path or an `InvalidDataException` whose
message says what to do, per the `IDataSourceReader` contract.

| Situation | Message |
|---|---|
| No file at the path | `No file at <path>.` (`FileNotFoundException`, matching `CsvReader`) |
| OLE2 magic `D0 CF 11 E0 A1 B1 1A E1` | `<name> is the old binary .xls format, not .xlsx. Open it in Excel and use Save As > Excel Workbook (*.xlsx), then point build app at the .xlsx.` |
| Not a zip at all | `<name> is not a readable .xlsx file. If it was renamed from another format, open it in Excel and save it as .xlsx.` |
| A zip with no `xl/workbook.xml` | `<name> is a zip file but not an Excel workbook.` |
| No sheet with data | as above, naming each sheet |

The `.xls` sniff runs before `ZipFile.OpenRead`, because otherwise the user gets
"End of Central Directory record could not be found", which tells them nothing.

## Testing

**No binary fixtures.** A committed `.xlsx` is unreviewable bytes in the repo, and odd cases —
a sparse row, a multi-run shared string, a `date1904` workbook — cannot be constructed by hand
in Excel reliably. Instead a test helper writes a minimal OPC package with `ZipArchive` from XML
written inline in the test, so every byte a test depends on is visible in that test.

`DataIngestion.Tests/Schema/Readers/ExcelReaderTests.cs` covers, red before green:

- a sheet becomes a table; the sheet name becomes the entity name
- shared strings, including a value split across `<r><t>` runs and one with `xml:space="preserve"`
- inline strings, and a formula's cached string
- **sparse cells**: `<c r="C1">` after `<c r="A1">` leaves column B null
- booleans emit `true`/`false` and the column types `bool`
- an error cell emits `null` and does not drag the column to `string`
- a numeric cell keeps its exact stored text; a `decimal` column stays `decimal`
- a date-styled serial types `DateTime` — builtin id, custom `numFmt`, the serial-60 boundary,
  a time component, and a `date1904` workbook
- an `00000`-formatted number keeps its padding and the column types `string`
- a hidden sheet is skipped; two visible sheets produce two tables
- an empty and a header-only sheet are skipped; a workbook of only those raises the naming error
- each error-path message: missing file, `.xls` magic, non-zip, zip without a workbook part

`DataSourceReadersTests` asserts `excel` now yields a reader and that `NotSupportedMessage`
covers only `Sql`.

`CodeGenerator.Tests/App/BuildAppDataSourceTests.cs` gains an end-to-end case: drive
`build app: <description> from excel <path>` through the CLI and **compile** the generated
project with its seed data, mirroring the CSV case from PR #73. Generated code is verified by
compiling it, never by substring assertions.

## Wiring and docs

| Location | Change |
|---|---|
| `DataSourceReaders.TryCreate` | add `case DataSourceKind.Excel: reader = new ExcelReader(); return true;` |
| `DataSourceReaders.NotSupportedMessage` | drop the Excel branch; working formats become `csv, txt, json, excel` |
| `DataSourceKind` doc comment | stops claiming Excel has no reader; Sql only |
| `CommandDictionary.cs` (`build app:` entry) | `Formats: csv, txt, json, excel (sql is not in this build yet)` |
| `docs/commands.md` | the same row |
| `BuildAppCommand.cs` usage line | `(also txt, json, excel)` |
| `README.md`, `DataIngestion/README.md`, `DataIngestion.Tests/README.md` | format lists and the reader table |

## Verification

CI builds `SmartCodeGenerator.slnx` in Release with `-warnaserror` and runs one of the five test
projects, so all five are run locally with that same command before the PR. `CodeGenerator` is
rebuilt before any CLI check, because `dotnet test` leaves its `bin` stale and a working fix can
look dead. The CLI is driven with redirected stdin, which takes the inline `from excel` form.

# Bug fixes G: data import and app targets (bug hunt #2)

Found 2026-11-20 by a read-only audit of the JSON, Excel and CSV readers and of generated
webapi, worker, console, classlib (and blazor) apps, on main b5cae20. All ten were reproduced
with the CLI and generated apps (PROVEN). SQL Server was not tested. G2 is a regression of fix
A8 (PR #174).

## G1. Entity names that some targets cannot compile (PROVEN, high)

**Repro:** `routes.csv` (Name,Stops) with `approve webapi`; or JSON child arrays named consoles,
hosts, programs, workers, routes; or a root table `Api` from api.json.

**Failure:** `Console`: CS0104 (System.Console) in webapi/worker/classlib, CS0118 in console.
`Host`: CS0104 (Microsoft.Extensions.Hosting.Host) in webapi/worker. `Route`: CS0104
(Microsoft.AspNetCore.Routing.Route) in webapi/blazor. `Program`: CS9338/CS0051 in console/worker.
`Worker`: CS0310 in worker. `Assert`: CS0104 in every test project. `Api`: CS0118 in webapi,
because `Shop.Api` is a namespace.

**Cause:** `AppScaffolding/DomainModelMapper.cs:22-56` `ReservedNames` lacks these; the comment at
`AppScaffolding/Screens/ScreenPlan.cs:66` says only the outermost namespace segment is in scope,
which is false - every enclosing segment is.

**Fix:** reserve every segment of the root namespace, and the types the targets' implicit usings
and templates bring in: Console, Host, Route, Program, Worker, Results, WebApplication, and
Assert/Fact for the test projects. A reserved name gets the usual `Item` suffix.

**Tests:** map entities Console, Host, Route, Program, Worker, Api; Roslyn-compile each target and
its test project: no errors.

## G2. An imported `CreatedOn` column stops every new record (PROVEN, high, regression of A8)

**Repro:** a CSV `Id,...,CreatedOn` with a date in every row.

**Failure:** the model gets `[Required] DateTime CreatedOn`. Console Add never asks for it
(`ConsoleMenuTemplate.cs:223` hides it), so every add fails "CreatedOn is required. Not added.";
webapi `POST` returns 400 "CreatedOn is required." although JsonStore stamps it after validation.
Blazor/WPF/WinForms editors also hide it. A unique rule is offered on CreatedOn too.

**Cause:** `DomainModelMapper.cs` ~730 adds `required` for a NOT NULL date without excluding the
audit names; `DataModelBuilder.DateFacts` (~414) sets IsNullable=false.

**Fix:** never add `required` or propose `unique` for CreatedOn/ModifiedOn (the stamped fields
the validator template already lists).

**Tests:** CSV with a filled CreatedOn column -> no `required`/`unique` on it; a generated
console and webapi Create succeeds.

## G3. Excel decimals come in with binary noise (PROVEN, high)

**Repro:** 2.3, 1.15, 19.99, 2.675, `=0.1*3` saved by Excel (it stores `<v>2.2999999999999998</v>`).
Seed: `2.2999999999999998`, `1.1499999999999999`, `0.30000000000000004`.

**Cause:** `DataIngestion/Schema/Readers/ExcelSheetReader.cs:163` passes the stored text through;
`TryPlainDecimal` keeps it exactly.

**Fix:** parse as double and round to 15 significant digits (`ToString("G15")`, as Excel shows
it), then PlainNumberText.

**Tests:** a cell `<v>2.2999999999999998</v>` reads "2.3"; `0.30000000000000004` reads "0.3";
an integer and a 15-digit value are unchanged.

## G4. Excel: a title row above the header silently drops every other column (PROVEN, high)

**Repro:** row 1 `Sales Report 2024`, row 3 `Region,Units,Revenue`, three data rows. Result: an
entity with one field `SalesReport2024`; seed holds "Region", "North"...; Units and Revenue are
gone with no note. Any cell right of the header width is lost the same way.

**Cause:** `ExcelReader.cs:72` calls `CsvReader.ToTable` directly, skipping the width checks and
notes of `FromText`; `CsvReader.cs:182` (`new string?[header.Length]`) truncates each row.

**Fix:** apply the same longer/shorter-row checks to sheets. Take as header the first row whose
width matches the rows below it (skipping title rows, with a note), or refuse the sheet with a
clear message. Never drop a non-empty cell silently.

**Tests:** the title-row workbook -> Region, Units, Revenue with 3 rows and a note about the
skipped rows; a row wider than the header -> a note.

## G5. Seeded rows fail their own unique rule when text differs but the value is equal (PROVEN, medium)

**Repro:** `Code,Price` with Price 1.5, 1.50, 2; accept the offered "no two rows share the same
Price". `PUT` of row 1 unchanged -> 400 "Another Dup already has this Price."

**Cause:** `DataIngestion/Schema/ColumnScan.cs:58` counts distinct values as text (ordinal); the
generated lookup compares typed values (`1.5m == 1.50m`). Same for "+5"/"5" and dates.

**Fix:** in ColumnScan, compare the parsed value of the inferred type for int, decimal and
DateTime columns.

**Tests:** the 1.5/1.50 CSV gets no unique proposal; 1.5/2.5 still does.

## G6. Web API PUT ignores the id in the route (PROVEN, medium)

**Repro:** `PUT /order/2` with no "id" in the body -> 404 although order 2 exists.
`PUT /order/2` with `{"id":1,...}` -> 204 and order 1 is overwritten. POST's Location is `/order`
with no id.

**Cause:** `AppScaffolding/WebApiProgramTemplate.cs` ~71: `id` is never used; ~65:
`Results.Created($"/{route}", ...)`.

**Fix:** a body id that is missing (0) takes the route id; a body id that differs returns 400.
Created uses `$"/{route}/{id}"`.

**Tests:** compile and run the template's API (or call the handler) for both PUT cases; Location
carries the new id.

## G7. Excel elapsed-time cells become 1900 dates (PROVEN, medium)

**Repro:** a `[h]:mm` column with 0.5, 1.5, 2.25 -> seed "12:00:00", "1900-01-01 12:00:00",
"1900-01-02 06:00:00" (mixed text).

**Cause:** `ExcelNumberFormats.cs:158` treats `[h]` as a date token; `ExcelSerialDate.cs:29`
turns only values below 1 into a time.

**Fix:** a format with elapsed tokens (`[h]`, `[mm]`, `[ss]`) is a duration: write every value as
total hours:mm:ss, never as a date.

**Tests:** `[h]:mm` with 1.5 -> "36:00:00"; 0.5 -> "12:00:00".

## G8. A semicolon "CSV" builds a nonsense model and loses rows (PROVEN, medium)

**Repro:** `Name;Price;Qty` / `Pen;1,50;3` / `Ink;2;4` / `Pad;3,25;5` with `from csv`: one table
with one column, 2 of 3 rows dropped. Excel writes such files in de/fr/nl/it/es locales.

**Cause:** `CsvReader.cs:27` always uses ','. Only the `txt` reader sniffs the delimiter.

**Fix:** when the header has no comma but has `;` or a tab, use
`DelimitedTextReader.SniffDelimiter` and say so in a note. (Decimal commas inside a `;` file stay
text unless the inference already handles them; do not invent a culture.)

**Tests:** the semicolon file with `from csv` -> 3 columns, 3 rows, a note naming `;`.

## G9. An unresolvable link is seeded as FK 0, which the app's validator refuses (PROVEN, low-medium)

**Repro:** Customers (ids 101-103) and Orders with one CustomerId 999. "1 reference(s) could not
be linked ... left unset." Order 3 is seeded `customerId: 0`; `PUT` of it unchanged -> 400
"Customer is required".

**Cause:** TabularImport clears the dangling reference, and the FK is a non-nullable `int`.

**Fix:** when the import cleared a reference in a column, make that FK `int?` (not required), so
the seeded row is valid and the user can link it later. Say so in the existing note.

**Tests:** every seeded row passes the generated validator; the FK property is `int?`.

## G10. Two small import faults (PROVEN, low)

- A JSON file with several root collections (`{"customers":[...],"orders":[...]}`) makes a fake
  root entity `Db` with an empty Name, and every Customer and Order gets a required `DbId`.
  Cause: `JsonTableReader` `Wrapped` accepts one property only. Fix: when every root property is
  an array of objects, read each as its own table.
- Headers `%` and `#` are dropped together with their data (only `why` shows it), while a blank
  header becomes `Column9`. Fix: give a header that cannot become a name the same `Column<n>`
  fallback.

**Tests:** the two-collection JSON -> tables Customer and Order, no Db; a CSV with `%` and `#`
headers keeps both columns as Column<n>.

## Order of work

G2 (regression) → G1 → G6 → G4 → G3 → G5 → G9 → G7 → G8 → G10. After all: DataIngestion and
CodeGenerator suites, a live CSV/Excel/JSON run per repro, and the app-check harness (26/26).

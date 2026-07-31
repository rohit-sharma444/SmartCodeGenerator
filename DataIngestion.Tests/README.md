# DataIngestion.Tests

Test suite for [`DataIngestion`](../DataIngestion/README.md). Uses **xUnit**
(`net10.0`), matching
[`DomainDiscovery.Tests`](../DomainDiscovery.Tests/README.md).

Tests run **offline and deterministically**. There is nothing to stub: this path
makes no network call at all, and the readers are driven either from in-memory
strings or from a temporary file the test writes and deletes. The interview and
the normalization prompt are driven through `StringReader`/`StringWriter` — the
same pattern `BuildAppTargetSelectionTests` uses, and the reason both are written
over `TextReader`/`TextWriter` rather than the console.

## Organization

| Path | Focus |
| --- | --- |
| `Schema/ColumnTypeInferenceTests.cs` | Value-set → expected-type table, including `0/1 → int`, the oversized-integer demotion, the empty-column name fallback, and the sample cap. |
| `Schema/Readers/CsvReaderTests.cs` | Quoted fields, embedded delimiters and newlines, doubled quotes, ragged rows, BOM, CRLF/LF, header-only rejection. |
| `Schema/Readers/DelimitedTextReaderTests.cs` | Delimiter sniffing across tab / pipe / semicolon / comma; prose and inconsistent field counts sniff nothing; a non-tabular file is rejected with actionable text. |
| `Schema/Readers/JsonTableReaderTests.cs` | Array-of-objects to one table; nested array → child table with FK; nested object flattened; scalar array → value table; key union; malformed JSON named. |
| `Schema/Readers/ExcelReaderTests.cs` | A sheet becomes a table, every visible sheet its own; hidden, empty and header-only sheets skipped and named when nothing survives; missing file, legacy `.xls`, non-zip and non-workbook messages. |
| `Schema/Readers/ExcelSheetReaderTests.cs` | Cell addressing including sparse rows, shared and inline strings, formula results, booleans, error cells, empty rows, and dates and padded codes reaching the cell. |
| `Schema/Readers/ExcelWorkbookTests.cs` | Sheet list and part paths, hidden state, the shared string table including split runs and phonetic runs, and the `date1904` flag. |
| `Schema/Readers/ExcelSerialDateTests.cs` | 1900 and 1904 epochs, the phantom 1900-02-29, times of day, rounding, and out-of-range serials. |
| `Schema/Readers/ExcelNumberFormatTests.cs` | Which builtin ids and custom codes are dates, the codes that must NOT be (quoted units, escapes, colours, locales), and the narrow all-zeros padding rule. |
| `Schema/Readers/DataSourceReadersTests.cs` | Which formats have readers, the alias spellings a user would type, and the not-supported message naming the alternatives. |
| `DataModelBuilderTests.cs` | The Id drop, the `Product.Product` rename and its second fallback, plural → singular, FK-to-relationship pairing, unusable column names, the default `Name` field. |
| `TabularImportTests.cs` | Row keying by **final** field name, the `MaxSeedRows` cap and its stated note, and the invariant that every imported key is a field the model declares. |
| `InlineDataSourceTests.cs` | The scriptable `from <format> <location> [normalize]` tail, including a description that contains the word "from", quoted and spaced paths, and `from csv` with no location. |
| `DataSourceInterviewTests.cs` | The three prompts; re-asking on an unknown format or a missing file; cancel being distinct from "no data"; closed stdin never busy-looping. |
| `Normalization/RepeatingGroupTests.cs` | Finding 1, positive and negative, plus its evidence. Also holds the shared `Table(...)` fixture helper the other normalization tests use. |
| `Normalization/NonAtomicValueTests.cs` | Finding 2, including the recurrence guard that keeps prose out and the minority-separator guard. |
| `Normalization/FunctionalDependencyTests.cs` | Finding 3: the two-dependent case, the shared-prefix second signal, the near-unique-column exclusion, one contradicting row, and the too-few-rows note. |
| `Normalization/DerivedColumnTests.cs` | Finding 4: product and sum, one row that does not add up, the all-zero coincidence guard. |
| `Normalization/NormalizationPlanTests.cs` | Applying each finding kind, the FK rewrite, `k` changing nothing, only the chosen subset applying, and a child name suffixed rather than colliding. |
| `Normalization/NormalizationDecisionTests.cs` | The audit trail: a reason constant per finding kind, evidence naming the columns and the file, and `k` recording nothing. |
| `Normalization/NormalizationPromptTests.cs` | `n` / `n <ids>` / `k` / `d <n>`, bad input re-asking, and no prompt at all when there is nothing to decide. |

## What is NOT tested here

The generated app is verified by **compiling** it, which needs both this project
and the scaffolder, so those tests live in
[`CodeGenerator.Tests`](../CodeGenerator.Tests/README.md):

- `App/SeedFileWriterTests.cs` — imported rows projected through the final model.
- `App/SeedDataFromFileTests.cs` — the generated seeder's second mode and the
  csproj content item.
- `App/GeneratedProjectBuildTests.cs` — `ProjectGeneratedFromACsv_*`: a CSV becomes
  a project that **builds**, with its data present as valid JSON and copied to the
  build output. Substring assertions over generated source cannot see CS0023,
  CS0542 or a literal that broke out of its string; compiling can.
- `App/BuildAppDataSourceTests.cs` — the CLI front step: the widened `build app`
  trigger, the tail parse, app naming, and combining findings across tables.

## Running

```bash
# from the repository root

# run this suite
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj

# a single class
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj \
  --filter "FullyQualifiedName~CsvReaderTests"

# every normalization test
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj \
  --filter "FullyQualifiedName~Normalization"
```

Or run the whole solution the way CI does — which is **not** the same as building
one project in Debug, since that hides analyzer errors and runs one suite of five:

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test  SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

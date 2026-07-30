# `build app` from existing data — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `build app` a data-source interview so a user who already has data gets their domain model from that data's schema — checked for normalization problems — with the rows imported as the generated app's seed data.

**Architecture:** A new `DataIngestion` project puts every reader behind one seam, `IDataSourceReader : DataSourceSpec -> TabularTable[]`. `TabularTable` has exactly three consumers — the normalization analyser, `DataModelBuilder` (which produces a `CandidateDomainModel`, the same type Wikipedia discovery produces), and the seed-data importer. `BuildAppCommand` gains a front step that picks between the interview and today's unchanged Wikipedia path; everything downstream of the review loop is untouched.

**Tech Stack:** C# / .NET 10, xunit (`DataIngestion.Tests`), MSTest (`CodeGenerator.Tests`), `System.Text.Json`. No new NuGet packages in this branch.

**Spec:** `docs/superpowers/specs/2026-07-30-build-app-data-source-design.md`

## Global Constraints

- Repo root is `D:\SmartCodeGenerator-Repository` — the *parent* of the `SmartCodeGenerator` folder that holds the `.slnx`. All paths below are relative to the repo root.
- Branch is `feat/build-app-data-source`, already created, spec already committed.
- `Directory.Build.props` sets `net10.0`, `ImplicitUsings=enable`, `Nullable=enable`, `LangVersion=latest` for every project. New `.csproj` files declare only what is specific to them.
- **No new NuGet packages.** Excel (`DocumentFormat.OpenXml`) and SQL Server (`Microsoft.Data.SqlClient`) are follow-on branches.
- Producible field types are exactly the five in `ModelEditor.SupportedTypes`: `string`, `int`, `decimal`, `bool`, `DateTime`. Nothing may produce any other type — `ModelEditor.AddField` rejects them and the review loop would refuse to edit them.
- Reasons written to `CandidateDomainModel.Decisions` MUST be constants on `DecisionReasons`, never ad-hoc literals. That file states the rule itself.
- Every threshold is a named `public const` on the class that uses it, so tests reference the constant rather than a copy of its value.
- Redirected stdin never prompts. `BuildAppCommand.ResolveTarget` sets this precedent and existing piped scripts and E2E tests depend on it.
- CI is the authority, and it is **not** the local build: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror` then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build`. A Debug build of one project hides analyzer errors and runs one of three test projects. Run the CI pair before claiming a task green.
- Generated C# must be verified by **compiling** it, not by substring assertions. `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs` is the existing pattern.
- Every test is written red first and observed failing for the *stated reason* before implementation. An inverted `Assert.DoesNotContain`, or an assertion on a name the fixture itself declares, passes green while proving nothing.

---

## File Structure

**New project `DataIngestion`** (library, references `DomainDiscovery` for `CandidateDomainModel` / `NameNormalizer` / `DecisionRecord`):

| File | Responsibility |
|---|---|
| `DataIngestion/DataIngestion.csproj` | project + reference to `DomainDiscovery` |
| `DataIngestion/DataSourceKind.cs` | `enum { Sql, Excel, Txt, Json, Csv }` |
| `DataIngestion/DataSourceSpec.cs` | `(Kind, Location, SqlDatabase?, SqlTables?)` |
| `DataIngestion/InlineDataSource.cs` | parses the `from <format> <loc> [normalize]` tail off a command |
| `DataIngestion/DataSourceInterview.cs` | the prompts, over `TextReader`/`TextWriter` |
| `DataIngestion/Schema/TabularColumn.cs` | one column: name + inferred type |
| `DataIngestion/Schema/TabularTable.cs` | name, columns, rows of raw strings |
| `DataIngestion/Schema/IDataSourceReader.cs` | the seam |
| `DataIngestion/Schema/ColumnTypeInference.cs` | values -> one of the five types |
| `DataIngestion/Schema/Readers/CsvReader.cs` | RFC4180-ish CSV |
| `DataIngestion/Schema/Readers/DelimitedTextReader.cs` | TXT with delimiter sniffing |
| `DataIngestion/Schema/Readers/JsonTableReader.cs` | array-of-objects + nested child tables |
| `DataIngestion/Schema/Readers/DataSourceReaders.cs` | kind -> reader, and the not-yet-supported message |
| `DataIngestion/Normalization/NormalizationFinding.cs` | one finding + its evidence |
| `DataIngestion/Normalization/NormalizationReport.cs` | findings + notes |
| `DataIngestion/Normalization/NormalizationAnalyzer.cs` | the four detections |
| `DataIngestion/Normalization/NormalizationPlan.cs` | applies chosen findings to the tables |
| `DataIngestion/Normalization/NormalizationPrompt.cs` | the `n`/`k`/`d` loop, over `TextReader`/`TextWriter` |
| `DataIngestion/DataModelBuilder.cs` | `TabularTable[]` -> `CandidateDomainModel` |
| `DataIngestion/ImportedData.cs` | entity name -> rows, carried to the scaffolder |

**New test project `DataIngestion.Tests`** (xunit, mirroring `DomainDiscovery.Tests`), with fixtures under `DataIngestion.Tests/Fixtures/`.

**Modified:**

| File | Change |
|---|---|
| `DomainDiscovery/Model/CandidateDomainModel.cs` | `Provenance` gains `Data` |
| `DomainDiscovery/Model/DecisionRecord.cs` | `DecisionStage` gains `Normalization` |
| `DomainDiscovery/Model/DecisionReasons.cs` | four normalization reason constants |
| `AppScaffolding/DomainModelMapper.cs` | expose the entity-name -> class-name map |
| `AppScaffolding/SeedDataTemplate.cs` | second mode: load `Data/Seed/<Class>.json` when present |
| `AppScaffolding/CsprojEditing.cs` | inject a `Content` item for the seed folder |
| `AppScaffolding/AppScaffolder.cs` | `Scaffold(..., ImportedData?)`; write the seed files |
| `CodeGenerator/App/BuildAppCommand.cs` | the front step; `IsBuildApp` widened |
| `CodeGenerationEngine/Core/CommandDictionary.cs` | help entries for the new forms |
| `docs/commands.md` | regenerated from the catalog |
| `SmartCodeGenerator/SmartCodeGenerator.slnx` | add both new projects |

---

## Task 1: `DataIngestion` project + the tabular shape + value-based type inference

**Files:**
- Create: `DataIngestion/DataIngestion.csproj`, `DataIngestion/Schema/TabularColumn.cs`, `DataIngestion/Schema/TabularTable.cs`, `DataIngestion/Schema/ColumnTypeInference.cs`
- Create: `DataIngestion.Tests/DataIngestion.Tests.csproj`, `DataIngestion.Tests/Schema/ColumnTypeInferenceTests.cs`
- Modify: `SmartCodeGenerator/SmartCodeGenerator.slnx`

**Interfaces:**
- Consumes: `DomainDiscovery.Enrichment.TypeInference.Infer(string fieldName)` — the existing name-based heuristic, used only as a fallback.
- Produces:
  - `TabularColumn { string Name; string Type; }` (mutable `Type`)
  - `TabularTable { string Name; List<TabularColumn> Columns; List<string?[]> Rows; string? SourceLabel; }` with `string? Value(int row, string column)` and `IEnumerable<string?> ColumnValues(string column)`
  - `ColumnTypeInference.Infer(IEnumerable<string?> values, string columnName) -> string`
  - `ColumnTypeInference.TypeSampleSize` (`const int` = 200)

- [ ] **Step 1: Create the two project files and register them**

`DataIngestion/DataIngestion.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\DomainDiscovery\DomainDiscovery.csproj" />
  </ItemGroup>

</Project>
```

`DataIngestion.Tests/DataIngestion.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DataIngestion\DataIngestion.csproj" />
  </ItemGroup>

</Project>
```

In `SmartCodeGenerator/SmartCodeGenerator.slnx`, add two lines inside `<Solution>`, keeping the file's alphabetical order (so `DataIngestion` lines go directly after the `CodeGenerator` lines):

```xml
  <Project Path="../DataIngestion.Tests/DataIngestion.Tests.csproj" />
  <Project Path="../DataIngestion/DataIngestion.csproj" />
```

- [ ] **Step 2: Write the failing test**

`DataIngestion.Tests/Schema/ColumnTypeInferenceTests.cs`:

```csharp
using DataIngestion.Schema;

namespace DataIngestion.Tests.Schema;

public class ColumnTypeInferenceTests
{
    [Theory]
    [InlineData("int", "1", "2", "3")]
    [InlineData("decimal", "1.50", "2", "3.75")]
    [InlineData("bool", "true", "false", "true")]
    [InlineData("bool", "yes", "no", "y")]
    [InlineData("DateTime", "2026-01-15", "2026-02-01", "2026-03-30")]
    [InlineData("string", "ORD-1", "ORD-2", "ORD-3")]
    public void Infers_TheTypeTheValuesActuallyCarry(string expected, params string[] values)
        => Assert.Equal(expected, ColumnTypeInference.Infer(values, "Whatever"));

    /// <summary>
    /// A 0/1 column stays int. A column wrongly typed bool cannot round-trip a 2, while one
    /// wrongly typed int loses nothing — and the review loop can correct either.
    /// </summary>
    [Fact]
    public void ZeroAndOne_StayInt_RatherThanBecomingBool()
        => Assert.Equal("int", ColumnTypeInference.Infer(new[] { "0", "1", "1", "0" }, "Active"));

    [Fact]
    public void EmptyValues_AreIgnored_NotTreatedAsStrings()
        => Assert.Equal("int", ColumnTypeInference.Infer(new[] { "1", "", null, "  ", "2" }, "Qty"));

    /// <summary>With no evidence at all, fall back to the existing name-based heuristic.</summary>
    [Fact]
    public void AllEmptyColumn_FallsBackToTheNameHeuristic()
    {
        Assert.Equal("decimal", ColumnTypeInference.Infer(new string?[] { "", null }, "UnitPrice"));
        Assert.Equal("string", ColumnTypeInference.Infer(new string?[] { "", null }, "Notes"));
    }

    /// <summary>One rogue value demotes the whole column: a column is only int if every value is.</summary>
    [Fact]
    public void OneNonNumericValue_DemotesTheColumnToString()
        => Assert.Equal("string", ColumnTypeInference.Infer(new[] { "1", "2", "n/a" }, "Qty"));

    /// <summary>Beyond int.MaxValue is decimal, never a silently truncated int.</summary>
    [Fact]
    public void OversizedIntegers_BecomeDecimal()
        => Assert.Equal("decimal", ColumnTypeInference.Infer(new[] { "3000000000", "5" }, "Reference"));

    /// <summary>Only the first TypeSampleSize non-empty values are examined; the cap is a constant
    /// so this test cannot drift from the implementation.</summary>
    [Fact]
    public void OnlyTheFirstSampleOfValuesIsExamined()
    {
        var values = Enumerable.Repeat("1", ColumnTypeInference.TypeSampleSize)
            .Append("not a number")
            .ToArray();

        Assert.Equal("int", ColumnTypeInference.Infer(values, "Qty"));
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run from the repo root:

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj
```

Expected: FAIL to **compile** — `ColumnTypeInference` does not exist. That is the correct red for a first test.

- [ ] **Step 4: Write the tabular shape**

`DataIngestion/Schema/TabularColumn.cs`:

```csharp
namespace DataIngestion.Schema;

/// <summary>
/// One column of a read data source: the name as the source spells it, and the C# type its
/// values were found to carry. <see cref="Type"/> is settable because a reader produces the
/// name and <see cref="ColumnTypeInference"/> fills the type in afterwards, over the rows the
/// same reader produced.
/// </summary>
public sealed class TabularColumn
{
    public required string Name { get; init; }

    /// <summary>One of string, int, decimal, bool, DateTime — the five
    /// <c>ModelEditor.SupportedTypes</c> allows. Defaults to string so a column is never
    /// untyped.</summary>
    public string Type { get; set; } = "string";
}
```

`DataIngestion/Schema/TabularTable.cs`:

```csharp
namespace DataIngestion.Schema;

/// <summary>
/// The one shape every data-source reader produces, and the only shape the normalization
/// analyser, <see cref="DataIngestion.DataModelBuilder"/> and the seed importer consume. That
/// is what makes Excel and SQL Server cheap later: they add a reader and touch nothing else.
/// </summary>
/// <remarks>
/// Rows hold RAW STRINGS, not typed values, because CSV, TXT and JSON all arrive as text and
/// typing is an inference over that text. Conversion to the column's type happens once, at
/// seed-writing time, so no stage has to agree with any other about when a value became typed.
/// A cell is null when the source had no value there.
/// </remarks>
public sealed class TabularTable
{
    /// <summary>The table's own name — a SQL table, a spreadsheet sheet, a JSON property, or a
    /// file's base name. Becomes the entity name.</summary>
    public required string Name { get; init; }

    public List<TabularColumn> Columns { get; } = new();

    /// <summary>One entry per row, each as long as <see cref="Columns"/>.</summary>
    public List<string?[]> Rows { get; } = new();

    /// <summary>What to call the source in a message to the user ("Orders.csv"). Falls back to
    /// <see cref="Name"/>.</summary>
    public string? SourceLabel { get; init; }

    public string Label => SourceLabel ?? Name;

    public int IndexOf(string column)
        => Columns.FindIndex(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));

    /// <summary>The cell at <paramref name="row"/> in <paramref name="column"/>, or null when
    /// either is absent. Never throws: a ragged source is normal, not exceptional.</summary>
    public string? Value(int row, string column)
    {
        var index = IndexOf(column);
        if (index < 0 || row < 0 || row >= Rows.Count || index >= Rows[row].Length)
            return null;

        return Rows[row][index];
    }

    public IEnumerable<string?> ColumnValues(string column)
        => Enumerable.Range(0, Rows.Count).Select(r => Value(r, column));
}
```

- [ ] **Step 5: Write the type inference**

`DataIngestion/Schema/ColumnTypeInference.cs`:

```csharp
using System.Globalization;
using DomainDiscovery.Enrichment;

namespace DataIngestion.Schema;

/// <summary>
/// The C# type a column's VALUES carry. Real data is better evidence than a column's name, so
/// this supersedes <see cref="TypeInference"/> on the data path — but only where there is
/// evidence: an all-empty column has none, and falls back to that name heuristic.
/// </summary>
/// <remarks>
/// Only the five types <c>ModelEditor.SupportedTypes</c> allows can be produced. A column of
/// GUIDs is therefore a string and an integer past <see cref="int.MaxValue"/> a decimal, since
/// emitting Guid or long would produce a model the review loop cannot edit.
/// </remarks>
public static class ColumnTypeInference
{
    /// <summary>How many non-empty values are examined. A column's type is a property of its
    /// domain, not of its length, and scanning a million rows to learn "int" is waste.</summary>
    public const int TypeSampleSize = 200;

    /// <summary>Words that mean true or false. A bare 0/1 is deliberately NOT here: a column
    /// wrongly typed bool cannot round-trip a 2, while one wrongly typed int loses nothing.</summary>
    private static readonly HashSet<string> BooleanWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "y", "n"
    };

    public static string Infer(IEnumerable<string?> values, string columnName)
    {
        var sample = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Take(TypeSampleSize)
            .ToList();

        if (sample.Count == 0)
            return TypeInference.Infer(columnName);

        // Ordered narrowest-first. int before decimal so "3" is a count, not money; bool after
        // both so the word forms are all that can reach it; DateTime last of the non-strings,
        // because a yyyymmdd date parses as int and is caught above (visible in the review loop,
        // and fixable there with `field`).
        if (sample.All(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            return "int";

        if (sample.All(v => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
            return "decimal";

        if (sample.All(BooleanWords.Contains))
            return "bool";

        if (sample.All(v => DateTime.TryParse(
                v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            return "DateTime";

        return "string";
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj
```

Expected: PASS, 13 tests (7 theory cases + 6 facts).

- [ ] **Step 7: Verify the solution still builds the way CI builds it**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
```

Expected: succeeds with no warnings. If the new projects are missing from the `.slnx`, this step still succeeds while CI never runs the new tests — confirm both new `Project Path` lines are present before moving on.

- [ ] **Step 8: Commit**

```bash
git add DataIngestion DataIngestion.Tests SmartCodeGenerator/SmartCodeGenerator.slnx
git commit -m "feat(data): tabular source shape and value-based column typing"
```

---

## Task 2: CSV reader

**Files:**
- Create: `DataIngestion/Schema/IDataSourceReader.cs`, `DataIngestion/DataSourceKind.cs`, `DataIngestion/DataSourceSpec.cs`, `DataIngestion/Schema/Readers/CsvReader.cs`
- Create: `DataIngestion.Tests/Schema/Readers/CsvReaderTests.cs`

**Interfaces:**
- Consumes: `TabularTable`, `TabularColumn`, `ColumnTypeInference.Infer` from Task 1.
- Produces:
  - `enum DataSourceKind { Sql, Excel, Txt, Json, Csv }`
  - `sealed record DataSourceSpec(DataSourceKind Kind, string Location, string? SqlDatabase = null, IReadOnlyList<string>? SqlTables = null)`
  - `interface IDataSourceReader { IReadOnlyList<TabularTable> Read(DataSourceSpec spec); }`
  - `CsvReader : IDataSourceReader`, plus `static IReadOnlyList<string?[]> ParseFields(string text, char delimiter)` and `static TabularTable ToTable(string name, string label, IReadOnlyList<string?[]> records)` — both reused by Task 3.

- [ ] **Step 1: Write the failing test**

`DataIngestion.Tests/Schema/Readers/CsvReaderTests.cs`:

```csharp
using DataIngestion;
using DataIngestion.Schema.Readers;

namespace DataIngestion.Tests.Schema.Readers;

public class CsvReaderTests
{
    [Fact]
    public void ReadsHeaderAndRows()
    {
        var records = CsvReader.ParseFields("Id,Name\n1,Ada\n2,Grace\n", ',');

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "Id", "Name" }, records[0]);
        Assert.Equal(new[] { "2", "Grace" }, records[2]);
    }

    [Fact]
    public void QuotedField_KeepsItsEmbeddedDelimiter()
    {
        var records = CsvReader.ParseFields("Name,City\n\"Wills, Ada\",London\n", ',');

        Assert.Equal(new[] { "Wills, Ada", "London" }, records[1]);
    }

    [Fact]
    public void QuotedField_KeepsItsEmbeddedNewline()
    {
        var records = CsvReader.ParseFields("Name,Note\nAda,\"line one\nline two\"\n", ',');

        Assert.Equal(2, records.Count);
        Assert.Equal("line one\nline two", records[1][1]);
    }

    [Fact]
    public void DoubledQuote_IsOneLiteralQuote()
    {
        var records = CsvReader.ParseFields("Name\n\"say \"\"hi\"\"\"\n", ',');

        Assert.Equal("say \"hi\"", records[1][0]);
    }

    [Fact]
    public void CrLfAndLf_AreBothRowEndings()
    {
        var records = CsvReader.ParseFields("A,B\r\n1,2\n3,4\r\n", ',');

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "3", "4" }, records[2]);
    }

    [Fact]
    public void ByteOrderMark_IsNotPartOfTheFirstColumnName()
    {
        var records = CsvReader.ParseFields("\uFEFFId,Name\n1,Ada\n", ',');

        Assert.Equal("Id", records[0][0]);
    }

    /// <summary>A short row is padded and a long row truncated, so every row matches the header.
    /// A ragged export is ordinary, and throwing would make the whole file unreadable.</summary>
    [Fact]
    public void RaggedRows_ArePaddedAndTruncatedToTheHeaderWidth()
    {
        var table = CsvReader.ToTable(
            "Orders", "orders.csv", CsvReader.ParseFields("A,B,C\n1\n1,2,3,4\n", ','));

        Assert.All(table.Rows, row => Assert.Equal(3, row.Length));
        Assert.Null(table.Value(0, "C"));
        Assert.Equal("3", table.Value(1, "C"));
    }

    [Fact]
    public void EmptyUnquotedField_IsNull_NotEmptyString()
    {
        var table = CsvReader.ToTable(
            "Orders", "orders.csv", CsvReader.ParseFields("A,B\n,2\n", ','));

        Assert.Null(table.Value(0, "A"));
    }

    [Fact]
    public void TypesEachColumnFromItsValues()
    {
        var table = CsvReader.ToTable(
            "Orders", "orders.csv",
            CsvReader.ParseFields("Ref,Qty,Price\nORD-1,2,9.99\nORD-2,3,4.50\n", ','));

        Assert.Equal("string", table.Columns[0].Type);
        Assert.Equal("int", table.Columns[1].Type);
        Assert.Equal("decimal", table.Columns[2].Type);
    }

    [Fact]
    public void UnnamedColumn_GetsAPositionalName_SoItIsAddressable()
    {
        var table = CsvReader.ToTable(
            "Orders", "orders.csv", CsvReader.ParseFields("Id,,Name\n1,x,Ada\n", ','));

        Assert.Equal("Column2", table.Columns[1].Name);
    }

    [Fact]
    public void ReadsAFileFromDisk_NamingTheTableAfterIt()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Name\n1,Ada\n");

        try
        {
            var table = Assert.Single(
                new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, path)));

            Assert.Equal(Path.GetFileNameWithoutExtension(path), table.Name);
            Assert.Equal(Path.GetFileName(path), table.Label);
            Assert.Equal("Ada", table.Value(0, "Name"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HeaderOnlyFile_IsRejectedWithAMessageThatSaysWhy()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Name\n");

        try
        {
            var error = Assert.Throws<InvalidDataException>(
                () => new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, path)));

            Assert.Contains("no data rows", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~CsvReaderTests"
```

Expected: FAIL to compile — `CsvReader`, `DataSourceSpec` and `DataSourceKind` do not exist.

- [ ] **Step 3: Write the seam types**

`DataIngestion/DataSourceKind.cs`:

```csharp
namespace DataIngestion;

/// <summary>The data formats the interview offers. Excel and Sql are declared here — and named
/// at the prompt — before their readers exist, so the menu the user sees is the whole menu and
/// choosing one gets a straight answer rather than a missing option.</summary>
public enum DataSourceKind
{
    Sql,
    Excel,
    Txt,
    Json,
    Csv
}
```

`DataIngestion/DataSourceSpec.cs`:

```csharp
namespace DataIngestion;

/// <summary>
/// Where the data is. <paramref name="Location"/> is a file path for the file formats and a
/// connection string for SQL; the two SQL members stay null for every other kind.
/// </summary>
public sealed record DataSourceSpec(
    DataSourceKind Kind,
    string Location,
    string? SqlDatabase = null,
    IReadOnlyList<string>? SqlTables = null);
```

`DataIngestion/Schema/IDataSourceReader.cs`:

```csharp
namespace DataIngestion.Schema;

/// <summary>
/// The seam every format implements. One method, one return shape — adding Excel or SQL Server
/// is one more implementation of this and no change anywhere downstream.
/// </summary>
public interface IDataSourceReader
{
    /// <summary>Reads <paramref name="spec"/> into one or more tables.</summary>
    /// <exception cref="FileNotFoundException">The location does not exist.</exception>
    /// <exception cref="InvalidDataException">
    /// The source exists but carries nothing a domain model can be built from. The message says
    /// what was wrong in terms the user can act on.
    /// </exception>
    IReadOnlyList<TabularTable> Read(DataSourceSpec spec);
}
```

- [ ] **Step 4: Write the CSV reader**

`DataIngestion/Schema/Readers/CsvReader.cs`:

```csharp
using System.Text;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// Reads a delimited text file into one <see cref="TabularTable"/>. RFC4180-shaped: a quoted
/// field may contain the delimiter, a newline, or a doubled quote standing for one literal quote.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from a package, for the same reason the rest of this repo's
/// parsing is: one file, no new dependency, and every rule visible where it is tested.
/// <see cref="ParseFields"/> and <see cref="ToTable"/> are public and static because
/// <see cref="DelimitedTextReader"/> is the same two steps with a sniffed delimiter, and
/// duplicating a quote-aware scanner is how the two would drift apart.
/// </remarks>
public sealed class CsvReader : IDataSourceReader
{
    public IReadOnlyList<TabularTable> Read(DataSourceSpec spec)
    {
        if (!File.Exists(spec.Location))
            throw new FileNotFoundException($"No file at {spec.Location}.", spec.Location);

        var records = ParseFields(File.ReadAllText(spec.Location), ',');

        return new[]
        {
            ToTable(
                Path.GetFileNameWithoutExtension(spec.Location),
                Path.GetFileName(spec.Location),
                records)
        };
    }

    /// <summary>
    /// Splits <paramref name="text"/> into records of fields. The first record is the header.
    /// A field is null when the source wrote nothing there; an explicitly quoted empty field
    /// ("") is an empty string, because the source said so.
    /// </summary>
    public static IReadOnlyList<string?[]> ParseFields(string text, char delimiter)
    {
        var records = new List<string?[]>();
        var fields = new List<string?>();
        var current = new StringBuilder();

        var quoted = false;       // inside a "..." field
        var wasQuoted = false;    // this field was quoted, so "" means empty, not absent
        var started = false;      // any character seen since the last delimiter/newline

        // A BOM is an encoding marker, not data. Left in place it becomes part of the first
        // column's name, and every later lookup of that column silently misses.
        var body = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];

            if (quoted)
            {
                if (c != '"')
                {
                    current.Append(c);
                    continue;
                }

                // "" inside a quoted field is one literal quote; a lone " ends the field.
                if (i + 1 < body.Length && body[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                quoted = false;
                continue;
            }

            if (c == '"' && !started)
            {
                quoted = true;
                wasQuoted = true;
                started = true;
                continue;
            }

            if (c == delimiter)
            {
                EndField();
                continue;
            }

            if (c is '\n' or '\r')
            {
                // Swallow the LF of a CRLF so it does not open an empty record.
                if (c == '\r' && i + 1 < body.Length && body[i + 1] == '\n')
                    i++;

                EndField();
                EndRecord();
                continue;
            }

            current.Append(c);
            started = true;
        }

        // A file not ending in a newline still has a last record.
        if (started || fields.Count > 0)
        {
            EndField();
            EndRecord();
        }

        return records;

        void EndField()
        {
            fields.Add(current.Length == 0 && !wasQuoted ? null : current.ToString());
            current.Clear();
            wasQuoted = false;
            started = false;
        }

        void EndRecord()
        {
            // A blank line between records is not a row of nulls.
            if (fields.Count > 1 || fields.Any(f => f is not null))
                records.Add(fields.ToArray());

            fields.Clear();
        }
    }

    /// <summary>
    /// Turns parsed records into a typed table: the first record names the columns, the rest are
    /// squared off to the header's width, and each column is typed from its own values.
    /// </summary>
    public static TabularTable ToTable(
        string name, string label, IReadOnlyList<string?[]> records)
    {
        if (records.Count == 0)
            throw new InvalidDataException($"{label} is empty — there is no header row to read.");

        if (records.Count == 1)
            throw new InvalidDataException(
                $"{label} has a header but no data rows, so there are no values to infer types "
                + "from. Add at least one row, or build the app from a description instead.");

        var header = records[0];
        var table = new TabularTable { Name = name, SourceLabel = label };

        for (var i = 0; i < header.Length; i++)
        {
            // An unnamed column is still addressable, and its position is the only name it has.
            var columnName = string.IsNullOrWhiteSpace(header[i])
                ? $"Column{i + 1}"
                : header[i]!.Trim();

            table.Columns.Add(new TabularColumn { Name = columnName });
        }

        foreach (var record in records.Skip(1))
        {
            var row = new string?[header.Length];
            for (var i = 0; i < header.Length; i++)
                row[i] = i < record.Length ? record[i] : null;

            table.Rows.Add(row);
        }

        foreach (var column in table.Columns)
            column.Type = ColumnTypeInference.Infer(table.ColumnValues(column.Name), column.Name);

        return table;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~CsvReaderTests"
```

Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add DataIngestion DataIngestion.Tests
git commit -m "feat(data): CSV reader with quote, ragged-row and BOM handling"
```

---

## Task 3: TXT reader with delimiter sniffing

**Files:**
- Create: `DataIngestion/Schema/Readers/DelimitedTextReader.cs`
- Create: `DataIngestion.Tests/Schema/Readers/DelimitedTextReaderTests.cs`

**Interfaces:**
- Consumes: `CsvReader.ParseFields`, `CsvReader.ToTable`, `IDataSourceReader`, `DataSourceSpec`.
- Produces: `DelimitedTextReader : IDataSourceReader`, plus `static char? SniffDelimiter(string text)` and `const int SniffLines`.

- [ ] **Step 1: Write the failing test**

`DataIngestion.Tests/Schema/Readers/DelimitedTextReaderTests.cs`:

```csharp
using DataIngestion;
using DataIngestion.Schema.Readers;

namespace DataIngestion.Tests.Schema.Readers;

public class DelimitedTextReaderTests
{
    [Theory]
    [InlineData('\t', "Id\tName\n1\tAda\n2\tGrace\n")]
    [InlineData('|', "Id|Name\n1|Ada\n2|Grace\n")]
    [InlineData(';', "Id;Name\n1;Ada\n2;Grace\n")]
    [InlineData(',', "Id,Name\n1,Ada\n2,Grace\n")]
    public void Sniffs_TheDelimiterThatIsConsistentAcrossLines(char expected, string text)
        => Assert.Equal(expected, DelimitedTextReader.SniffDelimiter(text));

    /// <summary>
    /// Prose contains commas and full stops but no consistent field count. Guessing a delimiter
    /// here would produce a one-column table of sentence fragments and call it a domain model.
    /// </summary>
    [Fact]
    public void Prose_SniffsNoDelimiter()
    {
        var prose = "We lend books to members, and we track loans.\n"
                  + "Every loan has a due date.\nMembers can be suspended.\n";

        Assert.Null(DelimitedTextReader.SniffDelimiter(prose));
    }

    [Fact]
    public void InconsistentFieldCounts_SniffNoDelimiter()
        => Assert.Null(DelimitedTextReader.SniffDelimiter("a,b\nc,d,e\nf\n"));

    /// <summary>Tab wins over a comma that also appears, because tab is unambiguous in text
    /// where a comma may be punctuation inside a field.</summary>
    [Fact]
    public void TabWinsOverComma_WhenBothAreConsistent()
        => Assert.Equal('\t',
            DelimitedTextReader.SniffDelimiter("Name\tCity\nWills, Ada\tLondon\nHopper, G\tNY\n"));

    [Fact]
    public void ReadsATabSeparatedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        File.WriteAllText(path, "Ref\tQty\nORD-1\t2\nORD-2\t3\n");

        try
        {
            var table = Assert.Single(
                new DelimitedTextReader().Read(new DataSourceSpec(DataSourceKind.Txt, path)));

            Assert.Equal(new[] { "Ref", "Qty" }, table.Columns.Select(c => c.Name));
            Assert.Equal("int", table.Columns[1].Type);
            Assert.Equal("ORD-2", table.Value(1, "Ref"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NonTabularFile_IsRejectedWithAMessageThatSaysWhatToDo()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        File.WriteAllText(path, "We lend books to members.\nEvery loan has a due date.\n");

        try
        {
            var error = Assert.Throws<InvalidDataException>(
                () => new DelimitedTextReader().Read(new DataSourceSpec(DataSourceKind.Txt, path)));

            Assert.Contains("could not find a delimiter", error.Message);
            Assert.Contains("build app:", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DelimitedTextReaderTests"
```

Expected: FAIL to compile — `DelimitedTextReader` does not exist.

- [ ] **Step 3: Write the reader**

`DataIngestion/Schema/Readers/DelimitedTextReader.cs`:

```csharp
namespace DataIngestion.Schema.Readers;

/// <summary>
/// Reads a <c>.txt</c> export by working out its delimiter first. A TXT file has no declared
/// format, so the alternative to sniffing is asking the user which character it is — a question
/// they should not have to answer about their own file.
/// </summary>
public sealed class DelimitedTextReader : IDataSourceReader
{
    /// <summary>
    /// How many lines the sniff examines. Enough to see a consistent field count, few enough
    /// that a large export is not read twice for the sake of a guess.
    /// </summary>
    public const int SniffLines = 20;

    /// <summary>
    /// Candidates in priority order. Tab first because it is almost never punctuation inside a
    /// field, so where both a tab and a comma are consistent the tab is the real one. Comma is
    /// last for the same reason inverted: it is the most likely to appear inside prose.
    /// </summary>
    private static readonly char[] Candidates = ['\t', '|', ';', ','];

    public IReadOnlyList<TabularTable> Read(DataSourceSpec spec)
    {
        if (!File.Exists(spec.Location))
            throw new FileNotFoundException($"No file at {spec.Location}.", spec.Location);

        var text = File.ReadAllText(spec.Location);
        var label = Path.GetFileName(spec.Location);

        var delimiter = SniffDelimiter(text)
            ?? throw new InvalidDataException(
                $"{label} could not find a delimiter — no tab, pipe, semicolon or comma splits "
                + "its lines into a consistent number of fields, so it does not look like a "
                + "table. If it is prose describing what you want, use `build app: <description>` "
                + "instead.");

        return new[]
        {
            ToTableFrom(text, delimiter,
                Path.GetFileNameWithoutExtension(spec.Location), label)
        };
    }

    private static TabularTable ToTableFrom(
        string text, char delimiter, string name, string label)
        => CsvReader.ToTable(name, label, CsvReader.ParseFields(text, delimiter));

    /// <summary>
    /// The delimiter that splits the first <see cref="SniffLines"/> lines into the same number
    /// of fields — more than one field, and the same count every line. Null when none does.
    /// </summary>
    /// <remarks>
    /// Consistency is the whole test. Prose is full of commas, but the number of them per line
    /// varies, so a comma never passes; a real CSV's comma count is fixed by its column count.
    /// The scan is quote-aware because it goes through <see cref="CsvReader.ParseFields"/>, so a
    /// quoted "Wills, Ada" does not count as two fields.
    /// </remarks>
    public static char? SniffDelimiter(string text)
    {
        foreach (var candidate in Candidates)
        {
            var records = CsvReader.ParseFields(text, candidate)
                .Take(SniffLines)
                .ToList();

            if (records.Count < 2)
                continue;

            var width = records[0].Length;
            if (width > 1 && records.All(r => r.Length == width))
                return candidate;
        }

        return null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DelimitedTextReaderTests"
```

Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion DataIngestion.Tests
git commit -m "feat(data): TXT reader with quote-aware delimiter sniffing"
```

---

## Task 4: JSON reader, including nested child tables

**Files:**
- Create: `DataIngestion/Schema/Readers/JsonTableReader.cs`
- Create: `DataIngestion.Tests/Schema/Readers/JsonTableReaderTests.cs`

**Interfaces:**
- Consumes: `TabularTable`, `TabularColumn`, `ColumnTypeInference`, `IDataSourceReader`, `DataSourceSpec`; `DomainDiscovery.SeedExtraction.NameNormalizer.ToUserTypedEntityName` and `.ToFieldName`.
- Produces: `JsonTableReader : IDataSourceReader`; `const string ParentIdSuffix = "Id"`; `const string ScalarValueColumn = "Value"`; `static IReadOnlyList<TabularTable> ReadJson(string json, string rootName, string label)`.

JSON is the one format that legitimately yields several tables: a nested array is a child entity, and flattening it would discard the structure the user already got right.

- [ ] **Step 1: Write the failing test**

`DataIngestion.Tests/Schema/Readers/JsonTableReaderTests.cs`:

```csharp
using DataIngestion;
using DataIngestion.Schema.Readers;

namespace DataIngestion.Tests.Schema.Readers;

public class JsonTableReaderTests
{
    [Fact]
    public void ArrayOfObjects_BecomesOneTable()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"Ref":"ORD-1","Qty":2},{"Ref":"ORD-2","Qty":3}]""",
            "Orders", "orders.json"));

        Assert.Equal("Order", table.Name);
        Assert.Equal(new[] { "Ref", "Qty" }, table.Columns.Select(c => c.Name));
        Assert.Equal("int", table.Columns[1].Type);
        Assert.Equal("ORD-2", table.Value(1, "Ref"));
    }

    /// <summary>Objects need not agree on their keys: the union is the column set, and a row
    /// missing a key has a null there rather than shifting its other values along.</summary>
    [Fact]
    public void ColumnsAreTheUnionOfEveryObjectsKeys()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"A":1},{"B":2}]""", "Thing", "t.json"));

        Assert.Equal(new[] { "A", "B" }, table.Columns.Select(c => c.Name));
        Assert.Null(table.Value(0, "B"));
        Assert.Equal("2", table.Value(1, "B"));
    }

    [Fact]
    public void NestedArray_BecomesAChildTableWithAForeignKeyToItsParent()
    {
        var tables = JsonTableReader.ReadJson(
            """
            [{"Ref":"ORD-1","Lines":[{"Sku":"A","Qty":2},{"Sku":"B","Qty":1}]},
             {"Ref":"ORD-2","Lines":[{"Sku":"C","Qty":5}]}]
            """,
            "Orders", "orders.json");

        Assert.Equal(2, tables.Count);

        // The array is not a column on the parent -- it is a table.
        Assert.DoesNotContain("Lines", tables[0].Columns.Select(c => c.Name));

        var child = tables[1];
        Assert.Equal("Line", child.Name);
        Assert.Equal(3, child.Rows.Count);
        Assert.Equal(new[] { "OrderId", "Sku", "Qty" }, child.Columns.Select(c => c.Name));

        // 1-based parent row index, because seeded Ids are assigned 1..N in row order.
        Assert.Equal("1", child.Value(0, "OrderId"));
        Assert.Equal("2", child.Value(2, "OrderId"));
        Assert.Equal("int", child.Columns.Single(c => c.Name == "OrderId").Type);
    }

    /// <summary>A nested single object is one-to-one, so its keys are flattened onto the parent
    /// with a prefix rather than becoming a table of one row per parent.</summary>
    [Fact]
    public void NestedObject_IsFlattenedOntoTheParentWithAPrefix()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"Ref":"ORD-1","Address":{"City":"London","PostCode":"E1"}}]""",
            "Orders", "orders.json"));

        Assert.Equal(new[] { "Ref", "AddressCity", "AddressPostCode" },
            table.Columns.Select(c => c.Name));
        Assert.Equal("London", table.Value(0, "AddressCity"));
    }

    /// <summary>An array of scalars is non-atomic data, not a table of objects. It becomes a
    /// child table of one value column -- the same shape the normalization analyser proposes for
    /// the same data written as a comma-separated string.</summary>
    [Fact]
    public void NestedScalarArray_BecomesAChildTableOfValues()
    {
        var tables = JsonTableReader.ReadJson(
            """[{"Ref":"ORD-1","Tags":["rush","gift"]}]""", "Orders", "orders.json");

        var child = tables[1];
        Assert.Equal("Tag", child.Name);
        Assert.Equal(new[] { "OrderId", "Value" }, child.Columns.Select(c => c.Name));
        Assert.Equal("rush", child.Value(0, "Value"));
        Assert.Equal(2, child.Rows.Count);
    }

    [Fact]
    public void JsonNull_IsAnAbsentValue()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"A":null},{"A":"x"}]""", "Thing", "t.json"));

        Assert.Null(table.Value(0, "A"));
    }

    [Fact]
    public void BooleanAndNumericJson_KeepTheirTypes()
    {
        var table = Assert.Single(JsonTableReader.ReadJson(
            """[{"Paid":true,"Total":9.99},{"Paid":false,"Total":1.50}]""", "Thing", "t.json"));

        Assert.Equal("bool", table.Columns.Single(c => c.Name == "Paid").Type);
        Assert.Equal("decimal", table.Columns.Single(c => c.Name == "Total").Type);
    }

    [Fact]
    public void SingleTopLevelObject_IsReadAsAOneRowTable()
        => Assert.Single(Assert.Single(JsonTableReader.ReadJson(
            """{"Ref":"ORD-1"}""", "Orders", "orders.json")).Rows);

    [Fact]
    public void EmptyArray_IsRejectedWithAMessageThatSaysWhy()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => JsonTableReader.ReadJson("[]", "Orders", "orders.json"));

        Assert.Contains("no objects", error.Message);
    }

    [Fact]
    public void MalformedJson_IsRejectedWithAMessageNamingTheFile()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => JsonTableReader.ReadJson("{not json", "Orders", "orders.json"));

        Assert.Contains("orders.json", error.Message);
    }

    [Fact]
    public void ReadsAFileFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, """[{"Ref":"ORD-1"}]""");

        try
        {
            var table = Assert.Single(
                new JsonTableReader().Read(new DataSourceSpec(DataSourceKind.Json, path)));

            Assert.Equal("ORD-1", table.Value(0, "Ref"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~JsonTableReaderTests"
```

Expected: FAIL to compile — `JsonTableReader` does not exist.

- [ ] **Step 3: Write the reader**

`DataIngestion/Schema/Readers/JsonTableReader.cs`:

```csharp
using System.Text.Json;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// Reads a JSON document into tables. Unlike CSV and TXT, JSON already carries structure, and
/// this reader keeps it: a nested array is a child entity with a foreign key, not a column.
/// </summary>
public sealed class JsonTableReader : IDataSourceReader
{
    /// <summary>Appended to a parent's entity name to make the child's foreign-key column, so it
    /// matches the <c>&lt;Target&gt;Id</c> convention <c>ForeignKeys</c> and
    /// <c>DomainModelMapper</c> already share and the generated nav properties line up.</summary>
    public const string ParentIdSuffix = "Id";

    /// <summary>The value column of a child table extracted from an array of scalars.</summary>
    public const string ScalarValueColumn = "Value";

    public IReadOnlyList<TabularTable> Read(DataSourceSpec spec)
    {
        if (!File.Exists(spec.Location))
            throw new FileNotFoundException($"No file at {spec.Location}.", spec.Location);

        return ReadJson(
            File.ReadAllText(spec.Location),
            Path.GetFileNameWithoutExtension(spec.Location),
            Path.GetFileName(spec.Location));
    }

    /// <summary>Parses <paramref name="json"/> into a root table named after
    /// <paramref name="rootName"/>, plus a child table per nested array at any depth.</summary>
    public static IReadOnlyList<TabularTable> ReadJson(string json, string rootName, string label)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{label} is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            var objects = root.ValueKind switch
            {
                // A single object is a table of one row, not an error.
                JsonValueKind.Object => new List<JsonElement> { root.Clone() },
                JsonValueKind.Array => root.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => e.Clone())
                    .ToList(),
                _ => throw new InvalidDataException(
                    $"{label} must hold an object or an array of objects at the top level."),
            };

            if (objects.Count == 0)
                throw new InvalidDataException(
                    $"{label} contains no objects, so there are no columns to read. An array of "
                    + "objects is what this reader needs.");

            var children = new List<TabularTable>();
            var tables = new List<TabularTable> { BuildTable(rootName, label, objects, children) };
            tables.AddRange(children);

            foreach (var table in tables)
                foreach (var column in table.Columns)
                    column.Type = ColumnTypeInference.Infer(
                        table.ColumnValues(column.Name), column.Name);

            return tables;
        }
    }

    /// <summary>
    /// Builds one table from <paramref name="objects"/>, appending a child table to
    /// <paramref name="children"/> for each nested array. Columns are the UNION of every
    /// object's keys, in order of first appearance, so objects that disagree still line up.
    /// </summary>
    private static TabularTable BuildTable(
        string name, string label, List<JsonElement> objects, List<TabularTable> children)
    {
        var entityName = NameNormalizer.ToUserTypedEntityName(name);
        var table = new TabularTable { Name = entityName, SourceLabel = label };

        var columnNames = new List<string>();
        var cells = new List<Dictionary<string, string?>>();

        // Nested arrays, keyed by property name, each element tagged with the 1-BASED index of
        // the parent row it came from. That index is the child's foreign key, and it is 1-based
        // because seeded Ids are assigned 1..N in row order.
        var arrays = new Dictionary<string, List<(int ParentRow, JsonElement Element)>>(
            StringComparer.Ordinal);

        for (var rowIndex = 0; rowIndex < objects.Count; rowIndex++)
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            Flatten(objects[rowIndex], string.Empty, rowIndex + 1, row, columnNames, arrays);
            cells.Add(row);
        }

        foreach (var columnName in columnNames)
            table.Columns.Add(new TabularColumn { Name = columnName });

        foreach (var row in cells)
            table.Rows.Add(columnNames
                .Select(c => row.TryGetValue(c, out var v) ? v : null)
                .ToArray());

        // Recursive: an array inside an array is a grandchild, and the same rule applies.
        foreach (var (property, elements) in arrays)
        {
            var foreignKey = entityName + ParentIdSuffix;
            var childName = NameNormalizer.ToUserTypedEntityName(property);

            var allObjects = elements.All(e => e.Element.ValueKind == JsonValueKind.Object);

            children.Add(allObjects
                ? ObjectChild(childName, label, foreignKey, elements, children)
                : ScalarChild(childName, label, foreignKey, elements));
        }

        return table;
    }

    /// <summary>A child whose elements are objects: the parent FK first, then its own columns.</summary>
    private static TabularTable ObjectChild(
        string childName,
        string label,
        string foreignKey,
        List<(int ParentRow, JsonElement Element)> elements,
        List<TabularTable> children)
    {
        var inner = BuildTable(
            childName, label, elements.Select(e => e.Element).ToList(), children);

        var child = new TabularTable { Name = inner.Name, SourceLabel = label };
        child.Columns.Add(new TabularColumn { Name = foreignKey, Type = "int" });
        child.Columns.AddRange(inner.Columns);

        for (var i = 0; i < inner.Rows.Count; i++)
        {
            var row = new string?[child.Columns.Count];
            row[0] = elements[i].ParentRow.ToString();
            Array.Copy(inner.Rows[i], 0, row, 1, inner.Rows[i].Length);
            child.Rows.Add(row);
        }

        return child;
    }

    /// <summary>A child whose elements are scalars: the parent FK and one value column.</summary>
    private static TabularTable ScalarChild(
        string childName,
        string label,
        string foreignKey,
        List<(int ParentRow, JsonElement Element)> elements)
    {
        var child = new TabularTable { Name = childName, SourceLabel = label };
        child.Columns.Add(new TabularColumn { Name = foreignKey, Type = "int" });
        child.Columns.Add(new TabularColumn { Name = ScalarValueColumn });

        foreach (var (parentRow, element) in elements)
            child.Rows.Add([parentRow.ToString(), Scalar(element)]);

        return child;
    }

    /// <summary>
    /// Writes one object's scalars into <paramref name="row"/>, recording any new column name.
    /// A nested object is flattened under its property name (one-to-one needs no table); a
    /// nested array is set aside as a child table (one-to-many does).
    /// </summary>
    private static void Flatten(
        JsonElement element,
        string prefix,
        int parentRow,
        Dictionary<string, string?> row,
        List<string> columnNames,
        Dictionary<string, List<(int ParentRow, JsonElement Element)>> arrays)
    {
        foreach (var property in element.EnumerateObject())
        {
            var name = prefix + NameNormalizer.ToFieldName(property.Name);

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, name, parentRow, row, columnNames, arrays);
                    break;

                case JsonValueKind.Array:
                    if (!arrays.TryGetValue(property.Name, out var elements))
                        arrays[property.Name] = elements = new();

                    foreach (var item in property.Value.EnumerateArray())
                        elements.Add((parentRow, item.Clone()));
                    break;

                default:
                    if (!columnNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        columnNames.Add(name);

                    row[name] = Scalar(property.Value);
                    break;
            }
        }
    }

    /// <summary>A scalar as the raw string every reader produces. JSON null is an absent value,
    /// which is what an empty CSV cell means too.</summary>
    private static string? Scalar(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText(),
        };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~JsonTableReaderTests"
```

Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion DataIngestion.Tests
git commit -m "feat(data): JSON reader keeping nested arrays as child entities"
```

---

## Task 5: Reader registry, and the two formats this branch does not implement

**Files:**
- Create: `DataIngestion/Schema/Readers/DataSourceReaders.cs`
- Create: `DataIngestion.Tests/Schema/Readers/DataSourceReadersTests.cs`

**Interfaces:**
- Consumes: `CsvReader`, `DelimitedTextReader`, `JsonTableReader`, `DataSourceKind`.
- Produces:
  - `DataSourceReaders.Keys -> IReadOnlyList<string>` — the prompt's menu, `["sql","excel","txt","json","csv"]`
  - `DataSourceReaders.TryParseKind(string? text, out DataSourceKind kind) -> bool`
  - `DataSourceReaders.TryCreate(DataSourceKind kind, out IDataSourceReader reader) -> bool`
  - `DataSourceReaders.NotSupportedMessage(DataSourceKind kind) -> string`

The interview offers all five formats — a menu missing Excel reads as "we never thought of Excel" rather than "Excel is next" — so exactly one place has to know which have readers, and this is it.

- [ ] **Step 1: Write the failing test**

`DataIngestion.Tests/Schema/Readers/DataSourceReadersTests.cs`:

```csharp
using DataIngestion;
using DataIngestion.Schema.Readers;

namespace DataIngestion.Tests.Schema.Readers;

public class DataSourceReadersTests
{
    [Theory]
    [InlineData(DataSourceKind.Csv)]
    [InlineData(DataSourceKind.Txt)]
    [InlineData(DataSourceKind.Json)]
    public void SupportedKinds_HaveAReader(DataSourceKind kind)
    {
        Assert.True(DataSourceReaders.TryCreate(kind, out var reader));
        Assert.NotNull(reader);
    }

    [Theory]
    [InlineData(DataSourceKind.Excel)]
    [InlineData(DataSourceKind.Sql)]
    public void FormatsNotInThisBuild_HaveNoReader(DataSourceKind kind)
        => Assert.False(DataSourceReaders.TryCreate(kind, out _));

    /// <summary>A refusal without an alternative leaves the user at a dead end, so the message
    /// must name the formats that do work.</summary>
    [Theory]
    [InlineData(DataSourceKind.Excel)]
    [InlineData(DataSourceKind.Sql)]
    public void NotSupportedMessage_NamesTheWorkingFormats(DataSourceKind kind)
    {
        var message = DataSourceReaders.NotSupportedMessage(kind);

        Assert.Contains("csv", message);
        Assert.Contains("txt", message);
        Assert.Contains("json", message);
    }

    /// <summary>Every kind is offered at the prompt, including the two with no reader.</summary>
    [Fact]
    public void Keys_OfferEveryKind()
        => Assert.Equal(new[] { "sql", "excel", "txt", "json", "csv" }, DataSourceReaders.Keys);

    [Theory]
    [InlineData("csv", DataSourceKind.Csv)]
    [InlineData("CSV", DataSourceKind.Csv)]
    [InlineData("  json  ", DataSourceKind.Json)]
    [InlineData("excel", DataSourceKind.Excel)]
    [InlineData("xlsx", DataSourceKind.Excel)]
    [InlineData("sql", DataSourceKind.Sql)]
    [InlineData("sqlserver", DataSourceKind.Sql)]
    [InlineData("txt", DataSourceKind.Txt)]
    [InlineData("text", DataSourceKind.Txt)]
    public void TryParseKind_AcceptsTheNamesAUserWouldType(string text, DataSourceKind expected)
    {
        Assert.True(DataSourceReaders.TryParseKind(text, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseKind_RejectsAnythingElse(string? text)
        => Assert.False(DataSourceReaders.TryParseKind(text, out _));
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DataSourceReadersTests"
```

Expected: FAIL to compile — `DataSourceReaders` does not exist.

- [ ] **Step 3: Write the registry**

`DataIngestion/Schema/Readers/DataSourceReaders.cs`:

```csharp
namespace DataIngestion.Schema.Readers;

/// <summary>
/// Maps a <see cref="DataSourceKind"/> to its reader, and answers for the two kinds that have no
/// reader yet. The interview offers every kind, so exactly one place has to know which are
/// implemented — this one.
/// </summary>
public static class DataSourceReaders
{
    /// <summary>The prompt's menu, in the order the requirements listed the formats.</summary>
    public static IReadOnlyList<string> Keys { get; } = ["sql", "excel", "txt", "json", "csv"];

    /// <summary>Spellings a user may reasonably type for each kind.</summary>
    private static readonly Dictionary<string, DataSourceKind> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sql"] = DataSourceKind.Sql,
            ["sqlserver"] = DataSourceKind.Sql,
            ["mssql"] = DataSourceKind.Sql,
            ["database"] = DataSourceKind.Sql,
            ["excel"] = DataSourceKind.Excel,
            ["xlsx"] = DataSourceKind.Excel,
            ["xls"] = DataSourceKind.Excel,
            ["spreadsheet"] = DataSourceKind.Excel,
            ["txt"] = DataSourceKind.Txt,
            ["text"] = DataSourceKind.Txt,
            ["tsv"] = DataSourceKind.Txt,
            ["json"] = DataSourceKind.Json,
            ["csv"] = DataSourceKind.Csv,
        };

    public static bool TryParseKind(string? text, out DataSourceKind kind)
    {
        kind = default;
        return !string.IsNullOrWhiteSpace(text) && Aliases.TryGetValue(text.Trim(), out kind);
    }

    public static bool TryCreate(DataSourceKind kind, out IDataSourceReader reader)
    {
        switch (kind)
        {
            case DataSourceKind.Csv:
                reader = new CsvReader();
                return true;
            case DataSourceKind.Txt:
                reader = new DelimitedTextReader();
                return true;
            case DataSourceKind.Json:
                reader = new JsonTableReader();
                return true;
            default:
                reader = null!;
                return false;
        }
    }

    /// <summary>What to tell a user who picked a kind this build cannot read.</summary>
    public static string NotSupportedMessage(DataSourceKind kind)
    {
        var advice = kind == DataSourceKind.Excel
            ? "Save the sheet as CSV and choose csv."
            : "Export the tables you want as CSV and choose csv.";

        return $"{kind} is not in this build yet — it lands in a follow-on branch. "
             + $"Working formats: csv, txt, json. {advice}";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DataSourceReadersTests"
```

Expected: PASS, 22 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion DataIngestion.Tests
git commit -m "feat(data): reader registry naming every format, implementing three"
```

---

## Task 6: `DataModelBuilder` — tables to a candidate domain model

**Files:**
- Modify: `DomainDiscovery/Model/CandidateDomainModel.cs` (add `Provenance.Data`)
- Modify: `DomainDiscovery/Model/DecisionReasons.cs` (add the data-path reasons)
- Create: `DataIngestion/DataModelBuilder.cs`
- Create: `DataIngestion.Tests/DataModelBuilderTests.cs`

**Interfaces:**
- Consumes: `TabularTable`, `TabularColumn`; `CandidateDomainModel`, `CandidateEntity`, `CandidateField`, `CandidateRelationship`, `Provenance`, `DecisionStage`, `DecisionOutcome`, `DecisionReasons`; `NameNormalizer.ToUserTypedEntityName`, `.ToFieldName`, `.IsValidIdentifier`.
- Produces: `DataModelBuilder.Build(IReadOnlyList<TabularTable> tables) -> CandidateDomainModel`; `DataModelBuilder.NameSuffix` (`const string` = `"Name"`); `DataModelBuilder.ValueSuffix` (`const string` = `"Value"`).

This is where the two collisions the existing code proves fatal get handled, and where every kept and dropped column gets a `DecisionRecord` so the review loop's `why` can explain it.

- [ ] **Step 1: Extend `Provenance` and `DecisionReasons`**

In `DomainDiscovery/Model/CandidateDomainModel.cs`, add a third value to `Provenance`:

```csharp
/// <summary>Where a discovered entity or field came from.</summary>
public enum Provenance
{
    /// <summary>Extracted from the user's own text.</summary>
    Seed,

    /// <summary>Harvested from a fetched Wikipedia article.</summary>
    Wiki,

    /// <summary>
    /// Read from a data source the user supplied — a CSV column, a JSON property, a SQL column.
    /// Distinct from <see cref="Seed"/> because the review loop prints this verbatim, and calling
    /// a real column "Seed" would claim the pipeline guessed at something it was told.
    /// </summary>
    Data
}
```

In `DomainDiscovery/Model/DecisionReasons.cs`, add a section before `// --- Review loop ---`:

```csharp
    // --- Data ingestion ---
    public const string ReadFromYourData = "read from your data";
    public const string PrimaryKeyIsGenerated = "a key column is generated, not read";
    public const string ColumnWouldCollideWithItsClassName =
        "a column may not share its class's name";
    public const string ColumnNameIsNotAValidIdentifier = "its column name is not a usable name";
```

- [ ] **Step 2: Write the failing test**

`DataIngestion.Tests/DataModelBuilderTests.cs`:

```csharp
using DataIngestion.Schema;
using DomainDiscovery.Model;

namespace DataIngestion.Tests;

public class DataModelBuilderTests
{
    private static TabularTable Table(string name, params (string Column, string Type)[] columns)
    {
        var table = new TabularTable { Name = name, SourceLabel = name.ToLowerInvariant() + ".csv" };
        foreach (var (column, type) in columns)
            table.Columns.Add(new TabularColumn { Name = column, Type = type });

        // One row, so the table is never empty; values are irrelevant to model building.
        table.Rows.Add(columns.Select(_ => (string?)"x").ToArray());
        return table;
    }

    [Fact]
    public void EachTableBecomesAnEntityCarryingItsColumnsAsTypedFields()
    {
        var model = DataModelBuilder.Build(
            [Table("Orders", ("Ref", "string"), ("Qty", "int"), ("Total", "decimal"))]);

        var order = Assert.Single(model.Entities);
        Assert.Equal("Order", order.Name);
        Assert.Equal(Provenance.Data, order.Provenance);
        Assert.Equal(new[] { "Ref", "Qty", "Total" }, order.Fields.Select(f => f.Name));
        Assert.Equal(new[] { "string", "int", "decimal" }, order.Fields.Select(f => f.Type));
        Assert.All(order.Fields, f => Assert.Equal(Provenance.Data, f.Provenance));
    }

    /// <summary>
    /// DomainModelMapper prepends its own `int Id`. Keeping a read Id would leave a string key
    /// that compiles and then makes JsonStore -- which assigns Id by reflection -- throw on the
    /// first Add. ModelEditor.AddField refuses an Id field for exactly this reason.
    /// </summary>
    [Theory]
    [InlineData("Id")]
    [InlineData("id")]
    [InlineData("OrderId")]
    public void AKeyColumnIsDropped_BecauseIdIsGenerated(string column)
    {
        var model = DataModelBuilder.Build([Table("Orders", (column, "int"), ("Ref", "string"))]);

        var order = Assert.Single(model.Entities);
        Assert.Equal(new[] { "Ref" }, order.Fields.Select(f => f.Name));
        Assert.Contains(model.Decisions,
            d => d.Name == column && d.Reason == DecisionReasons.PrimaryKeyIsGenerated);
    }

    /// <summary>
    /// A `&lt;Other&gt;Id` column is a foreign key, not this entity's own key, so it is kept AND
    /// paired with a relationship -- which is what makes DomainModelMapper emit the nav property.
    /// </summary>
    [Fact]
    public void AForeignKeyColumnIsKeptAndBecomesARelationship()
    {
        var model = DataModelBuilder.Build(
        [
            Table("Customers", ("Name", "string")),
            Table("Orders", ("CustomerId", "int"), ("Ref", "string")),
        ]);

        var order = model.Entities.Single(e => e.Name == "Order");

        Assert.Contains("CustomerId", order.Fields.Select(f => f.Name));
        var relationship = Assert.Single(order.Relationships);
        Assert.Equal("Customer", relationship.Target);
        Assert.Equal("references", relationship.Role);
    }

    /// <summary>A `&lt;Other&gt;Id` naming no table in this run is an incidental int, not a
    /// foreign key, and must not acquire a relationship to a class that will never exist.</summary>
    [Fact]
    public void AnIdColumnNamingNoTable_GetsNoRelationship()
    {
        var model = DataModelBuilder.Build([Table("Orders", ("LegacyId", "int"), ("Ref", "string"))]);

        var order = Assert.Single(model.Entities);
        Assert.Contains("LegacyId", order.Fields.Select(f => f.Name));
        Assert.Empty(order.Relationships);
    }

    /// <summary>`class Product { public string Product { get; set; } }` is CS0542.</summary>
    [Fact]
    public void AColumnNamedAfterItsOwnClassIsRenamed()
    {
        var model = DataModelBuilder.Build([Table("Products", ("Product", "string"), ("Sku", "string"))]);

        var product = Assert.Single(model.Entities);
        Assert.Equal("Product", product.Name);
        Assert.Contains("ProductName", product.Fields.Select(f => f.Name));
        Assert.DoesNotContain("Product", product.Fields.Select(f => f.Name));
        Assert.Contains(model.Decisions,
            d => d.Reason == DecisionReasons.ColumnWouldCollideWithItsClassName);
    }

    /// <summary>...and if the renamed form is taken too, fall further back rather than
    /// producing two fields with one name.</summary>
    [Fact]
    public void TheRenameFallsBackAgainWhenItsFirstChoiceIsTaken()
    {
        var model = DataModelBuilder.Build(
            [Table("Products", ("Product", "string"), ("ProductName", "string"))]);

        var product = Assert.Single(model.Entities);
        Assert.Equal(new[] { "ProductValue", "ProductName" }.OrderBy(n => n),
            product.Fields.Select(f => f.Name).OrderBy(n => n));
    }

    [Fact]
    public void ColumnNamesAreNormalizedToIdentifiers()
    {
        var model = DataModelBuilder.Build(
            [Table("Orders", ("order ref", "string"), ("due-date", "DateTime"))]);

        Assert.Equal(new[] { "OrderRef", "DueDate" },
            Assert.Single(model.Entities).Fields.Select(f => f.Name));
    }

    /// <summary>A column whose name cannot become an identifier is dropped with a reason, not
    /// emitted as something that will not compile.</summary>
    [Fact]
    public void AColumnNameThatCannotBeAnIdentifierIsDroppedWithAReason()
    {
        var model = DataModelBuilder.Build([Table("Orders", ("2nd line", "string"), ("Ref", "string"))]);

        Assert.Equal(new[] { "Ref" }, Assert.Single(model.Entities).Fields.Select(f => f.Name));
        Assert.Contains(model.Decisions,
            d => d.Reason == DecisionReasons.ColumnNameIsNotAValidIdentifier);
    }

    /// <summary>A table whose every column was dropped still needs one editable column, the same
    /// fallback DefaultFields gives a hand-added entity.</summary>
    [Fact]
    public void ATableLeftWithNoFieldsGetsTheDefaultNameField()
    {
        var model = DataModelBuilder.Build([Table("Orders", ("Id", "int"))]);

        Assert.Equal(new[] { DefaultFields.NameField },
            Assert.Single(model.Entities).Fields.Select(f => f.Name));
    }

    [Fact]
    public void EveryKeptEntityIsRecordedAsReadFromTheData()
    {
        var model = DataModelBuilder.Build([Table("Orders", ("Ref", "string"))]);

        Assert.Contains(model.Decisions, d =>
            d.Name == "Order"
            && d.Stage == DecisionStage.SeedExtraction
            && d.Outcome == DecisionOutcome.Kept
            && d.Reason == DecisionReasons.ReadFromYourData
            && d.Evidence!.Contains("orders.csv"));
    }

    /// <summary>Two sources whose names normalize to the same entity must not both become it --
    /// the second would silently overwrite the first's fields.</summary>
    [Fact]
    public void TwoTablesNormalizingToOneName_KeepOnlyTheFirst()
    {
        var model = DataModelBuilder.Build(
            [Table("Orders", ("Ref", "string")), Table("Order", ("Other", "string"))]);

        Assert.Single(model.Entities);
        Assert.Contains(model.Decisions, d => d.Reason == DecisionReasons.AlreadyNamed);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DataModelBuilderTests"
```

Expected: FAIL to compile — `DataModelBuilder` does not exist.

- [ ] **Step 4: Write the builder**

`DataIngestion/DataModelBuilder.cs`:

```csharp
using DataIngestion.Schema;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion;

/// <summary>
/// Turns read tables into the same <see cref="CandidateDomainModel"/> Wikipedia discovery
/// produces, so the review loop and the scaffolder see one type and know nothing about where it
/// came from.
/// </summary>
/// <remarks>
/// There is no shaping stage here and there should not be. Shaping exists to decide whether a
/// noun harvested from prose is a domain class at all; a column in the user's own data needs no
/// such adjudication. What this class does instead is guard the two name collisions that make
/// the generated project fail to compile, and record every drop so `why` can explain it.
/// </remarks>
public static class DataModelBuilder
{
    /// <summary>First fallback for a column that shares its class's name.</summary>
    public const string NameSuffix = "Name";

    /// <summary>Second fallback, for when the first is taken as well.</summary>
    public const string ValueSuffix = "Value";

    /// <summary>The role every data-derived foreign key carries. Matches what
    /// <c>RelationshipMiner</c> emits, which is what <c>DomainModelMapper</c> reads.</summary>
    private const string ReferencesRole = "references";

    public static CandidateDomainModel Build(IReadOnlyList<TabularTable> tables)
    {
        var model = new CandidateDomainModel();

        // Entity name -> the table it came from, so a foreign key can be resolved against the
        // set of entities this run actually produces rather than against a guess.
        var entityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);

            if (entityName.Length == 0 || !NameNormalizer.IsValidIdentifier(entityName))
            {
                model.Record(table.Name, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.NameNormalizedToNothing, $"from {table.Label}");
                continue;
            }

            if (!entityNames.Add(entityName))
            {
                model.Record(table.Name, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.AlreadyNamed, $"{table.Label} would also become {entityName}");
                continue;
            }

            var entity = new CandidateEntity
            {
                Name = entityName,
                Provenance = Provenance.Data,
                Confidence = 1.0,   // the user's own data; nothing was guessed
            };

            AddFields(model, entity, table);

            // Every entity keeps at least one editable column, exactly as a hand-added one does.
            if (entity.Fields.Count == 0)
                DefaultFields.ApplyTo(entity, Provenance.Data);

            model.Entities.Add(entity);
            model.Record(entityName, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                DecisionReasons.ReadFromYourData,
                $"{table.Columns.Count} column(s) in {table.Label}");
        }

        AddRelationships(model, entityNames);
        return model;
    }

    private static void AddFields(
        CandidateDomainModel model, CandidateEntity entity, TabularTable table)
    {
        foreach (var column in table.Columns)
        {
            var fieldName = NameNormalizer.ToFieldName(column.Name);

            if (!NameNormalizer.IsValidIdentifier(fieldName))
            {
                model.Record(column.Name, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.ColumnNameIsNotAValidIdentifier,
                    $"'{column.Name}' in {table.Label}");
                continue;
            }

            // This entity's OWN key. Dropped because DomainModelMapper prepends `int Id` itself,
            // and a read string Id compiles and then makes JsonStore throw on the first Add.
            if (IsOwnKey(fieldName, entity.Name))
            {
                model.Record(column.Name, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.PrimaryKeyIsGenerated, $"'{column.Name}' in {table.Label}");
                continue;
            }

            var resolved = Resolve(fieldName, entity);
            if (resolved is null)
                continue;   // duplicate after normalization; the first spelling already won

            if (!string.Equals(resolved, fieldName, StringComparison.Ordinal))
            {
                model.Record(column.Name, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                    DecisionReasons.ColumnWouldCollideWithItsClassName,
                    $"'{column.Name}' in {table.Label} became {resolved}");
            }

            entity.Fields.Add(new CandidateField
            {
                Name = resolved,
                Type = column.Type,
                Provenance = Provenance.Data
            });
        }
    }

    /// <summary>Whether <paramref name="fieldName"/> is this entity's own primary key —
    /// <c>Id</c>, or <c>&lt;Entity&gt;Id</c>. A <c>&lt;Other&gt;Id</c> is a foreign key and is
    /// kept.</summary>
    private static bool IsOwnKey(string fieldName, string entityName)
        => string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase)
           || string.Equals(fieldName, entityName + "Id", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name this field can actually carry: itself, or a suffixed form when it would collide
    /// with its own class (CS0542). Null when every candidate is already taken, in which case the
    /// column is a duplicate of one already added.
    /// </summary>
    private static string? Resolve(string fieldName, CandidateEntity entity)
    {
        bool Taken(string name) => entity.Fields.Any(
            f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        var collidesWithClass = string.Equals(
            fieldName, entity.Name, StringComparison.OrdinalIgnoreCase);

        foreach (var candidate in collidesWithClass
                     ? new[] { entity.Name + NameSuffix, entity.Name + ValueSuffix }
                     : new[] { fieldName })
        {
            if (!Taken(candidate))
                return candidate;
        }

        return collidesWithClass || Taken(fieldName) ? null : fieldName;
    }

    /// <summary>
    /// Pairs each <c>&lt;Target&gt;Id</c> field with a relationship to that target, but only when
    /// the target is an entity this run produced. Without that check a column named
    /// <c>LegacyId</c> would emit a navigation property typed as a class that is never generated,
    /// which does not compile — the same trap <c>DomainModelMapper</c> guards on its own side.
    /// </summary>
    private static void AddRelationships(
        CandidateDomainModel model, HashSet<string> entityNames)
    {
        foreach (var entity in model.Entities)
        {
            foreach (var field in entity.Fields)
            {
                if (field.Type != "int"
                    || !field.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = field.Name[..^2];

                if (target.Length == 0
                    || !entityNames.Contains(target)
                    || string.Equals(target, entity.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                entity.Relationships.Add(new CandidateRelationship
                {
                    Target = target,
                    Role = ReferencesRole
                });
            }
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DataModelBuilderTests"
```

Expected: PASS, 13 tests.

- [ ] **Step 6: Verify nothing that reads `Provenance` broke**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: the whole suite passes. `Provenance` is only ever used as `.Seed`/`.Wiki` with no exhaustive switch, so adding `Data` should be additive — this step is what confirms it rather than assuming it.

- [ ] **Step 7: Commit**

```bash
git add DataIngestion DataIngestion.Tests DomainDiscovery
git commit -m "feat(data): build a candidate model from read tables"
```

---

## Task 7: Normalization findings — the 1NF detections

**Files:**
- Create: `DataIngestion/Normalization/NormalizationFinding.cs`, `DataIngestion/Normalization/NormalizationReport.cs`, `DataIngestion/Normalization/NormalizationAnalyzer.cs`
- Create: `DataIngestion.Tests/Normalization/RepeatingGroupTests.cs`, `DataIngestion.Tests/Normalization/NonAtomicValueTests.cs`

**Interfaces:**
- Consumes: `TabularTable`, `TabularColumn`.
- Produces:
  - `enum NormalizationFindingKind { RepeatingGroup, NonAtomicValues, FunctionalDependency, DerivedColumn }`
  - `NormalizationFinding` — `Kind`, `TableName`, `Columns`, `ProposedEntity`, `Headline`, `Proposal`, `Evidence`, and for non-atomic only, `Separator`
  - `NormalizationReport(IReadOnlyList<NormalizationFinding> Findings, IReadOnlyList<string> Notes)`
  - `NormalizationAnalyzer.Analyze(TabularTable table) -> NormalizationReport`
  - constants `NonAtomicShare` (0.6), `MinRecurrence` (2), `MinRowsForDependencies` (5), `Separators`

Task 8 adds the other two detections to the same `Analyze`. All four detections are independently reviewable, which is why they are two tasks and not one.

- [ ] **Step 1: Write the failing tests**

`DataIngestion.Tests/Normalization/RepeatingGroupTests.cs`:

```csharp
using DataIngestion.Normalization;
using DataIngestion.Schema;

namespace DataIngestion.Tests.Normalization;

public class RepeatingGroupTests
{
    /// <summary>Builds a table from a header row and data rows, typing each column from its values.</summary>
    internal static TabularTable Table(string name, string[] header, params string?[][] rows)
    {
        var table = new TabularTable { Name = name, SourceLabel = name + ".csv" };
        foreach (var column in header)
            table.Columns.Add(new TabularColumn { Name = column });

        foreach (var row in rows)
            table.Rows.Add(row);

        foreach (var column in table.Columns)
            column.Type = ColumnTypeInference.Infer(table.ColumnValues(column.Name), column.Name);

        return table;
    }

    [Fact]
    public void ThreeOrdinalColumnsAreARepeatingGroup()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Orders",
            ["Ref", "Item1", "Item2", "Item3"],
            ["ORD-1", "Nut", "Bolt", "Washer"],
            ["ORD-2", "Nut", null, null]));

        var finding = Assert.Single(
            report.Findings.Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup));

        Assert.Equal(new[] { "Item1", "Item2", "Item3" }, finding.Columns);

        // ProposedEntity is the FINAL entity name, so NormalizationPlan never has to re-derive
        // it from the proposal text. The parent is singularized: Orders.csv is about an Order.
        Assert.Equal("OrderItem", finding.ProposedEntity);
        Assert.Contains("Item1, Item2, Item3", finding.Headline);
        Assert.Contains("many per Order", finding.Proposal);
    }

    [Fact]
    public void UnderscoreSeparatedOrdinalsAreAlsoARepeatingGroup()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Members",
            ["Name", "Phone_1", "Phone_2"],
            ["Ada", "555-1", "555-2"],
            ["Grace", "555-3", null]));

        var finding = Assert.Single(
            report.Findings.Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup));

        Assert.Equal("MemberPhone", finding.ProposedEntity);
    }

    /// <summary>One ordinal column is not a group -- there is nothing repeating about it.</summary>
    [Fact]
    public void ASingleOrdinalColumnIsNotAGroup()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Orders",
            ["Ref", "Line1"], ["ORD-1", "Nut"], ["ORD-2", "Bolt"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.RepeatingGroup);
    }

    /// <summary>Different stems are different columns that happen to end in digits.</summary>
    [Fact]
    public void ColumnsWithDifferentStemsAreNotAGroup()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Orders",
            ["Address1", "Phone2"], ["A", "B"], ["C", "D"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.RepeatingGroup);
    }

    /// <summary>The evidence must show the actual cells, or the user cannot check the claim.</summary>
    [Fact]
    public void EvidenceQuotesRealRows()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Orders",
            ["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"], ["ORD-2", "Washer", null]));

        var finding = report.Findings.Single(
            f => f.Kind == NormalizationFindingKind.RepeatingGroup);

        Assert.Contains(finding.Evidence, line => line.Contains("Nut") && line.Contains("Bolt"));
    }
}
```

`DataIngestion.Tests/Normalization/NonAtomicValueTests.cs`:

```csharp
using DataIngestion.Normalization;

namespace DataIngestion.Tests.Normalization;

public class NonAtomicValueTests
{
    private static DataIngestion.Schema.TabularTable Table(string[] header, params string?[][] rows)
        => RepeatingGroupTests.Table("Orders", header, rows);

    [Fact]
    public void ARepeatedCommaSeparatedListIsNonAtomic()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Tags"],
            ["ORD-1", "rush,gift"],
            ["ORD-2", "rush,fragile"],
            ["ORD-3", "gift,fragile"]));

        var finding = Assert.Single(
            report.Findings.Where(f => f.Kind == NormalizationFindingKind.NonAtomicValues));

        Assert.Equal(new[] { "Tags" }, finding.Columns);
        Assert.Equal("Tag", finding.ProposedEntity);
        Assert.Equal(',', finding.Separator);
        Assert.Contains("Tags", finding.Headline);
    }

    [Theory]
    [InlineData(';')]
    [InlineData('|')]
    public void SemicolonsAndPipesAreSeparatorsToo(char separator)
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Tags"],
            ["ORD-1", $"rush{separator}gift"],
            ["ORD-2", $"rush{separator}fragile"],
            ["ORD-3", $"gift{separator}fragile"]));

        Assert.Equal(separator, report.Findings
            .Single(f => f.Kind == NormalizationFindingKind.NonAtomicValues).Separator);
    }

    /// <summary>
    /// Prose is full of commas. Its split parts never recur, which is the whole test -- without
    /// it every free-text column in every export would be "offered" as an entity.
    /// </summary>
    [Fact]
    public void ProseIsNotNonAtomic_BecauseItsPartsNeverRecur()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Notes"],
            ["ORD-1", "Call the customer, then dispatch"],
            ["ORD-2", "Left with neighbour, signed for"],
            ["ORD-3", "Damaged in transit, refunded"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.NonAtomicValues);
    }

    /// <summary>A separator in a minority of rows is punctuation, not structure.</summary>
    [Fact]
    public void ASeparatorInOnlyOneRowOfManyIsNotStructure()
    {
        var rows = Enumerable.Range(1, 9)
            .Select(i => new string?[] { $"ORD-{i}", "rush" })
            .Append(["ORD-10", "rush,gift"])
            .ToArray();

        var report = NormalizationAnalyzer.Analyze(Table(["Ref", "Tags"], rows));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.NonAtomicValues);
    }

    /// <summary>Only string columns can be non-atomic; a decimal column's "1,50" is a number.</summary>
    [Fact]
    public void ANumericColumnIsNeverNonAtomic()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Total"], ["ORD-1", "9.99"], ["ORD-2", "4.50"], ["ORD-3", "1.00"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.NonAtomicValues);
    }

    [Fact]
    public void EvidenceShowsThePartsThatRecur()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Tags"],
            ["ORD-1", "rush,gift"],
            ["ORD-2", "rush,fragile"],
            ["ORD-3", "gift,fragile"]));

        var finding = report.Findings.Single(
            f => f.Kind == NormalizationFindingKind.NonAtomicValues);

        Assert.Contains(finding.Evidence, line => line.Contains("rush"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~Normalization"
```

Expected: FAIL to compile — `NormalizationAnalyzer` does not exist.

- [ ] **Step 3: Write the finding and report types**

`DataIngestion/Normalization/NormalizationFinding.cs`:

```csharp
namespace DataIngestion.Normalization;

/// <summary>Which normal form a finding is about.</summary>
public enum NormalizationFindingKind
{
    /// <summary>1NF: several columns holding the same kind of thing, numbered.</summary>
    RepeatingGroup,

    /// <summary>1NF: several values packed into one cell.</summary>
    NonAtomicValues,

    /// <summary>2NF/3NF: columns determined by a non-key column.</summary>
    FunctionalDependency,

    /// <summary>3NF: a column computed from others.</summary>
    DerivedColumn
}

/// <summary>
/// One normalization problem found in one table, with the split it proposes and the rows that
/// justify it.
/// </summary>
/// <remarks>
/// <see cref="Evidence"/> is not decoration. A finding the user cannot inspect is a finding they
/// cannot trust, so the `d &lt;n&gt;` verb prints these lines verbatim — the same reason the
/// discovery pipeline carries <c>DecisionRecord.Evidence</c> for its own verdicts.
/// </remarks>
public sealed class NormalizationFinding
{
    public required NormalizationFindingKind Kind { get; init; }

    public required string TableName { get; init; }

    /// <summary>What to call the source in a message ("orders.csv"). Carried here so the audit trail
    /// can name it without <see cref="DataIngestion.DataModelBuilder"/> having to re-find the table
    /// a finding came from.</summary>
    public required string SourceLabel { get; init; }

    /// <summary>The columns this finding is about.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>The entity a split would create. Null for <see cref="NormalizationFindingKind.DerivedColumn"/>,
    /// which removes a column rather than extracting one.</summary>
    public string? ProposedEntity { get; init; }

    /// <summary>One line naming the problem: "Repeating group: Item1, Item2, Item3".</summary>
    public required string Headline { get; init; }

    /// <summary>One line naming the remedy: "split into OrderItem (many per Order)".</summary>
    public required string Proposal { get; init; }

    /// <summary>Rows and values behind the claim, printed by `d &lt;n&gt;`.</summary>
    public required IReadOnlyList<string> Evidence { get; init; }

    /// <summary>The character packing values into one cell. Only set for
    /// <see cref="NormalizationFindingKind.NonAtomicValues"/>, whose split has to re-split on it.</summary>
    public char? Separator { get; init; }
}
```

`DataIngestion/Normalization/NormalizationReport.cs`:

```csharp
namespace DataIngestion.Normalization;

/// <summary>
/// What the analyser found, plus what it could not look for. <paramref name="Notes"/> exists so a
/// check that was skipped says so — a table of three rows cannot be tested for functional
/// dependencies, and silence there would read as "no problems found".
/// </summary>
public sealed record NormalizationReport(
    IReadOnlyList<NormalizationFinding> Findings,
    IReadOnlyList<string> Notes);
```

- [ ] **Step 4: Write the analyser with its two 1NF detections**

`DataIngestion/Normalization/NormalizationAnalyzer.cs`:

```csharp
using DataIngestion.Schema;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion.Normalization;

/// <summary>
/// Looks for the normalization problems a flat export routinely carries, and proposes the split
/// each one implies. Finds and explains only — <see cref="NormalizationPlan"/> is what changes
/// anything, and only for the findings the user picks.
/// </summary>
public static partial class NormalizationAnalyzer
{
    /// <summary>How many rows must show a separator before it is structure rather than
    /// punctuation.</summary>
    public const double NonAtomicShare = 0.6;

    /// <summary>How often a split part must appear across rows to count as a repeated value.
    /// Prose fragments appear once each; a real tag appears again.</summary>
    public const int MinRecurrence = 2;

    /// <summary>Separators that pack several values into one cell, in priority order.</summary>
    public static readonly char[] Separators = [',', ';', '|'];

    /// <summary>How many evidence rows to quote. Enough to see the pattern, not a data dump.</summary>
    public const int EvidenceRows = 3;

    public static NormalizationReport Analyze(TabularTable table)
    {
        var findings = new List<NormalizationFinding>();
        var notes = new List<string>();

        findings.AddRange(RepeatingGroups(table));
        findings.AddRange(NonAtomicColumns(table));
        findings.AddRange(FunctionalDependencies(table, notes));
        findings.AddRange(DerivedColumns(table));

        return new NormalizationReport(findings, notes);
    }

    /// <summary>
    /// Columns sharing a stem and differing only by a trailing number: Item1/Item2/Item3, or
    /// Phone_1/Phone_2. Two or more is a group; one is just a column whose name ends in a digit.
    /// </summary>
    private static IEnumerable<NormalizationFinding> RepeatingGroups(TabularTable table)
    {
        var groups = table.Columns
            .Select(c => (Column: c.Name, Stem: OrdinalStem(c.Name)))
            .Where(x => x.Stem is not null)
            .GroupBy(x => x.Stem!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2);

        // The parent's ENTITY name, not its table name: Orders.csv is about an Order, and both the
        // child's name and the proposal text read wrong otherwise ("OrdersItem", "many per Orders").
        var parent = NameNormalizer.ToUserTypedEntityName(table.Name);

        foreach (var group in groups)
        {
            var columns = group.Select(x => x.Column).ToList();

            // Prefixed with the parent, because "Item" alone collides across domains and says
            // nothing about what it belongs to.
            var entity = parent + NameNormalizer.ToUserTypedEntityName(group.Key);

            yield return new NormalizationFinding
            {
                Kind = NormalizationFindingKind.RepeatingGroup,
                TableName = table.Name,
                SourceLabel = table.Label,
                Columns = columns,
                ProposedEntity = entity,
                Headline = $"Repeating group: {string.Join(", ", columns)}",
                Proposal = $"split into {entity} (many per {parent})",
                Evidence = RowEvidence(table, columns),
            };
        }
    }

    /// <summary>
    /// The stem of a name ending in digits, with any separator before them removed:
    /// "Item1" -> "Item", "Phone_1" -> "Phone". Null when the name does not end in a digit, or
    /// when nothing is left once the digits are stripped.
    /// </summary>
    private static string? OrdinalStem(string columnName)
    {
        var end = columnName.Length;
        while (end > 0 && char.IsDigit(columnName[end - 1]))
            end--;

        if (end == columnName.Length)
            return null;

        var stem = columnName[..end].TrimEnd('_', '-', ' ');
        return stem.Length == 0 ? null : stem;
    }

    /// <summary>
    /// A string column packing several values into one cell. Two conditions, and the second is
    /// what keeps prose out: most rows must carry the separator, AND some split part must appear
    /// in more than one row. A Notes column full of commas splits into fragments that never recur.
    /// </summary>
    private static IEnumerable<NormalizationFinding> NonAtomicColumns(TabularTable table)
    {
        foreach (var column in table.Columns.Where(c => c.Type == "string"))
        {
            var values = table.ColumnValues(column.Name)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList();

            if (values.Count == 0)
                continue;

            foreach (var separator in Separators)
            {
                var withSeparator = values.Count(v => v.Contains(separator));
                if (withSeparator < values.Count * NonAtomicShare)
                    continue;

                var parts = values
                    .SelectMany(v => v.Split(separator))
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();

                var recurring = parts
                    .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() >= MinRecurrence)
                    .Select(g => g.Key)
                    .ToList();

                if (recurring.Count == 0)
                    continue;   // prose: commas everywhere, no repeated value

                var entity = NameNormalizer.ToUserTypedEntityName(column.Name);

                yield return new NormalizationFinding
                {
                    Kind = NormalizationFindingKind.NonAtomicValues,
                    TableName = table.Name,
                    SourceLabel = table.Label,
                    Columns = [column.Name],
                    ProposedEntity = entity,
                    Separator = separator,
                    Headline =
                        $"Non-atomic: \"{column.Name}\" holds {SeparatorName(separator)}-separated values",
                    Proposal = $"split into {entity}",
                    Evidence =
                    [
                        $"{withSeparator} of {values.Count} rows contain '{separator}'.",
                        "Values that appear in more than one row: "
                            + string.Join(", ", recurring.Take(EvidenceRows * 2)),
                    ],
                };

                break;   // one separator per column; the first that matches is the one
            }
        }
    }

    private static string SeparatorName(char separator) => separator switch
    {
        ',' => "comma",
        ';' => "semicolon",
        '|' => "pipe",
        _ => separator.ToString(),
    };

    /// <summary>The first few rows' values for <paramref name="columns"/>, as printable lines.</summary>
    private static List<string> RowEvidence(TabularTable table, IReadOnlyList<string> columns)
    {
        var lines = new List<string> { "  " + string.Join(" | ", columns) };

        for (var row = 0; row < Math.Min(EvidenceRows, table.Rows.Count); row++)
        {
            lines.Add("  " + string.Join(" | ",
                columns.Select(c => table.Value(row, c) ?? "(none)")));
        }

        return lines;
    }
}
```

`NormalizationAnalyzer` is declared `partial` so Task 8 can add `FunctionalDependencies` and `DerivedColumns` in their own file, keeping each detection where its tests are. Add a temporary second file now so this task compiles on its own — Task 8 replaces its bodies:

`DataIngestion/Normalization/NormalizationAnalyzer.Dependencies.cs`:

```csharp
using DataIngestion.Schema;

namespace DataIngestion.Normalization;

public static partial class NormalizationAnalyzer
{
    private static IEnumerable<NormalizationFinding> FunctionalDependencies(
        TabularTable table, List<string> notes)
        => [];

    private static IEnumerable<NormalizationFinding> DerivedColumns(TabularTable table)
        => [];
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~Normalization"
```

Expected: PASS, 13 tests.

- [ ] **Step 6: Commit**

```bash
git add DataIngestion DataIngestion.Tests
git commit -m "feat(data): detect repeating groups and non-atomic columns"
```

---

## Task 8: Normalization findings — the 2NF/3NF detections

**Files:**
- Modify: `DataIngestion/Normalization/NormalizationAnalyzer.Dependencies.cs` (replace the two stubs)
- Create: `DataIngestion.Tests/Normalization/FunctionalDependencyTests.cs`, `DataIngestion.Tests/Normalization/DerivedColumnTests.cs`

**Interfaces:**
- Consumes: `NormalizationFinding`, `NormalizationReport`, `TabularTable`, `RepeatingGroupTests.Table` (the shared test helper), `NormalizationAnalyzer.EvidenceRows`.
- Produces: real bodies for `FunctionalDependencies(TabularTable, List<string> notes)` and `DerivedColumns(TabularTable)`; constants `MinRowsForDependencies` (5) and `MaxDeterminantShare` (0.5); `NormalizationAnalyzer.TooFewRowsNote` (the exact note text).

- [ ] **Step 1: Write the failing tests**

`DataIngestion.Tests/Normalization/FunctionalDependencyTests.cs`:

```csharp
using DataIngestion.Normalization;

namespace DataIngestion.Tests.Normalization;

public class FunctionalDependencyTests
{
    private static DataIngestion.Schema.TabularTable Table(string[] header, params string?[][] rows)
        => RepeatingGroupTests.Table("Orders", header, rows);

    /// <summary>Two columns determined by CustomerName, which is not the key. Extract Customer.</summary>
    [Fact]
    public void ColumnsDeterminedByANonKeyColumnAreAPartialDependency()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity", "CustomerPostCode"],
            ["ORD-1", "Ada", "London", "E1"],
            ["ORD-2", "Grace", "York", "YO1"],
            ["ORD-3", "Ada", "London", "E1"],
            ["ORD-4", "Grace", "York", "YO1"],
            ["ORD-5", "Ada", "London", "E1"],
            ["ORD-6", "Grace", "York", "YO1"]));

        var finding = Assert.Single(
            report.Findings.Where(f => f.Kind == NormalizationFindingKind.FunctionalDependency));

        Assert.Equal("CustomerName", finding.Columns[0]);
        Assert.Contains("CustomerCity", finding.Columns);
        Assert.Contains("CustomerPostCode", finding.Columns);
        Assert.Equal("Customer", finding.ProposedEntity);
        Assert.Contains("Order references it", finding.Proposal);
    }

    /// <summary>
    /// A single dependent is only reported when the two columns share a name prefix. Otherwise any
    /// column that happens to determine one other column would be flagged, and the report becomes
    /// noise the user learns to skip.
    /// </summary>
    [Fact]
    public void OneDependentIsReportedWhenTheNamesShareAPrefix()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity"],
            ["ORD-1", "Ada", "London"],
            ["ORD-2", "Grace", "York"],
            ["ORD-3", "Ada", "London"],
            ["ORD-4", "Grace", "York"],
            ["ORD-5", "Ada", "London"]));

        Assert.Contains(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
    }

    [Fact]
    public void OneDependentWithUnrelatedNamesIsNotReported()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Status", "Colour"],
            ["ORD-1", "open", "red"],
            ["ORD-2", "shut", "blue"],
            ["ORD-3", "open", "red"],
            ["ORD-4", "shut", "blue"],
            ["ORD-5", "open", "red"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
    }

    /// <summary>
    /// A near-unique column determines everything trivially -- that is what being a key means, and
    /// extracting an entity per row is not normalization.
    /// </summary>
    [Fact]
    public void ANearUniqueColumnIsNotADeterminant()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "RefName", "Other"],
            ["ORD-1", "a", "1"],
            ["ORD-2", "b", "2"],
            ["ORD-3", "c", "3"],
            ["ORD-4", "d", "4"],
            ["ORD-5", "e", "5"],
            ["ORD-6", "f", "6"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
    }

    /// <summary>One counter-example kills the dependency: Ada must map to one city on EVERY row.</summary>
    [Fact]
    public void OneContradictingRowMeansThereIsNoDependency()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity"],
            ["ORD-1", "Ada", "London"],
            ["ORD-2", "Grace", "York"],
            ["ORD-3", "Ada", "Leeds"],
            ["ORD-4", "Grace", "York"],
            ["ORD-5", "Ada", "London"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
    }

    /// <summary>
    /// Three rows cannot evidence a dependency -- any two columns will agree by coincidence. The
    /// analyser says so rather than reporting nothing, which would read as "no problems found".
    /// </summary>
    [Fact]
    public void TooFewRowsIsReportedAsANote_NotAsSilence()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity"],
            ["ORD-1", "Ada", "London"],
            ["ORD-2", "Grace", "York"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
        Assert.Contains(NormalizationAnalyzer.TooFewRowsNote, report.Notes);
    }

    [Fact]
    public void EvidenceShowsEachDeterminantValueAndWhatItMapsTo()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity"],
            ["ORD-1", "Ada", "London"],
            ["ORD-2", "Grace", "York"],
            ["ORD-3", "Ada", "London"],
            ["ORD-4", "Grace", "York"],
            ["ORD-5", "Ada", "London"]));

        var finding = report.Findings.Single(
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);

        Assert.Contains(finding.Evidence, line => line.Contains("Ada") && line.Contains("London"));
    }
}
```

`DataIngestion.Tests/Normalization/DerivedColumnTests.cs`:

```csharp
using DataIngestion.Normalization;

namespace DataIngestion.Tests.Normalization;

public class DerivedColumnTests
{
    private static DataIngestion.Schema.TabularTable Table(string[] header, params string?[][] rows)
        => RepeatingGroupTests.Table("Orders", header, rows);

    [Fact]
    public void AProductOfTwoColumnsIsDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Qty", "UnitPrice", "Total"],
            ["2", "5.00", "10.00"],
            ["3", "2.50", "7.50"],
            ["4", "1.25", "5.00"],
            ["5", "2.00", "10.00"],
            ["6", "0.50", "3.00"]));

        var finding = Assert.Single(
            report.Findings.Where(f => f.Kind == NormalizationFindingKind.DerivedColumn));

        Assert.Equal(new[] { "Total" }, finding.Columns);
        Assert.Null(finding.ProposedEntity);
        Assert.Contains("Qty", finding.Headline);
        Assert.Contains("UnitPrice", finding.Headline);
    }

    [Fact]
    public void ASumOfTwoColumnsIsDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Net", "Tax", "Gross"],
            ["10.00", "2.00", "12.00"],
            ["20.00", "4.00", "24.00"],
            ["5.00", "1.00", "6.00"],
            ["8.00", "1.60", "9.60"],
            ["3.00", "0.60", "3.60"]));

        Assert.Contains(report.Findings, f =>
            f.Kind == NormalizationFindingKind.DerivedColumn && f.Columns[0] == "Gross");
    }

    /// <summary>One row that does not add up means the column is not derived -- it holds data.</summary>
    [Fact]
    public void OneRowThatDoesNotAddUpMeansItIsNotDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Net", "Tax", "Gross"],
            ["10.00", "2.00", "12.00"],
            ["20.00", "4.00", "24.00"],
            ["5.00", "1.00", "6.00"],
            ["8.00", "1.60", "9.60"],
            ["3.00", "0.60", "99.00"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.DerivedColumn);
    }

    /// <summary>Coincidence protection: with all-zero columns everything is both sum and product
    /// of everything, and reporting that is worse than reporting nothing.</summary>
    [Fact]
    public void AllZeroColumnsAreNotReportedAsDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["A", "B", "C"],
            ["0", "0", "0"],
            ["0", "0", "0"],
            ["0", "0", "0"],
            ["0", "0", "0"],
            ["0", "0", "0"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.DerivedColumn);
    }

    [Fact]
    public void NonNumericColumnsAreNeverDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["A", "B", "C"],
            ["x", "y", "z"],
            ["p", "q", "r"],
            ["s", "t", "u"],
            ["v", "w", "a"],
            ["b", "c", "d"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.DerivedColumn);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~FunctionalDependencyTests|FullyQualifiedName~DerivedColumnTests"
```

Expected: FAIL — the stubs return nothing, so every finding assertion fails and `TooFewRowsNote` does not exist (compile error). Both are the correct red.

- [ ] **Step 3: Replace the stubs with the real detections**

`DataIngestion/Normalization/NormalizationAnalyzer.Dependencies.cs` (whole file):

```csharp
using System.Globalization;
using DataIngestion.Schema;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion.Normalization;

/// <summary>
/// The 2NF/3NF half of the analyser. Separate file from the 1NF half because these two
/// detections reason about relationships BETWEEN columns over all rows, where those two reason
/// about one column or one set of names.
/// </summary>
public static partial class NormalizationAnalyzer
{
    /// <summary>
    /// Fewest rows a dependency claim can be made from. Below this, any two columns agree by
    /// coincidence and the "finding" is an artefact of the sample size.
    /// </summary>
    public const int MinRowsForDependencies = 5;

    /// <summary>
    /// A determinant may not have more distinct values than this share of the rows. A near-unique
    /// column determines every other column trivially — that is what being a key means — and
    /// extracting one entity per row is not normalization.
    /// </summary>
    public const double MaxDeterminantShare = 0.5;

    /// <summary>Tolerance for a derived-column match, in absolute terms. Money is rounded at the
    /// source, so an exact equality test would reject real derivations.</summary>
    public const decimal DerivedTolerance = 0.01m;

    /// <summary>Said out loud when the table is too short to test dependencies on. Silence here
    /// would read as "no problems found", which is a different claim.</summary>
    public const string TooFewRowsNote =
        "Too few rows to test dependencies — a partial-dependency check needs at least "
        + "5 rows to tell a real pattern from a coincidence.";

    /// <summary>
    /// Columns functionally determined by another, non-key column: every value of the determinant
    /// maps to exactly one value of each dependent, across every row.
    /// </summary>
    /// <remarks>
    /// A single dependent needs a second signal — a shared name prefix — because on its own,
    /// "column A determines column B" happens constantly in real data without meaning a missing
    /// entity. Two or more dependents is signal enough on its own.
    /// </remarks>
    private static IEnumerable<NormalizationFinding> FunctionalDependencies(
        TabularTable table, List<string> notes)
    {
        if (table.Rows.Count < MinRowsForDependencies)
        {
            notes.Add(TooFewRowsNote);
            yield break;
        }

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var determinant in table.Columns)
        {
            if (claimed.Contains(determinant.Name) || !IsCandidateDeterminant(table, determinant))
                continue;

            var dependents = table.Columns
                .Where(c => !string.Equals(c.Name, determinant.Name, StringComparison.OrdinalIgnoreCase))
                .Where(c => !claimed.Contains(c.Name))
                .Where(c => Determines(table, determinant.Name, c.Name))
                .Select(c => c.Name)
                .ToList();

            if (dependents.Count == 0)
                continue;

            if (dependents.Count == 1 && SharedPrefix(determinant.Name, dependents[0]) is null)
                continue;

            var stem = SharedPrefix(determinant.Name, dependents[0]) ?? determinant.Name;
            var entity = NameNormalizer.ToUserTypedEntityName(stem);

            var columns = new List<string> { determinant.Name };
            columns.AddRange(dependents);

            foreach (var column in columns)
                claimed.Add(column);

            yield return new NormalizationFinding
            {
                Kind = NormalizationFindingKind.FunctionalDependency,
                TableName = table.Name,
                SourceLabel = table.Label,
                Columns = columns,
                ProposedEntity = entity,
                Headline = $"Partial dependency: {string.Join(", ", columns)}",
                Proposal = "split into " + entity
                    + $" ({NameNormalizer.ToUserTypedEntityName(table.Name)} references it)",
                Evidence = DependencyEvidence(table, determinant.Name, dependents),
            };
        }
    }

    /// <summary>
    /// Whether <paramref name="column"/> could be a determinant: at least two distinct values (one
    /// determines nothing interesting) and not so many that it is effectively a key.
    /// </summary>
    private static bool IsCandidateDeterminant(TabularTable table, TabularColumn column)
    {
        var values = table.ColumnValues(column.Name)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        if (values.Count < table.Rows.Count)
            return false;   // any gap makes the mapping untestable for those rows

        var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return distinct >= 2 && distinct <= table.Rows.Count * MaxDeterminantShare;
    }

    /// <summary>Whether every value of <paramref name="determinant"/> maps to exactly one value of
    /// <paramref name="dependent"/>. A single counter-example is enough to say no.</summary>
    private static bool Determines(TabularTable table, string determinant, string dependent)
    {
        var mapping = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var key = table.Value(row, determinant);
            if (key is null)
                return false;

            var value = table.Value(row, dependent);

            if (mapping.TryGetValue(key, out var seen))
            {
                if (!string.Equals(seen, value, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else
            {
                mapping[key] = value;
            }
        }

        // A dependent that never varies at all carries no information; calling it a dependency
        // would propose an entity with one row.
        return mapping.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2;
    }

    /// <summary>
    /// The leading camel/Pascal segment two column names share ("CustomerName" and "CustomerCity"
    /// share "Customer"), or null when they share none.
    /// </summary>
    private static string? SharedPrefix(string first, string second)
    {
        var a = Segments(first);
        var b = Segments(second);

        if (a.Count == 0 || b.Count == 0
            || !string.Equals(a[0], b[0], StringComparison.OrdinalIgnoreCase))
            return null;

        // Both names must carry something beyond the shared part, or they are the same name.
        return a.Count > 1 && b.Count > 1 ? a[0] : null;
    }

    /// <summary>Camel/Pascal segments of a name. A run of capitals stays whole.</summary>
    private static List<string> Segments(string name)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }

            if (char.IsUpper(c) && current.Length > 0 && !char.IsUpper(current[^1]))
                Flush();

            current.Append(c);
        }

        Flush();
        return segments;

        void Flush()
        {
            if (current.Length > 0)
                segments.Add(current.ToString());
            current.Clear();
        }
    }

    private static List<string> DependencyEvidence(
        TabularTable table, string determinant, IReadOnlyList<string> dependents)
    {
        var lines = new List<string>
        {
            $"  {determinant} -> {string.Join(", ", dependents)}"
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var row = 0; row < table.Rows.Count && seen.Count < EvidenceRows; row++)
        {
            var key = table.Value(row, determinant);
            if (key is null || !seen.Add(key))
                continue;

            var values = dependents.Select(d => table.Value(row, d) ?? "(none)");
            lines.Add($"  {key} always has {string.Join(", ", values)}");
        }

        return lines;
    }

    /// <summary>
    /// A numeric column equal to the product or the sum of two others on every row. Storing it
    /// duplicates what the other two already say, and the two can then disagree with it.
    /// </summary>
    private static IEnumerable<NormalizationFinding> DerivedColumns(TabularTable table)
    {
        var numeric = table.Columns
            .Where(c => c.Type is "int" or "decimal")
            .Select(c => c.Name)
            .ToList();

        if (numeric.Count < 3 || table.Rows.Count < MinRowsForDependencies)
            yield break;

        foreach (var target in numeric)
        {
            var operands = numeric.Where(
                n => !string.Equals(n, target, StringComparison.OrdinalIgnoreCase)).ToList();

            for (var i = 0; i < operands.Count; i++)
            {
                for (var j = i + 1; j < operands.Count; j++)
                {
                    var (left, right) = (operands[i], operands[j]);

                    var operatorName =
                        Matches(table, target, left, right, (a, b) => a * b) ? "x"
                        : Matches(table, target, left, right, (a, b) => a + b) ? "+"
                        : null;

                    if (operatorName is null)
                        continue;

                    yield return new NormalizationFinding
                    {
                        Kind = NormalizationFindingKind.DerivedColumn,
                        TableName = table.Name,
                        SourceLabel = table.Label,
                        Columns = [target],
                        Headline = $"Derived column: \"{target}\" is always {left} {operatorName} {right}",
                        Proposal = $"drop {target} and compute it",
                        Evidence = RowEvidence(table, [target, left, right]),
                    };

                    yield break;   // one explanation per column is enough
                }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="target"/> equals <paramref name="combine"/> of the two operands on
    /// every row, within <see cref="DerivedTolerance"/>. An all-zero set never matches: zero is
    /// both the sum and the product of zeros, and reporting that is worse than reporting nothing.
    /// </summary>
    private static bool Matches(
        TabularTable table,
        string target,
        string left,
        string right,
        Func<decimal, decimal, decimal> combine)
    {
        var sawNonZero = false;

        for (var row = 0; row < table.Rows.Count; row++)
        {
            if (!TryNumber(table.Value(row, target), out var expected)
                || !TryNumber(table.Value(row, left), out var a)
                || !TryNumber(table.Value(row, right), out var b))
                return false;

            if (Math.Abs(expected - combine(a, b)) > DerivedTolerance)
                return false;

            if (expected != 0m)
                sawNonZero = true;
        }

        return sawNonZero;
    }

    private static bool TryNumber(string? text, out decimal value)
        => decimal.TryParse(
            text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~Normalization"
```

Expected: PASS, 25 tests (the 13 from Task 7 plus 12 here).

- [ ] **Step 5: Commit**

```bash
git add DataIngestion DataIngestion.Tests
git commit -m "feat(data): detect partial dependencies and derived columns"
```

---

## Task 9: `NormalizationPlan` — applying the chosen findings

**Files:**
- Create: `DataIngestion/Normalization/NormalizationPlan.cs`
- Create: `DataIngestion.Tests/Normalization/NormalizationPlanTests.cs`, `DataIngestion.Tests/Normalization/NormalizationDecisionTests.cs`
- Modify: `DomainDiscovery/Model/DecisionRecord.cs` (add `DecisionStage.Normalization`)
- Modify: `DomainDiscovery/Model/DecisionReasons.cs` (add the four normalization reasons)
- Modify: `DataIngestion/DataModelBuilder.cs` (recording overload)

**Interfaces:**
- Consumes: `NormalizationFinding`, `NormalizationFindingKind`, `TabularTable`, `TabularColumn`, `ColumnTypeInference`, `JsonTableReader.ParentIdSuffix`, `JsonTableReader.ScalarValueColumn`, `NameNormalizer.ToUserTypedEntityName`, `CandidateDomainModel.Record`.
- Produces:
  - `NormalizationPlan.Apply(IReadOnlyList<TabularTable> tables, IReadOnlyList<NormalizationFinding> chosen) -> IReadOnlyList<TabularTable>`
  - `DecisionStage.Normalization`
  - `DecisionReasons.ExtractedFromARepeatingGroup`, `.ExtractedFromANonAtomicColumn`, `.ExtractedFromAPartialDependency`, `.DroppedAsADerivedColumn`
  - `DataModelBuilder.Build(IReadOnlyList<TabularTable> tables, IReadOnlyList<NormalizationFinding> applied) -> CandidateDomainModel` (the one-argument overload delegates with an empty list)

Children are built from the ORIGINAL table and the new parent is produced afterwards, so no finding can see a table another finding has already edited. That is the whole reason this is one pass and not four.

- [ ] **Step 1: Write the failing test**

`DataIngestion.Tests/Normalization/NormalizationPlanTests.cs`:

```csharp
using DataIngestion.Normalization;
using DataIngestion.Schema;

namespace DataIngestion.Tests.Normalization;

public class NormalizationPlanTests
{
    private static TabularTable Orders(string[] header, params string?[][] rows)
        => RepeatingGroupTests.Table("Orders", header, rows);

    private static IReadOnlyList<TabularTable> Apply(TabularTable table, NormalizationFindingKind kind)
        => NormalizationPlan.Apply(
            [table],
            NormalizationAnalyzer.Analyze(table).Findings.Where(f => f.Kind == kind).ToList());

    [Fact]
    public void ARepeatingGroupBecomesAChildTableAndLeavesTheParent()
    {
        var tables = Apply(
            Orders(["Ref", "Item1", "Item2"],
                ["ORD-1", "Nut", "Bolt"],
                ["ORD-2", "Washer", null]),
            NormalizationFindingKind.RepeatingGroup);

        Assert.Equal(2, tables.Count);

        var parent = tables[0];
        Assert.Equal(new[] { "Ref" }, parent.Columns.Select(c => c.Name));

        var child = tables[1];
        Assert.Equal("OrderItem", child.Name);
        Assert.Equal(new[] { "OrderId", "Item" }, child.Columns.Select(c => c.Name));

        // One row per non-empty cell; the empty Item2 of row 2 contributes nothing.
        Assert.Equal(3, child.Rows.Count);
        Assert.Equal(new[] { "1", "1", "2" },
            child.Rows.Select(r => r[child.IndexOf("OrderId")]));
        Assert.Equal(new[] { "Nut", "Bolt", "Washer" },
            child.Rows.Select(r => r[child.IndexOf("Item")]));
        Assert.Equal("int", child.Columns.Single(c => c.Name == "OrderId").Type);
    }

    [Fact]
    public void ANonAtomicColumnBecomesAChildTableOfItsParts()
    {
        var tables = Apply(
            Orders(["Ref", "Tags"],
                ["ORD-1", "rush,gift"],
                ["ORD-2", "rush,fragile"],
                ["ORD-3", "gift,fragile"]),
            NormalizationFindingKind.NonAtomicValues);

        Assert.Equal(new[] { "Ref" }, tables[0].Columns.Select(c => c.Name));

        var child = tables[1];
        Assert.Equal("Tag", child.Name);
        Assert.Equal(new[] { "OrderId", "Value" }, child.Columns.Select(c => c.Name));
        Assert.Equal(6, child.Rows.Count);
        Assert.Equal("rush", child.Value(0, "Value"));
        Assert.Equal("3", child.Value(5, "OrderId"));
    }

    [Fact]
    public void APartialDependencyBecomesAParentTableTheOriginalReferences()
    {
        var tables = NormalizationPlan.Apply(
            [Orders(["Ref", "CustomerName", "CustomerCity"],
                ["ORD-1", "Ada", "London"],
                ["ORD-2", "Grace", "York"],
                ["ORD-3", "Ada", "London"],
                ["ORD-4", "Grace", "York"],
                ["ORD-5", "Ada", "London"])],
            NormalizationAnalyzer.Analyze(
                Orders(["Ref", "CustomerName", "CustomerCity"],
                    ["ORD-1", "Ada", "London"],
                    ["ORD-2", "Grace", "York"],
                    ["ORD-3", "Ada", "London"],
                    ["ORD-4", "Grace", "York"],
                    ["ORD-5", "Ada", "London"])).Findings);

        var order = tables.Single(t => t.Name == "Orders");
        var customer = tables.Single(t => t.Name == "Customer");

        // The extracted columns leave the parent, replaced by a foreign key.
        Assert.Equal(new[] { "Ref", "CustomerId" }, order.Columns.Select(c => c.Name));
        Assert.Equal("int", order.Columns.Single(c => c.Name == "CustomerId").Type);

        // One row per DISTINCT determinant value, in first-appearance order.
        Assert.Equal(new[] { "CustomerName", "CustomerCity" }, customer.Columns.Select(c => c.Name));
        Assert.Equal(2, customer.Rows.Count);
        Assert.Equal("Ada", customer.Value(0, "CustomerName"));
        Assert.Equal("Grace", customer.Value(1, "CustomerName"));

        // ...and the parent's FK points at the right one.
        Assert.Equal(new[] { "1", "2", "1", "2", "1" },
            order.Rows.Select(r => r[order.IndexOf("CustomerId")]));
    }

    [Fact]
    public void ADerivedColumnIsDroppedWithNoNewTable()
    {
        var tables = Apply(
            Orders(["Qty", "UnitPrice", "Total"],
                ["2", "5.00", "10.00"],
                ["3", "2.50", "7.50"],
                ["4", "1.25", "5.00"],
                ["5", "2.00", "10.00"],
                ["6", "0.50", "3.00"]),
            NormalizationFindingKind.DerivedColumn);

        var parent = Assert.Single(tables);
        Assert.Equal(new[] { "Qty", "UnitPrice" }, parent.Columns.Select(c => c.Name));
    }

    /// <summary>Applying nothing changes nothing -- this is the `k` (keep as-is) path.</summary>
    [Fact]
    public void ApplyingNoFindingsReturnsTheTablesUnchanged()
    {
        var table = Orders(["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"]);

        var result = Assert.Single(NormalizationPlan.Apply([table], []));
        Assert.Equal(new[] { "Ref", "Item1", "Item2" }, result.Columns.Select(c => c.Name));
    }

    /// <summary>A findings list is a subset the user chose, so applying one of two must not
    /// silently apply the other.</summary>
    [Fact]
    public void OnlyTheChosenFindingsAreApplied()
    {
        var table = Orders(["Ref", "Item1", "Item2", "Tags"],
            ["ORD-1", "Nut", "Bolt", "rush,gift"],
            ["ORD-2", "Washer", null, "rush,fragile"],
            ["ORD-3", "Nut", null, "gift,fragile"]);

        var repeating = NormalizationAnalyzer.Analyze(table).Findings
            .Single(f => f.Kind == NormalizationFindingKind.RepeatingGroup);

        var tables = NormalizationPlan.Apply([table], [repeating]);

        Assert.Equal(2, tables.Count);
        Assert.Contains("Tags", tables[0].Columns.Select(c => c.Name));
    }

    /// <summary>A table not named by any finding passes straight through -- JSON yields several
    /// tables and only some of them will have findings.</summary>
    [Fact]
    public void TablesWithNoFindingsPassThroughUntouched()
    {
        var withFinding = Orders(["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"]);
        var other = RepeatingGroupTests.Table("Members", ["Name"], ["Ada"]);

        var tables = NormalizationPlan.Apply(
            [withFinding, other],
            NormalizationAnalyzer.Analyze(withFinding).Findings
                .Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup).ToList());

        Assert.Contains(tables, t => t.Name == "Members" && t.Columns.Count == 1);
    }

    /// <summary>A child's name must not collide with a table already in the run, or the model
    /// builder would drop the second as a duplicate and the split would silently vanish.</summary>
    [Fact]
    public void AChildNameAlreadyTakenIsSuffixedRatherThanColliding()
    {
        var orders = Orders(["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"]);
        var existing = RepeatingGroupTests.Table("OrderItem", ["Sku"], ["A"]);

        var tables = NormalizationPlan.Apply(
            [orders, existing],
            NormalizationAnalyzer.Analyze(orders).Findings
                .Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup).ToList());

        Assert.Contains(tables, t => t.Name == "OrderItem2");
        Assert.Contains(tables, t => t.Name == "OrderItem");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~NormalizationPlanTests"
```

Expected: FAIL to compile — `NormalizationPlan` does not exist.

- [ ] **Step 3: Write the plan**

`DataIngestion/Normalization/NormalizationPlan.cs`:

```csharp
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion.Normalization;

/// <summary>
/// Rewrites the read tables to carry out the findings the user chose. The analyser finds and
/// explains; this is the only thing that changes anything.
/// </summary>
/// <remarks>
/// Every child is built from the ORIGINAL table, and the replacement parent is assembled
/// afterwards from the columns that survived. No finding ever sees a table another finding has
/// already edited, so the result does not depend on the order the user listed them in — which is
/// the bug a straightforward apply-each-in-turn loop would have.
/// </remarks>
public static class NormalizationPlan
{
    /// <summary>
    /// Applies <paramref name="chosen"/> to <paramref name="tables"/>, returning the new table
    /// set: each affected table rewritten, each extracted entity added, and every unaffected
    /// table passed through as it was.
    /// </summary>
    public static IReadOnlyList<TabularTable> Apply(
        IReadOnlyList<TabularTable> tables, IReadOnlyList<NormalizationFinding> chosen)
    {
        var result = new List<TabularTable>();

        // Names already in play, so an extracted entity never collides with a table this run
        // already has — DataModelBuilder would drop the loser as a duplicate and the user's split
        // would disappear without a word.
        var takenNames = new HashSet<string>(
            tables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var findings = chosen
                .Where(f => string.Equals(f.TableName, table.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (findings.Count == 0)
            {
                result.Add(table);
                continue;
            }

            var parentEntity = NameNormalizer.ToUserTypedEntityName(table.Name);
            var foreignKey = parentEntity + JsonTableReader.ParentIdSuffix;

            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var extracted = new List<TabularTable>();

            // Determinant column -> (new FK column name, one FK value per original row).
            var addedKeys = new List<(string Column, string?[] Values)>();

            foreach (var finding in findings)
            {
                switch (finding.Kind)
                {
                    case NormalizationFindingKind.RepeatingGroup:
                        extracted.Add(RepeatingGroupChild(table, finding, foreignKey, takenNames));
                        foreach (var column in finding.Columns)
                            removed.Add(column);
                        break;

                    case NormalizationFindingKind.NonAtomicValues:
                        extracted.Add(NonAtomicChild(table, finding, foreignKey, takenNames));
                        removed.Add(finding.Columns[0]);
                        break;

                    case NormalizationFindingKind.FunctionalDependency:
                        var (entity, keys) = DependencyParent(table, finding, takenNames);
                        extracted.Add(entity);
                        addedKeys.Add((entity.Name + JsonTableReader.ParentIdSuffix, keys));
                        foreach (var column in finding.Columns)
                            removed.Add(column);
                        break;

                    case NormalizationFindingKind.DerivedColumn:
                        removed.Add(finding.Columns[0]);
                        break;
                }
            }

            result.Add(WithoutColumns(table, removed, addedKeys));
            result.AddRange(extracted);
        }

        return result;
    }

    /// <summary>
    /// One child row per non-empty cell across the group's columns, tagged with its parent's
    /// 1-based row number — 1-based because seeded Ids are assigned 1..N in row order.
    /// </summary>
    private static TabularTable RepeatingGroupChild(
        TabularTable table,
        NormalizationFinding finding,
        string foreignKey,
        HashSet<string> takenNames)
    {
        // The value column is named for what the group holds, not for one of its numbered
        // columns: Item1/Item2/Item3 all hold an Item.
        var valueColumn = ValueColumnName(finding.Columns[0]);

        var child = NewChild(finding.ProposedEntity!, table, foreignKey, valueColumn, takenNames);

        for (var row = 0; row < table.Rows.Count; row++)
        {
            foreach (var column in finding.Columns)
            {
                var value = table.Value(row, column);
                if (!string.IsNullOrWhiteSpace(value))
                    child.Rows.Add([(row + 1).ToString(), value]);
            }
        }

        TypeChild(child);
        return child;
    }

    /// <summary>One child row per split part of each cell.</summary>
    private static TabularTable NonAtomicChild(
        TabularTable table,
        NormalizationFinding finding,
        string foreignKey,
        HashSet<string> takenNames)
    {
        var child = NewChild(
            finding.ProposedEntity!, table, foreignKey,
            JsonTableReader.ScalarValueColumn, takenNames);

        var separator = finding.Separator ?? ',';

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var cell = table.Value(row, finding.Columns[0]);
            if (string.IsNullOrWhiteSpace(cell))
                continue;

            foreach (var part in cell.Split(separator))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                    child.Rows.Add([(row + 1).ToString(), trimmed]);
            }
        }

        TypeChild(child);
        return child;
    }

    /// <summary>
    /// The extracted entity for a functional dependency: one row per DISTINCT determinant value,
    /// in first-appearance order, plus the foreign key each original row should carry.
    /// </summary>
    private static (TabularTable Entity, string?[] Keys) DependencyParent(
        TabularTable table, NormalizationFinding finding, HashSet<string> takenNames)
    {
        var entity = new TabularTable
        {
            Name = UniqueName(finding.ProposedEntity!, takenNames),
            SourceLabel = table.Label,
        };

        foreach (var column in finding.Columns)
        {
            entity.Columns.Add(new TabularColumn
            {
                Name = column,
                Type = table.Columns.First(
                    c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)).Type,
            });
        }

        var determinant = finding.Columns[0];
        var rowNumberByValue = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var keys = new string?[table.Rows.Count];

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var key = table.Value(row, determinant);
            if (key is null)
                continue;

            if (!rowNumberByValue.TryGetValue(key, out var number))
            {
                entity.Rows.Add(finding.Columns.Select(c => table.Value(row, c)).ToArray());
                number = entity.Rows.Count;   // 1-based, matching the seeded Id
                rowNumberByValue[key] = number;
            }

            keys[row] = number.ToString();
        }

        return (entity, keys);
    }

    /// <summary>A child table with its foreign key and one value column, and nothing else.</summary>
    private static TabularTable NewChild(
        string proposedName,
        TabularTable table,
        string foreignKey,
        string valueColumn,
        HashSet<string> takenNames)
    {
        var child = new TabularTable
        {
            Name = UniqueName(proposedName, takenNames),
            SourceLabel = table.Label,
        };

        child.Columns.Add(new TabularColumn { Name = foreignKey, Type = "int" });
        child.Columns.Add(new TabularColumn { Name = valueColumn });

        return child;
    }

    /// <summary>The value column's name: the group's stem, with its trailing digits gone.</summary>
    private static string ValueColumnName(string firstColumn)
    {
        var end = firstColumn.Length;
        while (end > 0 && char.IsDigit(firstColumn[end - 1]))
            end--;

        var stem = firstColumn[..end].TrimEnd('_', '-', ' ');
        return NameNormalizer.ToFieldName(stem.Length == 0 ? firstColumn : stem);
    }

    /// <summary>The child's value column is typed from the values it actually received, so a
    /// repeating group of ints becomes an int column rather than defaulting to string.</summary>
    private static void TypeChild(TabularTable child)
    {
        var valueColumn = child.Columns[1];
        valueColumn.Type = ColumnTypeInference.Infer(
            child.ColumnValues(valueColumn.Name), valueColumn.Name);
    }

    /// <summary><paramref name="proposed"/>, or the first numbered variant that is free. Claimed
    /// as a side effect, so two findings in one run cannot pick the same name.</summary>
    private static string UniqueName(string proposed, HashSet<string> takenNames)
    {
        if (takenNames.Add(proposed))
            return proposed;

        for (var n = 2; ; n++)
        {
            var candidate = proposed + n;
            if (takenNames.Add(candidate))
                return candidate;
        }
    }

    /// <summary>The parent as it survives: the columns no finding removed, then the foreign keys
    /// the extracted entities need.</summary>
    private static TabularTable WithoutColumns(
        TabularTable table,
        HashSet<string> removed,
        List<(string Column, string?[] Values)> addedKeys)
    {
        var kept = table.Columns.Where(c => !removed.Contains(c.Name)).ToList();

        var rewritten = new TabularTable { Name = table.Name, SourceLabel = table.SourceLabel };

        foreach (var column in kept)
            rewritten.Columns.Add(new TabularColumn { Name = column.Name, Type = column.Type });

        foreach (var (column, _) in addedKeys)
            rewritten.Columns.Add(new TabularColumn { Name = column, Type = "int" });

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var values = kept.Select(c => table.Value(row, c.Name)).ToList();
            values.AddRange(addedKeys.Select(k => k.Values[row]));
            rewritten.Rows.Add(values.ToArray());
        }

        return rewritten;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~NormalizationPlanTests"
```

Expected: PASS, 8 tests.

- [ ] **Step 5: Write the failing test for the audit trail**

An applied split creates a class the user never named and no article mentioned. `why OrderItem` has
to answer for it, or the model contains entities with no explanation — which is the exact problem
`DecisionRecord` exists to solve.

Add `DataIngestion.Tests/Normalization/NormalizationDecisionTests.cs`:

```csharp
using DataIngestion.Normalization;
using DomainDiscovery.Model;

namespace DataIngestion.Tests.Normalization;

public class NormalizationDecisionTests
{
    private static DataIngestion.Schema.TabularTable Orders()
        => RepeatingGroupTests.Table("Orders",
            ["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"], ["ORD-2", "Washer", null]);

    [Fact]
    public void AnAppliedSplitIsRecordedAgainstTheEntityItCreated()
    {
        var table = Orders();
        var findings = NormalizationAnalyzer.Analyze(table).Findings
            .Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup).ToList();

        var model = DataModelBuilder.Build(NormalizationPlan.Apply([table], findings), findings);

        Assert.Contains(model.Decisions, d =>
            d.Name == "OrderItem"
            && d.Stage == DecisionStage.Normalization
            && d.Outcome == DecisionOutcome.Kept
            && d.Reason == DecisionReasons.ExtractedFromARepeatingGroup);
    }

    /// <summary>The evidence has to name the columns, or `why` says a class exists without saying
    /// what in the data put it there.</summary>
    [Fact]
    public void TheRecordsEvidenceNamesTheColumnsItCameFrom()
    {
        var table = Orders();
        var findings = NormalizationAnalyzer.Analyze(table).Findings
            .Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup).ToList();

        var record = DataModelBuilder
            .Build(NormalizationPlan.Apply([table], findings), findings)
            .Decisions.First(d => d.Stage == DecisionStage.Normalization);

        Assert.Contains("Item1", record.Evidence);
        Assert.Contains("Item2", record.Evidence);
        Assert.Contains("orders.csv", record.Evidence);
    }

    [Theory]
    [InlineData(NormalizationFindingKind.NonAtomicValues, DecisionReasons.ExtractedFromANonAtomicColumn)]
    [InlineData(NormalizationFindingKind.FunctionalDependency, DecisionReasons.ExtractedFromAPartialDependency)]
    public void EachKindHasItsOwnReason(NormalizationFindingKind kind, string reason)
    {
        var table = RepeatingGroupTests.Table("Orders",
            ["Ref", "Tags", "CustomerName", "CustomerCity"],
            ["ORD-1", "rush,gift", "Ada", "London"],
            ["ORD-2", "rush,fragile", "Grace", "York"],
            ["ORD-3", "gift,fragile", "Ada", "London"],
            ["ORD-4", "rush,gift", "Grace", "York"],
            ["ORD-5", "gift,rush", "Ada", "London"]);

        var findings = NormalizationAnalyzer.Analyze(table).Findings
            .Where(f => f.Kind == kind).ToList();

        Assert.NotEmpty(findings);

        var model = DataModelBuilder.Build(NormalizationPlan.Apply([table], findings), findings);

        Assert.Contains(model.Decisions, d => d.Reason == reason);
    }

    /// <summary>A dropped derived column is a removal, not an extraction, so it is recorded as one
    /// -- and against the column's own name, which is what a user would ask `why` about.</summary>
    [Fact]
    public void ADroppedDerivedColumnIsRecordedAsRemoved()
    {
        var table = RepeatingGroupTests.Table("Orders",
            ["Qty", "UnitPrice", "Total"],
            ["2", "5.00", "10.00"], ["3", "2.50", "7.50"], ["4", "1.25", "5.00"],
            ["5", "2.00", "10.00"], ["6", "0.50", "3.00"]);

        var findings = NormalizationAnalyzer.Analyze(table).Findings
            .Where(f => f.Kind == NormalizationFindingKind.DerivedColumn).ToList();

        var model = DataModelBuilder.Build(NormalizationPlan.Apply([table], findings), findings);

        Assert.Contains(model.Decisions, d =>
            d.Name == "Total"
            && d.Stage == DecisionStage.Normalization
            && d.Outcome == DecisionOutcome.Rejected
            && d.Reason == DecisionReasons.DroppedAsADerivedColumn);
    }

    /// <summary>Applying nothing records nothing about normalization -- the `k` path leaves no trail
    /// claiming a change was made.</summary>
    [Fact]
    public void KeepingTheDataAsItIsRecordsNoNormalizationDecision()
    {
        var table = Orders();

        var model = DataModelBuilder.Build(NormalizationPlan.Apply([table], []), []);

        Assert.DoesNotContain(model.Decisions, d => d.Stage == DecisionStage.Normalization);
    }
}
```

Run it:

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~NormalizationDecisionTests"
```

Expected: FAIL to compile — `DecisionStage.Normalization`, the four reason constants, and the
two-argument `DataModelBuilder.Build` do not exist.

- [ ] **Step 6: Add the stage and the reasons**

In `DomainDiscovery/Model/DecisionRecord.cs`, add a value to `DecisionStage` after `Shaping`:

```csharp
    /// <summary>
    /// Restructuring a data source the user supplied. A normalization split creates a class nobody
    /// named and no article mentioned, so `why` needs a trail for it just as much as for a
    /// harvested noun — arguably more, since the user did not ask for it by name.
    /// </summary>
    Normalization,
```

`ReviewLoop` must stay last so existing persisted values keep their meaning; inserting before it is
fine because nothing serializes this enum by number.

In `DomainDiscovery/Model/DecisionReasons.cs`, add after the data-ingestion block from Task 6:

```csharp
    // --- Normalization ---
    public const string ExtractedFromARepeatingGroup = "extracted from a repeating group";
    public const string ExtractedFromANonAtomicColumn = "extracted from a column holding several values";
    public const string ExtractedFromAPartialDependency = "extracted from a partial dependency";
    public const string DroppedAsADerivedColumn = "dropped: it is computed from other columns";
```

- [ ] **Step 7: Add the recording overload to `DataModelBuilder`**

In `DataIngestion/DataModelBuilder.cs`, make the existing `Build` delegate and add the overload:

```csharp
    public static CandidateDomainModel Build(IReadOnlyList<TabularTable> tables)
        => Build(tables, []);

    /// <summary>
    /// As <see cref="Build(IReadOnlyList{TabularTable})"/>, additionally recording why each
    /// normalization split exists. <paramref name="applied"/> is what the user chose at the
    /// normalization prompt — the findings that were carried out, not everything found.
    /// </summary>
    public static CandidateDomainModel Build(
        IReadOnlyList<TabularTable> tables,
        IReadOnlyList<Normalization.NormalizationFinding> applied)
    {
        // ... the existing body, unchanged, up to and including AddRelationships(model, entityNames);

        RecordNormalization(model, applied);
        return model;
    }

    /// <summary>
    /// One record per applied finding, filed under the name a user would ask about: the entity a
    /// split created, or the column a drop removed.
    /// </summary>
    private static void RecordNormalization(
        CandidateDomainModel model, IReadOnlyList<Normalization.NormalizationFinding> applied)
    {
        foreach (var finding in applied)
        {
            var evidence =
                $"{string.Join(", ", finding.Columns)} in {finding.TableName} "
                + $"({finding.SourceLabel})";

            switch (finding.Kind)
            {
                case Normalization.NormalizationFindingKind.RepeatingGroup:
                    model.Record(finding.ProposedEntity!, DecisionStage.Normalization,
                        DecisionOutcome.Kept, DecisionReasons.ExtractedFromARepeatingGroup, evidence);
                    break;

                case Normalization.NormalizationFindingKind.NonAtomicValues:
                    model.Record(finding.ProposedEntity!, DecisionStage.Normalization,
                        DecisionOutcome.Kept, DecisionReasons.ExtractedFromANonAtomicColumn, evidence);
                    break;

                case Normalization.NormalizationFindingKind.FunctionalDependency:
                    model.Record(finding.ProposedEntity!, DecisionStage.Normalization,
                        DecisionOutcome.Kept, DecisionReasons.ExtractedFromAPartialDependency, evidence);
                    break;

                case Normalization.NormalizationFindingKind.DerivedColumn:
                    // A drop, not an extraction — and filed under the COLUMN, since that is the name
                    // a user would ask `why` about when it is missing from their model.
                    model.Record(finding.Columns[0], DecisionStage.Normalization,
                        DecisionOutcome.Rejected, DecisionReasons.DroppedAsADerivedColumn, evidence);
                    break;
            }
        }
    }
```

`finding.SourceLabel` is already carried by `NormalizationFinding` (Task 7) and set at all four
construction sites in `NormalizationAnalyzer`, so the audit trail can name the file without the
model builder having to re-find the table a finding came from.

- [ ] **Step 8: Pass the applied findings from the CLI**

This changes the call added in Task 16. When implementing Task 16, use:

```csharp
        var model = DataModelBuilder.Build(tables, chosen);
```

If Task 16 is already done, change it there now.

- [ ] **Step 9: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: PASS, including the 6 new decision tests. `DecisionReport` renders the new stage without
change — it reads `DecisionRecord` generically — so `why OrderItem` now answers for a split entity.

- [ ] **Step 10: Commit**

```bash
git add DataIngestion DataIngestion.Tests DomainDiscovery
git commit -m "feat(data): apply normalization findings and record why each split exists"
```

---

## Task 10: The normalization prompt — `n` / `n <ids>` / `k` / `d <n>`

**Files:**
- Create: `DataIngestion/Normalization/NormalizationPrompt.cs`
- Create: `DataIngestion.Tests/Normalization/NormalizationPromptTests.cs`

**Interfaces:**
- Consumes: `NormalizationReport`, `NormalizationFinding`.
- Produces:
  - `NormalizationPrompt.Lines(NormalizationReport report, string label, int rowCount) -> IReadOnlyList<string>`
  - `NormalizationPrompt.Choose(NormalizationReport report, TextReader reader, TextWriter writer) -> IReadOnlyList<NormalizationFinding>`

Split in two so the display is pure and assertable, and only the loop needs a reader — the same shape `BuildAppCommand.ResolveTarget` uses, and for the same reason.

- [ ] **Step 1: Write the failing test**

`DataIngestion.Tests/Normalization/NormalizationPromptTests.cs`:

```csharp
using DataIngestion.Normalization;

namespace DataIngestion.Tests.Normalization;

public class NormalizationPromptTests
{
    private static NormalizationFinding Finding(string headline, string proposal, string evidence)
        => new()
        {
            Kind = NormalizationFindingKind.RepeatingGroup,
            TableName = "Orders",
            SourceLabel = "orders.csv",
            Columns = ["Item1", "Item2"],
            ProposedEntity = "OrderItem",
            Headline = headline,
            Proposal = proposal,
            Evidence = [evidence],
        };

    private static NormalizationReport Report(params NormalizationFinding[] findings)
        => new(findings, []);

    private static NormalizationReport ThreeFindings() => Report(
        Finding("Repeating group: Item1, Item2", "split into OrderItem", "Nut | Bolt"),
        Finding("Partial dependency: CustomerName, CustomerCity", "split into Customer", "Ada -> London"),
        Finding("Non-atomic: \"Tags\"", "split into Tag", "rush appears 4 times"));

    [Fact]
    public void Lines_NumberEachFindingAndShowItsProposal()
    {
        var text = string.Join("\n", NormalizationPrompt.Lines(ThreeFindings(), "Orders.csv", 500));

        Assert.Contains("Normalization check on Orders.csv (500 rows)", text);
        Assert.Contains("1 ! Repeating group: Item1, Item2", text);
        Assert.Contains("-> split into OrderItem", text);
        Assert.Contains("3 ! Non-atomic:", text);
    }

    /// <summary>Evidence belongs behind `d`, not in the summary -- the summary has to stay
    /// scannable.</summary>
    [Fact]
    public void Lines_DoNotIncludeTheEvidence()
        => Assert.DoesNotContain("Nut | Bolt",
            string.Join("\n", NormalizationPrompt.Lines(ThreeFindings(), "Orders.csv", 500)));

    /// <summary>A skipped check must say so. Printing nothing would read as "no problems found",
    /// which is a different claim.</summary>
    [Fact]
    public void Lines_ShowTheNotes()
    {
        var report = new NormalizationReport([], [NormalizationAnalyzer.TooFewRowsNote]);

        Assert.Contains(NormalizationAnalyzer.TooFewRowsNote,
            string.Join("\n", NormalizationPrompt.Lines(report, "Orders.csv", 3)));
    }

    [Fact]
    public void Lines_SayNothingWasFoundWhenNothingWas()
        => Assert.Contains("No normalization problems found",
            string.Join("\n", NormalizationPrompt.Lines(new NormalizationReport([], []), "t.csv", 9)));

    [Fact]
    public void N_AppliesEveryFinding()
    {
        var report = ThreeFindings();

        var chosen = NormalizationPrompt.Choose(report, new StringReader("n\n"), new StringWriter());

        Assert.Equal(3, chosen.Count);
    }

    [Fact]
    public void N_WithNumbers_AppliesOnlyThose()
    {
        var report = ThreeFindings();

        var chosen = NormalizationPrompt.Choose(report, new StringReader("n 1 3\n"), new StringWriter());

        Assert.Equal(2, chosen.Count);
        Assert.Same(report.Findings[0], chosen[0]);
        Assert.Same(report.Findings[2], chosen[1]);
    }

    [Fact]
    public void K_AppliesNothing()
        => Assert.Empty(NormalizationPrompt.Choose(
            ThreeFindings(), new StringReader("k\n"), new StringWriter()));

    /// <summary>A finding the user cannot inspect is a finding they cannot trust, so `d` prints the
    /// rows behind it and then asks again.</summary>
    [Fact]
    public void D_PrintsThatFindingsEvidenceThenAsksAgain()
    {
        var writer = new StringWriter();

        var chosen = NormalizationPrompt.Choose(
            ThreeFindings(), new StringReader("d 2\nk\n"), writer);

        Assert.Contains("Ada -> London", writer.ToString());
        Assert.DoesNotContain("Nut | Bolt", writer.ToString());
        Assert.Empty(chosen);
    }

    [Theory]
    [InlineData("d 9")]
    [InlineData("d 0")]
    [InlineData("d x")]
    [InlineData("d")]
    public void D_WithABadNumber_SaysSoAndAsksAgain(string line)
    {
        var writer = new StringWriter();

        NormalizationPrompt.Choose(ThreeFindings(), new StringReader(line + "\nk\n"), writer);

        Assert.Contains("no finding", writer.ToString());
    }

    [Fact]
    public void UnrecognizedInput_SaysSoAndAsksAgain()
    {
        var writer = new StringWriter();

        var chosen = NormalizationPrompt.Choose(
            ThreeFindings(), new StringReader("what?\nn\n"), writer);

        Assert.Contains("Unrecognized", writer.ToString());
        Assert.Equal(3, chosen.Count);
    }

    /// <summary>An out-of-range number in `n` is reported rather than silently ignored, so the
    /// user is never told a split was applied that was not.</summary>
    [Fact]
    public void N_WithAnOutOfRangeNumber_SaysSoAndAsksAgain()
    {
        var writer = new StringWriter();

        NormalizationPrompt.Choose(ThreeFindings(), new StringReader("n 1 9\nk\n"), writer);

        Assert.Contains("no finding", writer.ToString());
    }

    /// <summary>Closed stdin must not busy-loop. The review loop already treats EOF as cancel;
    /// here the safe reading is "change nothing".</summary>
    [Fact]
    public void ClosedInput_KeepsTheDataAsItIs()
        => Assert.Empty(NormalizationPrompt.Choose(
            ThreeFindings(), TextReader.Null, new StringWriter()));

    /// <summary>With nothing to decide there is nothing to ask.</summary>
    [Fact]
    public void NoFindings_NeverPrompts()
    {
        var reader = new StringReader("n\n");

        Assert.Empty(NormalizationPrompt.Choose(
            new NormalizationReport([], []), reader, new StringWriter()));

        // The reader was not consumed, so the next thing that reads stdin still sees "n".
        Assert.Equal("n", reader.ReadLine());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~NormalizationPromptTests"
```

Expected: FAIL to compile — `NormalizationPrompt` does not exist.

- [ ] **Step 3: Write the prompt**

`DataIngestion/Normalization/NormalizationPrompt.cs`:

```csharp
namespace DataIngestion.Normalization;

/// <summary>
/// Shows what the analyser found and asks what to do about it. Rendering is separate from the
/// loop so the display can be asserted as a value and only the loop needs a reader — the shape
/// <c>BuildAppCommand.ResolveTarget</c> established, for the same reason: a method that reads the
/// console cannot be driven from a test.
/// </summary>
public static class NormalizationPrompt
{
    /// <summary>
    /// The summary block: one numbered line per finding with its proposal, then any notes. The
    /// evidence is deliberately NOT here — it is what `d` is for, and inlining it would bury the
    /// list the user has to scan.
    /// </summary>
    public static IReadOnlyList<string> Lines(
        NormalizationReport report, string label, int rowCount)
    {
        var lines = new List<string>
        {
            string.Empty,
            $"Normalization check on {label} ({rowCount} rows):",
            string.Empty,
        };

        if (report.Findings.Count == 0)
        {
            lines.Add("  No normalization problems found.");
        }
        else
        {
            for (var i = 0; i < report.Findings.Count; i++)
            {
                lines.Add($"  {i + 1} ! {report.Findings[i].Headline}");
                lines.Add($"        -> {report.Findings[i].Proposal}");
            }
        }

        // A check that could not run says so. Silence here would read as "nothing to report".
        foreach (var note in report.Notes)
        {
            lines.Add(string.Empty);
            lines.Add("  " + note);
        }

        return lines;
    }

    /// <summary>
    /// Asks which findings to apply and returns them, in the order the analyser found them.
    /// Returns empty for `k`, for closed input, and when there is nothing to decide.
    /// </summary>
    public static IReadOnlyList<NormalizationFinding> Choose(
        NormalizationReport report, TextReader reader, TextWriter writer)
    {
        // Nothing to decide, so nothing is asked — and nothing is read, which matters: the caller's
        // reader is shared with the review loop that runs next.
        if (report.Findings.Count == 0)
            return [];

        while (true)
        {
            writer.WriteLine();
            writer.Write("  [n]ormalize   n <1 3> subset   [k]eep as-is   [d]etail <n>: ");

            var input = reader.ReadLine();

            // Closed stdin. The safe reading is "change nothing" — never a busy loop, and never
            // an unasked-for rewrite of the user's data.
            if (input is null)
                return [];

            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            switch (parts[0].ToLowerInvariant())
            {
                case "k":
                case "keep":
                    return [];

                case "n":
                case "normalize":
                    if (parts.Length == 1)
                        return report.Findings;

                    if (TryResolve(report, parts.Skip(1), writer, out var subset))
                        return subset;

                    break;

                case "d":
                case "detail":
                    if (TryResolve(report, parts.Skip(1), writer, out var detail)
                        && detail.Count == 1)
                    {
                        foreach (var line in detail[0].Evidence)
                            writer.WriteLine(line);
                    }
                    else if (parts.Length < 2)
                    {
                        writer.WriteLine($"  Usage: d <1-{report.Findings.Count}>. "
                                         + "There is no finding without a number.");
                    }

                    break;

                default:
                    writer.WriteLine("  Unrecognized. Type n, n with numbers, k, or d <n>.");
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves 1-based finding numbers. Reports anything out of range rather than dropping it:
    /// silently ignoring `n 1 9` would tell the user two splits were applied when one was.
    /// </summary>
    private static bool TryResolve(
        NormalizationReport report,
        IEnumerable<string> tokens,
        TextWriter writer,
        out IReadOnlyList<NormalizationFinding> resolved)
    {
        var chosen = new List<NormalizationFinding>();
        var indices = new SortedSet<int>();

        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var number)
                || number < 1
                || number > report.Findings.Count)
            {
                writer.WriteLine(
                    $"  There is no finding '{token}'. Valid: 1-{report.Findings.Count}.");
                resolved = [];
                return false;
            }

            indices.Add(number);
        }

        if (indices.Count == 0)
        {
            resolved = [];
            return false;
        }

        // Analyser order, not the order the user typed the numbers in, so the applied set is
        // deterministic however it was asked for.
        foreach (var index in indices)
            chosen.Add(report.Findings[index - 1]);

        resolved = chosen;
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~NormalizationPromptTests"
```

Expected: PASS, 16 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion DataIngestion.Tests
git commit -m "feat(data): normalization prompt with inspectable findings"
```

---

## Task 11: `InlineDataSource` — the scriptable `from <format> <location>` form

**Files:**
- Create: `DataIngestion/InlineDataSource.cs`
- Create: `DataIngestion.Tests/InlineDataSourceTests.cs`

**Interfaces:**
- Consumes: `DataSourceKind`, `DataSourceSpec`, `DataSourceReaders.TryParseKind`.
- Produces:
  - `sealed record InlineBuildRequest(string Description, DataSourceSpec? Source, bool Normalize)`
  - `InlineDataSource.Parse(string tail) -> InlineBuildRequest`
  - `const string NormalizeKeyword = "normalize"`

This is what makes a data-backed build scriptable, and therefore what the end-to-end test drives. Redirected stdin never prompts, so without this there would be no way to test the data path end to end.

- [ ] **Step 1: Write the failing test**

`DataIngestion.Tests/InlineDataSourceTests.cs`:

```csharp
namespace DataIngestion.Tests;

public class InlineDataSourceTests
{
    [Fact]
    public void SplitsTheDescriptionFromTheSource()
    {
        var request = InlineDataSource.Parse(@"order tracking from csv D:\data\orders.csv");

        Assert.Equal("order tracking", request.Description);
        Assert.NotNull(request.Source);
        Assert.Equal(DataSourceKind.Csv, request.Source!.Kind);
        Assert.Equal(@"D:\data\orders.csv", request.Source.Location);
        Assert.False(request.Normalize);
    }

    [Fact]
    public void ADescriptionIsOptional()
    {
        var request = InlineDataSource.Parse(@"from csv D:\orders.csv");

        Assert.Equal(string.Empty, request.Description);
        Assert.Equal(DataSourceKind.Csv, request.Source!.Kind);
    }

    /// <summary>
    /// The LAST `from` followed by a format keyword wins, so a description that legitimately
    /// contains the word "from" is not truncated. This is the case that makes naive splitting
    /// on the first "from" wrong.
    /// </summary>
    [Fact]
    public void ADescriptionMayContainTheWordFrom()
    {
        var request = InlineDataSource.Parse(@"track orders from suppliers from csv D:\orders.csv");

        Assert.Equal("track orders from suppliers", request.Description);
        Assert.Equal(@"D:\orders.csv", request.Source!.Location);
    }

    /// <summary>`from` followed by something that is not a format is just prose.</summary>
    [Fact]
    public void FromSomethingThatIsNotAFormatIsNotASource()
    {
        var request = InlineDataSource.Parse("a system built from scratch");

        Assert.Null(request.Source);
        Assert.Equal("a system built from scratch", request.Description);
    }

    [Fact]
    public void NoFromAtAllIsJustADescription()
    {
        var request = InlineDataSource.Parse("a library that lends books to members");

        Assert.Null(request.Source);
        Assert.Equal("a library that lends books to members", request.Description);
    }

    [Fact]
    public void TrailingNormalizeIsAFlag_NotPartOfThePath()
    {
        var request = InlineDataSource.Parse(@"orders from csv D:\orders.csv normalize");

        Assert.True(request.Normalize);
        Assert.Equal(@"D:\orders.csv", request.Source!.Location);
    }

    /// <summary>Windows paths contain spaces, so the location is the rest of the line, not one token.</summary>
    [Fact]
    public void APathWithSpacesIsKeptWhole()
    {
        var request = InlineDataSource.Parse(@"orders from csv C:\My Data\orders 2026.csv");

        Assert.Equal(@"C:\My Data\orders 2026.csv", request.Source!.Location);
    }

    [Fact]
    public void AQuotedPathLosesItsQuotes()
    {
        var request = InlineDataSource.Parse(@"orders from csv ""C:\My Data\orders.csv"" normalize");

        Assert.Equal(@"C:\My Data\orders.csv", request.Source!.Location);
        Assert.True(request.Normalize);
    }

    [Theory]
    [InlineData("csv", DataSourceKind.Csv)]
    [InlineData("txt", DataSourceKind.Txt)]
    [InlineData("json", DataSourceKind.Json)]
    [InlineData("excel", DataSourceKind.Excel)]
    [InlineData("sql", DataSourceKind.Sql)]
    public void EveryFormatKeywordIsRecognized(string keyword, DataSourceKind expected)
    {
        var request = InlineDataSource.Parse($@"orders from {keyword} D:\x");

        Assert.Equal(expected, request.Source!.Kind);
    }

    /// <summary>A connection string contains '=' and ';' and no spaces around them; it is the
    /// location, kept verbatim.</summary>
    [Fact]
    public void ASqlConnectionStringIsTheLocation()
    {
        var request = InlineDataSource.Parse(
            "shop from sql Server=.;Database=Shop;Trusted_Connection=True");

        Assert.Equal(DataSourceKind.Sql, request.Source!.Kind);
        Assert.Equal("Server=.;Database=Shop;Trusted_Connection=True", request.Source.Location);
    }

    /// <summary>`from csv` with nothing after it names no file, so it is not a source. Treating it
    /// as one would produce a spec with an empty path and a confusing not-found error.</summary>
    [Fact]
    public void AFormatWithNoLocationIsNotASource()
    {
        var request = InlineDataSource.Parse("orders from csv");

        Assert.Null(request.Source);
        Assert.Equal("orders from csv", request.Description);
    }

    [Fact]
    public void EmptyInputIsAnEmptyDescription()
    {
        var request = InlineDataSource.Parse("   ");

        Assert.Null(request.Source);
        Assert.Equal(string.Empty, request.Description);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~InlineDataSourceTests"
```

Expected: FAIL to compile — `InlineDataSource` does not exist.

- [ ] **Step 3: Write the parser**

`DataIngestion/InlineDataSource.cs`:

```csharp
using DataIngestion.Schema.Readers;

namespace DataIngestion;

/// <summary>What a `build app` line asked for: a description, and optionally a data source.</summary>
public sealed record InlineBuildRequest(string Description, DataSourceSpec? Source, bool Normalize);

/// <summary>
/// Parses the scriptable form, <c>&lt;description&gt; from &lt;format&gt; &lt;location&gt;
/// [normalize]</c>.
/// </summary>
/// <remarks>
/// This form exists because redirected stdin never prompts — the precedent
/// <c>BuildAppCommand.ResolveTarget</c> set, and what every existing piped script and E2E test
/// depends on. Without an inline form there would be no way to drive the data path from a script
/// at all, so this is what the end-to-end test uses.
/// </remarks>
public static class InlineDataSource
{
    /// <summary>Opts a non-interactive run into applying every normalization finding. Absent, a
    /// scripted run reports the findings and changes nothing, so scripts stay deterministic.</summary>
    public const string NormalizeKeyword = "normalize";

    public static InlineBuildRequest Parse(string tail)
    {
        var text = (tail ?? string.Empty).Trim();
        if (text.Length == 0)
            return new InlineBuildRequest(string.Empty, null, false);

        // Scan RIGHT to LEFT for `from <format>`. A description may legitimately contain the word
        // "from" ("track orders from suppliers"), and the leftmost match would swallow the rest of
        // it as a location.
        var tokens = Tokenize(text);

        for (var i = tokens.Count - 2; i >= 0; i--)
        {
            if (!string.Equals(tokens[i].Text, "from", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!DataSourceReaders.TryParseKind(tokens[i + 1].Text, out var kind))
                continue;

            var description = text[..tokens[i].Start].Trim();

            // The location is the REST OF THE LINE, not one token: Windows paths contain spaces.
            var locationStart = tokens[i + 1].Start + tokens[i + 1].Text.Length;
            var location = text[locationStart..].Trim();

            var normalize = false;
            if (location.EndsWith(NormalizeKeyword, StringComparison.OrdinalIgnoreCase)
                && location.Length > NormalizeKeyword.Length
                && char.IsWhiteSpace(location[^(NormalizeKeyword.Length + 1)]))
            {
                normalize = true;
                location = location[..^NormalizeKeyword.Length].Trim();
            }

            location = Unquote(location);

            // `from csv` with nothing after it names no file. Returning a spec with an empty
            // location would surface as a baffling "no file at ''" instead of being read as prose.
            if (location.Length == 0)
                continue;

            return new InlineBuildRequest(
                description, new DataSourceSpec(kind, location), normalize);
        }

        return new InlineBuildRequest(text, null, false);
    }

    private static string Unquote(string text)
        => text.Length >= 2 && text[0] == '"' && text[^1] == '"'
            ? text[1..^1]
            : text;

    /// <summary>Whitespace-separated tokens with the offset each starts at, so a description can be
    /// cut from the ORIGINAL text and keep its spacing.</summary>
    private static List<(string Text, int Start)> Tokenize(string text)
    {
        var tokens = new List<(string, int)>();
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            var isSpace = i == text.Length || char.IsWhiteSpace(text[i]);

            if (!isSpace && start < 0)
                start = i;
            else if (isSpace && start >= 0)
            {
                tokens.Add((text[start..i], start));
                start = -1;
            }
        }

        return tokens;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~InlineDataSourceTests"
```

Expected: PASS, 16 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion DataIngestion.Tests
git commit -m "feat(data): parse the inline from <format> <location> form"
```

---

## Task 12: `DataSourceInterview` — the prompts

**Files:**
- Create: `DataIngestion/DataSourceInterview.cs`
- Create: `DataIngestion.Tests/DataSourceInterviewTests.cs`

**Interfaces:**
- Consumes: `DataSourceKind`, `DataSourceSpec`, `DataSourceReaders.Keys`, `.TryParseKind`, `.TryCreate`, `.NotSupportedMessage`.
- Produces:
  - `enum InterviewDecision { NoData, Source, Cancelled }`
  - `sealed record InterviewOutcome(InterviewDecision Decision, DataSourceSpec? Source)`
  - `DataSourceInterview.Resolve(TextReader reader, TextWriter writer) -> InterviewOutcome`

`Cancelled` is distinct from `NoData` on purpose: a user who abandons the interview must not silently get a Wikipedia build they never asked for.

The SQL branch stops at the not-supported message in this branch, so the connection / database / tables prompts land with `SqlServerReader` — there is nothing for them to feed yet, and writing them now would be three prompts that lead to a refusal.

- [ ] **Step 1: Write the failing test**

`DataIngestion.Tests/DataSourceInterviewTests.cs`:

```csharp
namespace DataIngestion.Tests;

public class DataSourceInterviewTests
{
    private static string TempCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Name\n1,Ada\n");
        return path;
    }

    private static InterviewOutcome Run(string input, out string written)
    {
        var writer = new StringWriter();
        var outcome = DataSourceInterview.Resolve(new StringReader(input), writer);
        written = writer.ToString();
        return outcome;
    }

    [Theory]
    [InlineData("n\n")]
    [InlineData("N\n")]
    [InlineData("no\n")]
    [InlineData("\n")]   // blank takes the default, which is No
    public void SayingNo_MeansNoData(string input)
    {
        var outcome = Run(input, out var written);

        Assert.Equal(InterviewDecision.NoData, outcome.Decision);
        Assert.Null(outcome.Source);
        Assert.Contains("Do you have existing data?", written);
    }

    [Fact]
    public void SayingYesThenPickingCsv_AsksForThePathAndReturnsIt()
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\ncsv\n{path}\n", out var written);

            Assert.Equal(InterviewDecision.Source, outcome.Decision);
            Assert.Equal(DataSourceKind.Csv, outcome.Source!.Kind);
            Assert.Equal(path, outcome.Source.Location);
            Assert.Contains("sql/excel/txt/json/csv", written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The menu offers all five formats, so a user is never left wondering whether Excel
    /// was considered.</summary>
    [Fact]
    public void TheFormatPromptOffersEveryFormat()
    {
        Run("y\nc\n", out var written);

        foreach (var key in Schema.Readers.DataSourceReaders.Keys)
            Assert.Contains(key, written);
    }

    [Fact]
    public void AnUnknownFormat_ReAsksRatherThanFailing()
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\npostgres\ncsv\n{path}\n", out var written);

            Assert.Equal(InterviewDecision.Source, outcome.Decision);
            Assert.Contains("Unknown format", written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Excel and SQL are on the menu but have no reader here. The user gets the reason and
    /// the alternative, then another go -- not a crash and not a silent fallback.</summary>
    [Theory]
    [InlineData("excel")]
    [InlineData("sql")]
    public void AFormatWithNoReader_SaysSoAndReAsks(string format)
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\n{format}\ncsv\n{path}\n", out var written);

            Assert.Equal(InterviewDecision.Source, outcome.Decision);
            Assert.Contains("not in this build yet", written);
            Assert.Contains("csv", written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A mistyped path is the most likely thing to go wrong here, so it re-asks -- the same
    /// way ResolveTarget re-asks on an unknown target key.</summary>
    [Fact]
    public void AMissingFile_ReAsksForThePath()
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\ncsv\nD:\\nope\\missing.csv\n{path}\n", out var written);

            Assert.Equal(InterviewDecision.Source, outcome.Decision);
            Assert.Equal(path, outcome.Source!.Location);
            Assert.Contains("No file at", written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AQuotedPathLosesItsQuotes()
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\ncsv\n\"{path}\"\n", out _);

            Assert.Equal(path, outcome.Source!.Location);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Cancelling is NOT the same as having no data: a user who backs out must not be
    /// handed a Wikipedia build they never asked for.</summary>
    [Theory]
    [InlineData("y\nc\n")]
    [InlineData("y\ncancel\n")]
    [InlineData("y\ncsv\nc\n")]
    public void Cancelling_IsDistinctFromHavingNoData(string input)
    {
        var outcome = Run(input, out _);

        Assert.Equal(InterviewDecision.Cancelled, outcome.Decision);
        Assert.Null(outcome.Source);
    }

    /// <summary>Closed stdin must not busy-loop at any of the three prompts.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("y\n")]
    [InlineData("y\ncsv\n")]
    public void ClosedInput_Cancels(string input)
        => Assert.Equal(InterviewDecision.Cancelled, Run(input, out _).Decision);
}
```

Note the first `ClosedInput_Cancels` case: an immediately-closed reader at the *first* prompt cancels rather than returning `NoData`. A blank line is a deliberate "take the default"; EOF is the absence of an answer, and the two must not be conflated.

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DataSourceInterviewTests"
```

Expected: FAIL to compile — `DataSourceInterview` does not exist.

- [ ] **Step 3: Write the interview**

`DataIngestion/DataSourceInterview.cs`:

```csharp
using DataIngestion.Schema.Readers;

namespace DataIngestion;

/// <summary>What the interview concluded.</summary>
public enum InterviewDecision
{
    /// <summary>The user has no data. The caller runs the unchanged Wikipedia path.</summary>
    NoData,

    /// <summary>The user named a source. <see cref="InterviewOutcome.Source"/> holds it.</summary>
    Source,

    /// <summary>
    /// The user backed out, or stdin closed mid-interview. Deliberately not
    /// <see cref="NoData"/>: someone who abandons the interview must not be handed a Wikipedia
    /// build they never asked for.
    /// </summary>
    Cancelled
}

public sealed record InterviewOutcome(InterviewDecision Decision, DataSourceSpec? Source);

/// <summary>
/// Asks whether the user has data, in what format, and where. Reads and writes through the
/// <see cref="TextReader"/>/<see cref="TextWriter"/> it is given rather than the console, so the
/// whole flow is drivable from a test — the pattern <c>BuildAppCommand.ResolveTarget</c>
/// established for exactly this reason.
/// </summary>
/// <remarks>
/// The caller decides whether to run this at all: redirected stdin never prompts. See
/// <see cref="InlineDataSource"/> for the scriptable form.
/// </remarks>
public static class DataSourceInterview
{
    private static readonly InterviewOutcome Cancelled = new(InterviewDecision.Cancelled, null);

    public static InterviewOutcome Resolve(TextReader reader, TextWriter writer)
    {
        writer.Write("Do you have existing data? [y/N]: ");
        var answer = reader.ReadLine();

        // EOF is the absence of an answer, not the default answer. Conflating the two would run a
        // whole discovery pipeline off a closed pipe.
        if (answer is null)
            return Cancelled;

        var trimmed = answer.Trim();

        if (trimmed.Length == 0
            || trimmed.StartsWith("n", StringComparison.OrdinalIgnoreCase))
            return new InterviewOutcome(InterviewDecision.NoData, null);

        return ResolveFormat(reader, writer);
    }

    private static InterviewOutcome ResolveFormat(TextReader reader, TextWriter writer)
    {
        while (true)
        {
            writer.Write($"Format [{string.Join("/", DataSourceReaders.Keys)}] (c to cancel): ");
            var line = reader.ReadLine();

            if (line is null)
                return Cancelled;

            var text = line.Trim();

            if (IsCancel(text))
                return Cancelled;

            if (!DataSourceReaders.TryParseKind(text, out var kind))
            {
                writer.WriteLine(
                    "  Unknown format. Valid: " + string.Join(", ", DataSourceReaders.Keys));
                continue;
            }

            // On the menu, but with no reader in this build. Say why, say what works, ask again.
            if (!DataSourceReaders.TryCreate(kind, out _))
            {
                writer.WriteLine("  " + DataSourceReaders.NotSupportedMessage(kind));
                continue;
            }

            return ResolveLocation(kind, reader, writer);
        }
    }

    private static InterviewOutcome ResolveLocation(
        DataSourceKind kind, TextReader reader, TextWriter writer)
    {
        while (true)
        {
            writer.Write($"{kind.ToString().ToUpperInvariant()} file path (c to cancel): ");
            var line = reader.ReadLine();

            if (line is null)
                return Cancelled;

            var text = line.Trim();

            if (IsCancel(text))
                return Cancelled;

            if (text.Length == 0)
                continue;

            // A path pasted from Explorer arrives quoted.
            var path = text.Length >= 2 && text[0] == '"' && text[^1] == '"'
                ? text[1..^1]
                : text;

            // A mistyped path is the likeliest thing to go wrong here, so re-ask rather than
            // abort — the same way ResolveTarget re-asks on an unknown target key.
            if (!File.Exists(path))
            {
                writer.WriteLine($"  No file at {path}.");
                continue;
            }

            return new InterviewOutcome(
                InterviewDecision.Source, new DataSourceSpec(kind, path));
        }
    }

    private static bool IsCancel(string text)
        => string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
           || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DataSourceInterviewTests"
```

Expected: PASS, 17 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion DataIngestion.Tests
git commit -m "feat(data): the data-source interview"
```

---

## Task 13: `ImportedData` — the rows, capped and accounted for

**Files:**
- Create: `AppScaffolding/ImportedData.cs`
- Create: `DataIngestion/TabularImport.cs`
- Modify: `DataIngestion/DataIngestion.csproj` (add a reference to `AppScaffolding`)
- Create: `DataIngestion.Tests/TabularImportTests.cs`
- Modify: `DataIngestion.Tests/DataIngestion.Tests.csproj` (add a reference to `AppScaffolding`)

**Interfaces:**
- Consumes: `TabularTable`, `NameNormalizer.ToUserTypedEntityName`, `.ToFieldName`.
- Produces:
  - `AppScaffolding.ImportedData` — `const int MaxSeedRows = 1000`; `IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> Rows`; `IReadOnlyList<string> Notes`; `bool IsEmpty`; `IReadOnlyList<IReadOnlyDictionary<string, string?>> For(string entityName)`
  - `DataIngestion.TabularImport.ToImportedData(IReadOnlyList<TabularTable> tables) -> ImportedData`

`ImportedData` lives in `AppScaffolding` because it is an *input to the scaffolder*, alongside `PersistenceKind` — and because putting it there keeps it free of any `TabularTable` dependency, so a future SQL reader feeds the same carrier. `DataIngestion` gains a one-way reference to `AppScaffolding` for it; there is no cycle, since `AppScaffolding` knows nothing of `DataIngestion`.

- [ ] **Step 1: Add the project references**

In `DataIngestion/DataIngestion.csproj` and `DataIngestion.Tests/DataIngestion.Tests.csproj`, add inside the existing `ProjectReference` `ItemGroup`:

```xml
    <ProjectReference Include="..\AppScaffolding\AppScaffolding.csproj" />
```

- [ ] **Step 2: Write the failing test**

`DataIngestion.Tests/TabularImportTests.cs`:

```csharp
using AppScaffolding;
using DataIngestion.Schema;

namespace DataIngestion.Tests;

public class TabularImportTests
{
    private static TabularTable Table(string name, string[] header, params string?[][] rows)
    {
        var table = new TabularTable { Name = name, SourceLabel = name + ".csv" };
        foreach (var column in header)
            table.Columns.Add(new TabularColumn { Name = column });

        foreach (var row in rows)
            table.Rows.Add(row);

        return table;
    }

    [Fact]
    public void RowsAreKeyedByEntityNameAndFieldName()
    {
        var data = TabularImport.ToImportedData(
            [Table("Orders", ["order ref", "Qty"], ["ORD-1", "2"], ["ORD-2", "3"])]);

        var rows = data.For("Order");

        Assert.Equal(2, rows.Count);

        // Keys are the FIELD names the model will carry, not the raw column headers -- otherwise
        // the seed file's keys would not match the generated class's properties.
        Assert.Equal("ORD-1", rows[0]["OrderRef"]);
        Assert.Equal("3", rows[1]["Qty"]);
    }

    [Fact]
    public void AnEntityWithNoImportedRowsIsAbsent()
    {
        var data = TabularImport.ToImportedData([Table("Orders", ["Ref"], ["ORD-1"])]);

        Assert.Empty(data.For("Customer"));
        Assert.False(data.IsEmpty);
    }

    [Fact]
    public void NoTablesIsEmpty()
        => Assert.True(TabularImport.ToImportedData([]).IsEmpty);

    [Fact]
    public void NullCellsSurviveAsNulls()
    {
        var data = TabularImport.ToImportedData([Table("Orders", ["Ref", "Note"], ["ORD-1", null])]);

        Assert.Null(data.For("Order")[0]["Note"]);
    }

    /// <summary>
    /// A 48,000-row export must not become a 48,000-row JSON blob inside the generated project.
    /// The cap is real, and it is SAID -- silent truncation reads as "all your data is in there".
    /// </summary>
    [Fact]
    public void RowsAreCappedAndTheCapIsStated()
    {
        var rows = Enumerable.Range(1, ImportedData.MaxSeedRows + 500)
            .Select(i => new string?[] { $"ORD-{i}" })
            .ToArray();

        var data = TabularImport.ToImportedData([Table("Orders", ["Ref"], rows)]);

        Assert.Equal(ImportedData.MaxSeedRows, data.For("Order").Count);
        Assert.Contains(data.Notes, n =>
            n.Contains(ImportedData.MaxSeedRows.ToString())
            && n.Contains((ImportedData.MaxSeedRows + 500).ToString()));
    }

    [Fact]
    public void ARowCountUnderTheCapProducesNoNote()
        => Assert.Empty(TabularImport.ToImportedData(
            [Table("Orders", ["Ref"], ["ORD-1"])]).Notes);

    /// <summary>Every table becomes its own entry, so a JSON document's child tables are imported
    /// too.</summary>
    [Fact]
    public void EveryTableIsImported()
    {
        var data = TabularImport.ToImportedData(
        [
            Table("Orders", ["Ref"], ["ORD-1"]),
            Table("OrderItem", ["OrderId", "Item"], ["1", "Nut"]),
        ]);

        Assert.Single(data.For("Order"));
        Assert.Single(data.For("OrderItem"));
    }

    /// <summary>Entity names match DataModelBuilder's -- both singularize the table name, and a
    /// mismatch would silently leave the seed file unmatched to any class.</summary>
    [Fact]
    public void EntityNamesAreSingularized_MatchingTheModelBuilder()
    {
        var tables = new[] { Table("Categories", ["Name"], ["Fiction"]) };

        var entity = DataModelBuilder.Build(tables).Entities.Single().Name;

        Assert.Equal(entity, Assert.Single(TabularImport.ToImportedData(tables).Rows).Key);
    }
}
```

- [ ] **Step 3: Write the carrier**

`AppScaffolding/ImportedData.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// The rows a user's data source contributed, keyed by entity name and then by field name. An
/// input to <see cref="AppScaffolder"/> alongside <see cref="PersistenceKind"/>.
/// </summary>
/// <remarks>
/// Values stay as raw strings. They are converted to the field's declared type when the seed file
/// is written, which is the one place that knows the FINAL field list — after the review loop has
/// had its say. Converting earlier would fix a type the user is still free to change.
/// </remarks>
public sealed class ImportedData
{
    /// <summary>
    /// Most rows imported per entity. A large export would otherwise become a large JSON file
    /// inside the generated project, committed and diffed forever, when its purpose is to give the
    /// app something to show on first run.
    /// </summary>
    public const int MaxSeedRows = 1000;

    public static ImportedData None { get; } =
        new(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>(), []);

    public ImportedData(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> rows,
        IReadOnlyList<string> notes)
    {
        Rows = rows;
        Notes = notes;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> Rows
    { get; }

    /// <summary>
    /// What the caller must tell the user — above all, that rows were dropped. A cap the user is
    /// not told about reads as "all your data is in there", which is a false claim about their data.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The rows for <paramref name="entityName"/>, or none. Never null: an entity the user
    /// added by hand in the review loop has no imported rows and that is not an error.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string?>> For(string entityName)
        => Rows.TryGetValue(entityName, out var rows) ? rows : [];
}
```

- [ ] **Step 4: Write the converter**

`DataIngestion/TabularImport.cs`:

```csharp
using AppScaffolding;
using DataIngestion.Schema;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion;

/// <summary>
/// Turns read tables into the row set the scaffolder seeds from. Keys are normalized the same way
/// <see cref="DataModelBuilder"/> normalizes them, so every seed row's keys match the properties
/// of the class built from the same table.
/// </summary>
public static class TabularImport
{
    public static ImportedData ToImportedData(IReadOnlyList<TabularTable> tables)
    {
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>(
            StringComparer.OrdinalIgnoreCase);

        var notes = new List<string>();

        foreach (var table in tables)
        {
            var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);
            if (entityName.Length == 0 || rows.ContainsKey(entityName))
                continue;   // DataModelBuilder skipped this table for the same reason

            var imported = new List<IReadOnlyDictionary<string, string?>>();

            foreach (var row in table.Rows.Take(ImportedData.MaxSeedRows))
            {
                var cells = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                foreach (var column in table.Columns)
                {
                    var fieldName = NameNormalizer.ToFieldName(column.Name);
                    if (fieldName.Length > 0)
                        cells[fieldName] = table.Value(table.Rows.IndexOf(row), column.Name);
                }

                imported.Add(cells);
            }

            if (table.Rows.Count > ImportedData.MaxSeedRows)
            {
                notes.Add(
                    $"Imported the first {ImportedData.MaxSeedRows} of {table.Rows.Count} rows "
                    + $"from {table.Label} as seed data.");
            }

            rows[entityName] = imported;
        }

        return new ImportedData(rows, notes);
    }
}
```

> **Note for the implementer:** `table.Rows.IndexOf(row)` above is O(n²) and wrong for duplicate
> rows — `List.IndexOf` returns the *first* equal element. Replace the inner loop with an index:
>
> ```csharp
> for (var rowIndex = 0;
>      rowIndex < Math.Min(table.Rows.Count, ImportedData.MaxSeedRows);
>      rowIndex++)
> {
>     var cells = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
>     foreach (var column in table.Columns)
>     {
>         var fieldName = NameNormalizer.ToFieldName(column.Name);
>         if (fieldName.Length > 0)
>             cells[fieldName] = table.Value(rowIndex, column.Name);
>     }
>     imported.Add(cells);
> }
> ```
>
> Write it this way from the start; the version above is shown only so the defect is named rather
> than silently inherited.

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~TabularImportTests"
```

Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add DataIngestion DataIngestion.Tests AppScaffolding
git commit -m "feat(data): carry imported rows, capped and reported"
```

---

## Task 14: `SeedFileWriter` — imported rows as seed JSON, projected through the final model

**Files:**
- Modify: `AppScaffolding/DomainModelMapper.cs` (expose the entity-name → class-name map)
- Create: `AppScaffolding/SeedFileWriter.cs`
- Create: `CodeGenerator.Tests/App/SeedFileWriterTests.cs`

**Interfaces:**
- Consumes: `ClassDefinition`, `PropertyDefinition` (`CodeGenerationEngine.Models`), `ImportedData`, `CandidateDomainModel`.
- Produces:
  - `DomainModelMapper.ToClassDefinitions(CandidateDomainModel model, out IReadOnlyDictionary<string, string> classNames) -> IReadOnlyList<ClassDefinition>` (the existing one-argument overload delegates to it)
  - `SeedFileWriter.SeedFolderName` (`const string` = `"Seed"`)
  - `SeedFileWriter.Files(IReadOnlyList<ClassDefinition> classes, IReadOnlyDictionary<string, string> classNames, ImportedData data) -> IReadOnlyList<(string FileName, string Json)>`

**This task is where the review-loop interaction is resolved.** The writer iterates each class's **final property list**, not the data's keys, so a `remove`, `rename`, `field` or `unfield` in the review loop is absorbed automatically: a dropped field simply stops being written, and no seed file can name a property the generated class does not declare.

Tests live in `CodeGenerator.Tests` (MSTest) because it is the only test project that sees both `AppScaffolding` and `CodeGenerationEngine.Models`.

- [ ] **Step 1: Expose the class-name map**

In `AppScaffolding/DomainModelMapper.cs`, change the signature and add a delegating overload. `classNames` is already built inside the method; this only returns it.

```csharp
    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(CandidateDomainModel model)
        => ToClassDefinitions(model, out _);

    /// <summary>
    /// As <see cref="ToClassDefinitions(CandidateDomainModel)"/>, also yielding the entity-name to
    /// class-name map this method already builds.
    /// </summary>
    /// <remarks>
    /// <see cref="SeedFileWriter"/> needs it: imported rows are keyed by ENTITY name, seed files are
    /// named for the CLASS, and <see cref="ToUniqueClassName"/> can rename one ("Type" ->
    /// "TypeItem"). Without the map a renamed entity's rows would be written to a file no class ever
    /// reads, and the app would start empty with nothing to explain why.
    /// </remarks>
    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(
        CandidateDomainModel model, out IReadOnlyDictionary<string, string> classNames)
    {
        // ... existing body unchanged, except: the local `classNames` dictionary is assigned to the
        // out parameter immediately before `return classes;`
    }
```

The existing body declares `var classNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);`. Rename that local to `names`, keep every use, and end the method with:

```csharp
        AddNavigationProperties(model, classes, names);
        AddAuditFields(classes);
        AddValidation(classes);

        classNames = names;
        return classes;
```

- [ ] **Step 2: Write the failing test**

`CodeGenerator.Tests/App/SeedFileWriterTests.cs`:

```csharp
using System.Text.Json;
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SeedFileWriterTests
{
    private static ClassDefinition Order(params (string Name, string Type)[] properties)
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        foreach (var (name, type) in properties)
            definition.Properties.Add(new PropertyDefinition { Name = name, Type = type });

        return definition;
    }

    private static ImportedData Data(params Dictionary<string, string?>[] rows)
        => new(
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>
            {
                ["Order"] = rows.Cast<IReadOnlyDictionary<string, string?>>().ToList(),
            },
            []);

    private static Dictionary<string, string> Map() => new() { ["Order"] = "Order" };

    private static JsonElement[] Rows(string json)
        => JsonDocument.Parse(json).RootElement.EnumerateArray().ToArray();

    [TestMethod]
    public void WritesOneFilePerEntityNamedAfterItsClass()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"))], Map(), Data(new() { ["Ref"] = "ORD-1" }));

        var file = files.Single();
        Assert.AreEqual("Order.json", file.FileName);
        Assert.AreEqual(1, Rows(file.Json).Length);
    }

    /// <summary>Ids are assigned 1..N in row order, which is what the foreign keys the readers and
    /// NormalizationPlan produced already point at.</summary>
    [TestMethod]
    public void IdsAreAssignedOneToNInRowOrder()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"))], Map(),
            Data(new() { ["Ref"] = "a" }, new() { ["Ref"] = "b" }, new() { ["Ref"] = "c" }));

        Assert.AreEqual(
            "1,2,3",
            string.Join(",", Rows(files.Single().Json).Select(r => r.GetProperty("Id").GetInt32())));
    }

    [TestMethod]
    public void ValuesAreWrittenAsTheirDeclaredJsonTypes()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"), ("Qty", "int"), ("Total", "decimal"),
                   ("Paid", "bool"), ("Placed", "DateTime"))],
            Map(),
            Data(new()
            {
                ["Ref"] = "ORD-1",
                ["Qty"] = "2",
                ["Total"] = "9.99",
                ["Paid"] = "yes",
                ["Placed"] = "2026-01-15",
            }));

        var row = Rows(files.Single().Json)[0];

        Assert.AreEqual(JsonValueKind.String, row.GetProperty("Ref").ValueKind);
        Assert.AreEqual(2, row.GetProperty("Qty").GetInt32());
        Assert.AreEqual(9.99m, row.GetProperty("Total").GetDecimal());
        Assert.IsTrue(row.GetProperty("Paid").GetBoolean());
        Assert.AreEqual(new DateTime(2026, 1, 15), row.GetProperty("Placed").GetDateTime());
    }

    /// <summary>
    /// THE review-loop interaction. The writer walks the CLASS's properties, so a field the user
    /// removed simply stops being written -- the seed file can never name a property the generated
    /// class does not declare, which would not compile in the generated tests and would throw at
    /// load.
    /// </summary>
    [TestMethod]
    public void AFieldTheUserRemovedIsNotWritten()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"))],   // Qty was removed in the review loop
            Map(),
            Data(new() { ["Ref"] = "ORD-1", ["Qty"] = "2" }));

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Qty", out _));
    }

    /// <summary>A field the user ADDED has no imported value, so it is omitted and the class's
    /// default stands.</summary>
    [TestMethod]
    public void AFieldWithNoImportedValueIsOmitted()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"), ("Courier", "string"))],
            Map(),
            Data(new() { ["Ref"] = "ORD-1" }));

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Courier", out _));
    }

    /// <summary>A value that does not parse as its declared type is omitted, not emitted as a
    /// broken literal. The user changed the type in the review loop and the data disagrees; the
    /// row still loads.</summary>
    [TestMethod]
    public void AValueThatDoesNotParseAsItsTypeIsOmitted()
    {
        var files = SeedFileWriter.Files(
            [Order(("Qty", "int"))], Map(), Data(new() { ["Qty"] = "several" }));

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Qty", out _));
    }

    [TestMethod]
    public void ANullValueIsOmitted()
    {
        var files = SeedFileWriter.Files(
            [Order(("Ref", "string"), ("Note", "string"))],
            Map(),
            Data(new() { ["Ref"] = "ORD-1", ["Note"] = null }));

        Assert.IsFalse(Rows(files.Single().Json)[0].TryGetProperty("Note", out _));
    }

    /// <summary>Navigation properties are resolved from the foreign key at load time. Writing them
    /// would make Order -> Customer -> Orders a reference cycle, which is the reason
    /// DomainModelMapper marks them [JsonIgnore] in the first place.</summary>
    [TestMethod]
    public void NavigationPropertiesAreNeverWritten()
    {
        var order = Order(("Ref", "string"), ("CustomerId", "int"));
        order.Properties.Add(new PropertyDefinition { Name = "Customer", Type = "Customer" });
        order.Properties.Add(new PropertyDefinition { Name = "Lines", Type = "List<OrderLine>" });

        var row = Rows(SeedFileWriter.Files(
            [order], Map(), Data(new() { ["Ref"] = "a", ["CustomerId"] = "1" })).Single().Json)[0];

        Assert.IsFalse(row.TryGetProperty("Customer", out _));
        Assert.IsFalse(row.TryGetProperty("Lines", out _));
        Assert.AreEqual(1, row.GetProperty("CustomerId").GetInt32());
    }

    /// <summary>Rows are keyed by ENTITY name and files by CLASS name, and ToUniqueClassName can
    /// rename one. Without the map the rows would go to a file no class ever reads.</summary>
    [TestMethod]
    public void ARenamedClassGetsItsEntitysRows()
    {
        var definition = new ClassDefinition { Name = "TypeItem" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Name", Type = "string" });

        var data = new ImportedData(
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>
            {
                ["Type"] = new List<IReadOnlyDictionary<string, string?>>
                {
                    new Dictionary<string, string?> { ["Name"] = "Hardback" },
                },
            },
            []);

        var file = SeedFileWriter.Files(
            [definition], new Dictionary<string, string> { ["Type"] = "TypeItem" }, data).Single();

        Assert.AreEqual("TypeItem.json", file.FileName);
        Assert.AreEqual("Hardback", Rows(file.Json)[0].GetProperty("Name").GetString());
    }

    [TestMethod]
    public void AClassWithNoImportedRowsGetsNoFile()
        => Assert.AreEqual(0, SeedFileWriter.Files(
            [Order(("Ref", "string"))], Map(), ImportedData.None).Count);

    /// <summary>A value that looks like JSON or C# is data, not syntax. It goes through the JSON
    /// writer, so it can never break out of its string.</summary>
    [TestMethod]
    public void AValueContainingQuotesAndBracesStaysOneString()
    {
        var hostile = "\"},{\"Id\":99,\"Ref\":\"injected";

        var rows = Rows(SeedFileWriter.Files(
            [Order(("Ref", "string"))], Map(), Data(new() { ["Ref"] = hostile })).Single().Json);

        Assert.AreEqual(1, rows.Length);
        Assert.AreEqual(hostile, rows[0].GetProperty("Ref").GetString());
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~SeedFileWriterTests"
```

Expected: FAIL to compile — `SeedFileWriter` does not exist.

- [ ] **Step 4: Write the writer**

`AppScaffolding/SeedFileWriter.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// Renders imported rows as the seed files the generated app loads on first run: one
/// <c>&lt;Class&gt;.json</c> per entity that contributed data.
/// </summary>
/// <remarks>
/// It walks each class's FINAL property list rather than the data's keys, and that is the whole
/// design. The review loop can remove, rename, add and retype fields after the data was read, so
/// projecting through the class is what keeps the seed file and the generated class in step
/// automatically — a removed field stops being written, and no file can ever name a property that
/// does not exist.
/// <para>
/// Everything goes through <see cref="Utf8JsonWriter"/>, never string concatenation, so a cell
/// containing quotes and braces stays one string instead of becoming syntax.
/// </para>
/// </remarks>
public static class SeedFileWriter
{
    /// <summary>Folder under the generated project's <c>Data/</c> that holds the seed files.</summary>
    public const string SeedFolderName = "Seed";

    /// <summary>
    /// The files to write, as (file name, JSON) pairs. A class with no imported rows gets none —
    /// the generated seeder falls back to its sample rows for those.
    /// </summary>
    public static IReadOnlyList<(string FileName, string Json)> Files(
        IReadOnlyList<ClassDefinition> classes,
        IReadOnlyDictionary<string, string> classNames,
        ImportedData data)
    {
        // Class name -> entity name. Rows are keyed by entity; files are named for the class; and
        // DomainModelMapper.ToUniqueClassName can rename one ("Type" -> "TypeItem").
        var entityByClass = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entity, className) in classNames)
            entityByClass[className] = entity;

        var files = new List<(string, string)>();

        foreach (var definition in classes)
        {
            if (!entityByClass.TryGetValue(definition.Name, out var entityName))
                continue;

            var rows = data.For(entityName);
            if (rows.Count == 0)
                continue;

            files.Add((definition.Name + ".json", Render(definition, rows)));
        }

        return files;
    }

    private static string Render(
        ClassDefinition definition, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();

            for (var index = 0; index < rows.Count; index++)
            {
                writer.WriteStartObject();

                foreach (var property in definition.Properties)
                {
                    // Ids are assigned here, 1..N in row order — which is exactly what the foreign
                    // keys the readers and NormalizationPlan emitted already point at.
                    if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteNumber("Id", index + 1);
                        continue;
                    }

                    // A navigation property is resolved from its foreign key at load time. Writing
                    // it would make Order -> Customer -> Orders a cycle, which is why
                    // DomainModelMapper marks these [JsonIgnore].
                    if (!IsScalar(property.Type))
                        continue;

                    if (!rows[index].TryGetValue(property.Name, out var raw)
                        || string.IsNullOrWhiteSpace(raw))
                        continue;

                    Write(writer, property, raw.Trim());
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The five types the model can carry. Anything else is a navigation property.</summary>
    private static bool IsScalar(string type)
        => type is "string" or "int" or "decimal" or "bool" or "DateTime";

    /// <summary>
    /// Writes one value as its declared JSON type. A value that does not parse is OMITTED, leaving
    /// the class's default: the user may have retyped the field in the review loop, and the row
    /// should still load rather than the whole file failing.
    /// </summary>
    private static void Write(Utf8JsonWriter writer, PropertyDefinition property, string raw)
    {
        switch (property.Type)
        {
            case "int":
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    writer.WriteNumber(property.Name, i);
                break;

            case "decimal":
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                    writer.WriteNumber(property.Name, d);
                break;

            case "bool":
                if (TryBoolean(raw, out var b))
                    writer.WriteBoolean(property.Name, b);
                break;

            case "DateTime":
                if (DateTime.TryParse(
                        raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
                    writer.WriteString(property.Name, moment);
                break;

            default:
                writer.WriteString(property.Name, raw);
                break;
        }
    }

    /// <summary>The word forms <c>ColumnTypeInference</c> accepts as boolean. Kept in step with it:
    /// a column typed bool from "yes"/"no" has to be writable as one.</summary>
    private static bool TryBoolean(string raw, out bool value)
    {
        switch (raw.ToLowerInvariant())
        {
            case "true" or "yes" or "y" or "1":
                value = true;
                return true;
            case "false" or "no" or "n" or "0":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~SeedFileWriterTests"
```

Expected: PASS, 12 tests.

- [ ] **Step 6: Verify the `DomainModelMapper` change broke nothing**

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj
```

Expected: the whole MSTest suite passes, `DomainModelMapperTests` included. The one-argument overload has the same behaviour, so any failure here means the local rename in step 1 was applied incompletely.

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding CodeGenerator.Tests
git commit -m "feat(scaffold): render imported rows as seed JSON via the final model"
```

---

## Task 15: Wire seed files into the scaffolder and the generated seeder

**Files:**
- Modify: `AppScaffolding/CsprojEditing.cs` (add `InjectSeedContent`)
- Modify: `AppScaffolding/SeedDataTemplate.cs` (second mode)
- Modify: `AppScaffolding/AppScaffolder.cs` (new `Scaffold` overload; write the files)
- Create: `CodeGenerator.Tests/App/SeedDataFromFileTests.cs`

**Interfaces:**
- Consumes: `SeedFileWriter.Files`, `SeedFileWriter.SeedFolderName`, `ImportedData`, `DomainModelMapper.ToClassDefinitions(model, out classNames)`.
- Produces:
  - `CsprojEditing.InjectSeedContent(string csproj) -> string`
  - `SeedDataTemplate.Source(string projectName, IReadOnlyList<string> orderedEntityNames, IReadOnlyList<string> foreignKeyFieldNames, IReadOnlyList<string> seededFromFile) -> string` (existing three-argument overload delegates with an empty list)
  - `AppScaffolder.Scaffold(CandidateDomainModel, string appName, string workspaceRoot, ITargetScaffolder, PersistenceKind, ImportedData?) -> ScaffoldResult` (existing overloads delegate with null)

The generated seeder loads through `IStore<T>`, so JSON *and* SQLite persistence both work with no further change.

- [ ] **Step 1: Write the failing test**

`CodeGenerator.Tests/App/SeedDataFromFileTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SeedDataFromFileTests
{
    [TestMethod]
    public void WithNoSeededEntities_TheGeneratedSeederKeepsItsSampleRows()
    {
        var source = SeedDataTemplate.Source("Shop", ["Order"], [], []);

        StringAssert.Contains(source, "SampleValue");
        Assert.IsFalse(source.Contains("SeedFiles"),
            "a project with no imported data must not carry the seed-file branch at all");
    }

    [TestMethod]
    public void WithSeededEntities_TheGeneratedSeederLoadsTheirFiles()
    {
        var source = SeedDataTemplate.Source("Shop", ["Order", "Customer"], [], ["Order"]);

        StringAssert.Contains(source, "SeedFiles");
        StringAssert.Contains(source, "\"Order\"");

        // The folder the scaffolder actually writes to, not a second spelling of it.
        StringAssert.Contains(source, SeedFileWriter.SeedFolderName);

        // Sample rows survive for entities with no file -- Customer above.
        StringAssert.Contains(source, "SampleValue");
    }

    [TestMethod]
    public void InjectSeedContent_AddsTheContentItem()
    {
        var csproj = "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>";

        var updated = CsprojEditing.InjectSeedContent(csproj);

        StringAssert.Contains(updated, "Data\\Seed\\*.json");
        StringAssert.Contains(updated, "PreserveNewest");
    }

    [TestMethod]
    public void InjectSeedContent_IsIdempotent()
    {
        var once = CsprojEditing.InjectSeedContent("<Project>\n</Project>");

        Assert.AreEqual(once, CsprojEditing.InjectSeedContent(once));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~SeedDataFromFileTests"
```

Expected: FAIL to compile — the four-argument `SeedDataTemplate.Source` and `CsprojEditing.InjectSeedContent` do not exist.

- [ ] **Step 3: Add `InjectSeedContent`**

Append to `AppScaffolding/CsprojEditing.cs`, inside the class:

```csharp
    /// <summary>
    /// Registers the seed folder as content copied to the output directory, so the generated app
    /// can find <c>Data/Seed/&lt;Class&gt;.json</c> beside its binary at run time. Idempotent, and
    /// a no-op on a project file with no closing tag.
    /// </summary>
    public static string InjectSeedContent(string csproj)
    {
        const string include = @"Data\Seed\*.json";

        if (csproj.Contains(include, StringComparison.Ordinal))
            return csproj;

        var itemGroup =
            "  <ItemGroup>\n" +
            $"    <Content Include=\"{include}\" CopyToOutputDirectory=\"PreserveNewest\" />\n" +
            "  </ItemGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, itemGroup);
    }
```

- [ ] **Step 4: Give `SeedDataTemplate` its second mode**

In `AppScaffolding/SeedDataTemplate.cs`, keep the existing three-argument `Source` as a delegating overload and add the four-argument one:

```csharp
    /// <summary>Backward-compatible overload: no imported data, so every store gets sample rows.</summary>
    public static string Source(
        string projectName,
        IReadOnlyList<string> orderedEntityNames,
        IReadOnlyList<string> foreignKeyFieldNames)
        => Source(projectName, orderedEntityNames, foreignKeyFieldNames, []);
```

In the four-argument version, build two extra fragments before the interpolated source. When
`seededFromFile` is empty, both are empty strings and the emitted file is byte-for-byte what it is
today — a project with no imported data must not carry a branch it can never take.

```csharp
        var seedFileSet = seededFromFile.Count == 0
            ? string.Empty
            : """

    /// <summary>Entities whose starting rows were imported from the user's own data and written to
    /// Data/Seed/&lt;Class&gt;.json. Everything else falls back to generated sample rows.</summary>
    private static readonly HashSet<string> SeedFiles = new(StringComparer.Ordinal)
    { """ + string.Join(", ", seededFromFile.Select(n => $"\"{n}\"")) + " };\n";

        var seedFileBranch = seededFromFile.Count == 0
            ? string.Empty
            : $$"""

        // Imported rows win over sample rows. Written at generation time from the user's own data,
        // with Ids already assigned 1..N so the foreign keys in these files resolve.
        if (SeedFiles.Contains(typeof(T).Name) && TryLoadSeedFile<T>(out var imported))
        {
            store.SaveAll(imported);
            seededIds[typeof(T).Name] = imported.Select(GetId).ToList();
            return;
        }
""";

        var seedFileLoader = seededFromFile.Count == 0
            ? string.Empty
            : """

    /// <summary>Reads Data/Seed/&lt;T&gt;.json from beside the binary. A missing or unreadable file
    /// is not fatal: the caller falls back to sample rows, which is better than refusing to start.</summary>
    private static bool TryLoadSeedFile<T>(out List<T> rows) where T : class, new()
    {
        rows = new List<T>();

        var path = Path.Combine(
            AppContext.BaseDirectory, "Data", "SEED_FOLDER", typeof(T).Name + ".json");

        if (!File.Exists(path))
            return false;

        try
        {
            var loaded = JsonSerializer.Deserialize<List<T>>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (loaded is null || loaded.Count == 0)
                return false;

            rows = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }
""".Replace("SEED_FOLDER", SeedFileWriter.SeedFolderName);
```

Then in the emitted source:

- add `using System.IO;` and `using System.Text.Json;` to the `using` block **only when**
  `seededFromFile.Count > 0` (an unused using is a warning, and CI builds with `-warnaserror`):

  ```csharp
        var extraUsings = seededFromFile.Count == 0
            ? string.Empty
            : "using System.IO;\nusing System.Text.Json;\n";
  ```

  and interpolate `{{extraUsings}}` directly after the existing `using System.Reflection;` line.

- interpolate `{{seedFileSet}}` after the `ForeignKeyFields` declaration
- interpolate `{{seedFileBranch}}` inside `Seed<T>`, immediately after the
  `if (existing.Count > 0) { ... }` block and before `var rows = new List<T>();`
- interpolate `{{seedFileLoader}}` after `SampleValue`, before `GetId`

`GetId` is already declared and is reused by the new branch, so nothing else changes.

- [ ] **Step 5: Wire the scaffolder**

In `AppScaffolding/AppScaffolder.cs`:

Change the two existing public overloads to pass `null`, and make the five-argument one delegate:

```csharp
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target,
        PersistenceKind persistence)
        => Scaffold(model, appName, workspaceRoot, target, persistence, importedData: null);

    /// <summary>
    /// As the five-argument overload, additionally seeding the generated app from
    /// <paramref name="importedData"/> — the rows the user's own data source contributed.
    /// </summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target,
        PersistenceKind persistence, ImportedData? importedData)
    {
```

Inside that body, four edits:

1. Capture the class-name map:

```csharp
        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);
```

2. Compute the seed files before the `files` list is built:

```csharp
        // Projected through `classes`, so any review-loop edit is already accounted for.
        var seedFiles = importedData is null
            ? Array.Empty<(string FileName, string Json)>()
            : SeedFileWriter.Files(classes, classNames, importedData).ToArray();

        var seededClassNames = seedFiles
            .Select(f => Path.GetFileNameWithoutExtension(f.FileName))
            .ToList();
```

3. Pass them to the template — replace the existing `SeedData.cs` entry with:

```csharp
            (dataFolder, "SeedData.cs",
                SeedDataTemplate.Source(
                    projectName,
                    SeedDataTemplate.OrderByDependency(classes),
                    SeedDataTemplate.ForeignKeyFieldNames(classes),
                    seededClassNames)),
```

4. After the `foreach (var (folder, name, source) in files)` loop, write the seed files and
   register them in the project file:

```csharp
        // Data files, not source: written with the same best-effort WriteSource so a locked file
        // is reported through `expected` rather than throwing mid-scaffold.
        if (seedFiles.Length > 0)
        {
            var seedFolder = Path.Combine(dataFolder, SeedFileWriter.SeedFolderName);

            foreach (var (fileName, json) in seedFiles)
            {
                expected++;
                var path = WriteSource(seedFolder, fileName, json);
                if (path is not null)
                    written.Add(path);
            }

            InjectSeedContent(csprojPath);
        }
```

and add the helper beside `InjectEfCorePackage`, which it mirrors exactly:

```csharp
    /// <summary>Best-effort registration of the seed folder as copied-to-output content. Silent on
    /// failure: the app still builds, it just starts with sample rows instead of the imported ones.</summary>
    private static void InjectSeedContent(string csprojPath)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            File.WriteAllText(
                csprojPath, CsprojEditing.InjectSeedContent(File.ReadAllText(csprojPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~SeedDataFromFileTests"
```

Expected: PASS, 4 tests.

- [ ] **Step 7: Verify no existing generated project changed**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: the whole suite passes, `GeneratedProjectBuildTests` included. With no imported data the
emitted `SeedData.cs` must be byte-for-byte what it was; a failure here means one of the three
fragments is emitting something when `seededFromFile` is empty.

- [ ] **Step 8: Commit**

```bash
git add AppScaffolding CodeGenerator.Tests
git commit -m "feat(scaffold): seed the generated app from imported data files"
```

---

## Task 16: Wire the front step into `build app`

**Files:**
- Modify: `CodeGenerator/CodeGenerator.csproj` (reference `DataIngestion`)
- Modify: `CodeGenerator/App/BuildAppCommand.cs`
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs`
- Modify: `docs/commands.md` (regenerated)
- Create: `CodeGenerator.Tests/App/BuildAppDataSourceTests.cs`

**Interfaces:**
- Consumes: `InlineDataSource.Parse`, `DataSourceInterview.Resolve`, `DataSourceReaders.TryCreate`, `NormalizationAnalyzer.Analyze`, `NormalizationPrompt.Lines`/`.Choose`, `NormalizationPlan.Apply`, `DataModelBuilder.Build`, `TabularImport.ToImportedData`, `ImportedData`.
- Produces:
  - `BuildAppCommand.IsBuildApp(string input) -> bool` (widened)
  - `BuildAppCommand.Tail(string input) -> string`
  - `BuildAppCommand.AppNameFor(string description, DataSourceSpec? source) -> string`
  - `BuildAppCommand.CombineReports(IReadOnlyList<TabularTable> tables) -> NormalizationReport`

- [ ] **Step 1: Reference `DataIngestion` from the CLI**

In `CodeGenerator/CodeGenerator.csproj`, inside the `ProjectReference` `ItemGroup`:

```xml
    <ProjectReference Include="..\DataIngestion\DataIngestion.csproj" />
```

- [ ] **Step 2: Write the failing test**

`CodeGenerator.Tests/App/BuildAppDataSourceTests.cs`:

```csharp
using CodeGenerator.App;
using DataIngestion;
using DataIngestion.Normalization;
using DataIngestion.Schema;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppDataSourceTests
{
    [TestMethod]
    public void IsBuildApp_StillAcceptsTheColonForm()
    {
        Assert.IsTrue(BuildAppCommand.IsBuildApp("build app: a library"));
        Assert.IsTrue(BuildAppCommand.IsBuildApp("  BUILD APP: a library"));
    }

    /// <summary>The requirement is "run build app" with no description at all.</summary>
    [TestMethod]
    public void IsBuildApp_AcceptsTheBareForm()
    {
        Assert.IsTrue(BuildAppCommand.IsBuildApp("build app"));
        Assert.IsTrue(BuildAppCommand.IsBuildApp("Build App"));
        Assert.IsTrue(BuildAppCommand.IsBuildApp(@"build app from csv D:\orders.csv"));
    }

    /// <summary>A prefix check on "build app" would hijack these. Widening the trigger must not
    /// start swallowing other commands.</summary>
    [TestMethod]
    public void IsBuildApp_DoesNotHijackOtherInput()
    {
        Assert.IsFalse(BuildAppCommand.IsBuildApp("build apple tracker"));
        Assert.IsFalse(BuildAppCommand.IsBuildApp("build application"));
        Assert.IsFalse(BuildAppCommand.IsBuildApp("rebuild app"));
        Assert.IsFalse(BuildAppCommand.IsBuildApp("build"));
    }

    [TestMethod]
    public void Tail_IsWhatFollowsTheCommand_WithOrWithoutTheColon()
    {
        Assert.AreEqual("a library", BuildAppCommand.Tail("build app: a library"));
        Assert.AreEqual("a library", BuildAppCommand.Tail("build app a library"));
        Assert.AreEqual(string.Empty, BuildAppCommand.Tail("build app"));
        Assert.AreEqual(string.Empty, BuildAppCommand.Tail("build app:"));
    }

    /// <summary>A description names the app when there is one...</summary>
    [TestMethod]
    public void AppNameFor_PrefersTheDescription()
        => Assert.AreEqual("OrderTracking", BuildAppCommand.AppNameFor(
            "order tracking", new DataSourceSpec(DataSourceKind.Csv, @"D:\orders.csv")));

    /// <summary>...and with no description the source's base name goes through the SAME naming
    /// rule, rather than a second one invented for this path.</summary>
    [TestMethod]
    public void AppNameFor_FallsBackToTheSourcesBaseName()
        => Assert.AreEqual("Orders", BuildAppCommand.AppNameFor(
            string.Empty, new DataSourceSpec(DataSourceKind.Csv, @"D:\data\orders.csv")));

    [TestMethod]
    public void AppNameFor_WithNeither_UsesTheExistingFallback()
        => Assert.AreEqual("GeneratedApp", BuildAppCommand.AppNameFor(string.Empty, null));

    /// <summary>Findings from every table are numbered in ONE list, so `n 2` means the same thing
    /// whichever table the finding came from.</summary>
    [TestMethod]
    public void CombineReports_NumbersFindingsAcrossEveryTable()
    {
        var orders = Table("Orders", ["Ref", "Item1", "Item2"],
            ["ORD-1", "Nut", "Bolt"], ["ORD-2", "Washer", null]);
        var members = Table("Members", ["Name", "Phone_1", "Phone_2"],
            ["Ada", "555-1", "555-2"], ["Grace", "555-3", null]);

        var report = BuildAppCommand.CombineReports([orders, members]);

        Assert.AreEqual(2, report.Findings.Count);
        Assert.AreEqual("Orders", report.Findings[0].TableName);
        Assert.AreEqual("Members", report.Findings[1].TableName);
    }

    /// <summary>The same note from several tables is said once, not once per table.</summary>
    [TestMethod]
    public void CombineReports_DoesNotRepeatTheSameNote()
    {
        var a = Table("Orders", ["Ref", "CustomerName", "CustomerCity"], ["1", "Ada", "London"]);
        var b = Table("Members", ["Ref", "CustomerName", "CustomerCity"], ["1", "Ada", "London"]);

        var report = BuildAppCommand.CombineReports([a, b]);

        Assert.AreEqual(1, report.Notes.Count(n => n == NormalizationAnalyzer.TooFewRowsNote));
    }

    private static TabularTable Table(string name, string[] header, params string?[][] rows)
    {
        var table = new TabularTable { Name = name, SourceLabel = name + ".csv" };
        foreach (var column in header)
            table.Columns.Add(new TabularColumn { Name = column });

        foreach (var row in rows)
            table.Rows.Add(row);

        foreach (var column in table.Columns)
            column.Type = ColumnTypeInference.Infer(table.ColumnValues(column.Name), column.Name);

        return table;
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildAppDataSourceTests"
```

Expected: FAIL to compile — `Tail`, `AppNameFor` and `CombineReports` do not exist; the bare-form assertions would fail too.

- [ ] **Step 4: Rewrite the front of `BuildAppCommand`**

Replace `Prefix`, `IsBuildApp` and `Handle` in `CodeGenerator/App/BuildAppCommand.cs`, and add the
four new members. Everything from `RunReviewLoop` down is unchanged except for threading
`ImportedData` to the scaffolder.

```csharp
    private const string Command = "build app";

    /// <summary>
    /// Whether <paramref name="input"/> is a build-app line. Both <c>build app: &lt;description&gt;</c>
    /// and the bare <c>build app</c> count, the latter so a user with data need not invent a
    /// description first.
    /// </summary>
    /// <remarks>
    /// Deliberately not a bare prefix test: "build apple tracker" and "build application" both start
    /// with "build app". The character after the command must be a colon, whitespace, or nothing.
    /// <para>
    /// Widening this is safe because "build app" appears in no recognition source — not
    /// <c>LiteralCommandTable</c>, not a <c>CommandFront</c> guard, not <c>NlpCommandMapper</c> —
    /// only in the <c>CommandDictionary</c> help catalog. <c>CodeGeneratorStarter</c> tests this
    /// before the pipeline runs, so nothing downstream competes for the text.
    /// </para>
    /// </remarks>
    public static bool IsBuildApp(string input)
    {
        var text = (input ?? string.Empty).TrimStart();

        if (!text.StartsWith(Command, StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Length == Command.Length)
            return true;

        var next = text[Command.Length];
        return next == ':' || char.IsWhiteSpace(next);
    }

    /// <summary>Everything after the command word, with an optional colon removed.</summary>
    internal static string Tail(string input)
    {
        var text = (input ?? string.Empty).TrimStart();
        if (text.Length <= Command.Length)
            return string.Empty;

        var rest = text[Command.Length..];
        return (rest.StartsWith(':') ? rest[1..] : rest).Trim();
    }

    public void Handle(string input, string workspaceRoot)
    {
        var request = InlineDataSource.Parse(Tail(input));
        var source = request.Source;
        var normalize = request.Normalize;

        // Redirected stdin never prompts: existing piped scripts and E2E tests feed the review loop
        // directly, and a question at the front would break every one of them. They opt in with the
        // inline `from <format> <location>` form instead.
        if (source is null && !Console.IsInputRedirected)
        {
            var outcome = DataSourceInterview.Resolve(Console.In, Console.Out);

            if (outcome.Decision == InterviewDecision.Cancelled)
            {
                // Not the same as "no data" — someone who backed out must not be handed a
                // Wikipedia build they never asked for.
                Console.WriteLine("Cancelled. Nothing was generated.");
                return;
            }

            source = outcome.Source;
        }

        if (source is null)
        {
            HandleFromDescription(request.Description, workspaceRoot);
            return;
        }

        HandleFromData(request.Description, source, normalize, workspaceRoot);
    }

    /// <summary>The unchanged path: NLE seeds, Wikipedia topic, enrichment, review loop.</summary>
    private static void HandleFromDescription(string description, string workspaceRoot)
    {
        if (description.Length == 0)
        {
            Console.WriteLine("Usage: build app: <description of the system you want>");
            Console.WriteLine("   or: build app from csv <path>   (also txt, json)");
            return;
        }

        var cacheDir = Path.Combine(workspaceRoot, ".dd-cache");
        var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDir);

        Console.WriteLine("Discovering domain (this may fetch from Wikipedia the first time)...");
        DiscoveryResult result = pipeline.Discover(description);

        if (result.Topic.IsRecognized)
        {
            if (result.Topic.SupportingTitles.Count > 0)
                Console.WriteLine("Also read: " + string.Join(", ", result.Topic.SupportingTitles));
        }
        else
            Console.WriteLine("No topic recognized online — showing seed entities only. " +
                              "You can add entities by hand below.");

        RunReviewLoop(
            result.Model, DomainAppName.FromDescription(description), workspaceRoot,
            ImportedData.None);
    }

    /// <summary>
    /// The data path. No Wikipedia call at all: the schema is ground truth, so there is nothing to
    /// recognize, enrich or shape, and the run is fast and offline.
    /// </summary>
    private static void HandleFromData(
        string description, DataSourceSpec source, bool normalize, string workspaceRoot)
    {
        if (!DataSourceReaders.TryCreate(source.Kind, out var reader))
        {
            Console.WriteLine(DataSourceReaders.NotSupportedMessage(source.Kind));
            return;
        }

        IReadOnlyList<TabularTable> tables;
        try
        {
            tables = reader.Read(source);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            // The reader's message already says what was wrong in the user's own terms.
            Console.WriteLine(ex.Message);
            return;
        }

        Console.WriteLine(
            $"Read {tables.Count} table(s) from {tables[0].Label}: "
            + string.Join(", ", tables.Select(t => $"{t.Name} ({t.Rows.Count} rows)")));

        var report = CombineReports(tables);

        foreach (var line in NormalizationPrompt.Lines(
                     report, tables[0].Label, tables.Sum(t => t.Rows.Count)))
        {
            Console.WriteLine(line);
        }

        // A scripted run applies everything or nothing, so it stays deterministic; `normalize`
        // inline is how it opts in.
        var chosen = Console.IsInputRedirected
            ? (normalize ? report.Findings : [])
            : NormalizationPrompt.Choose(report, Console.In, Console.Out);

        if (chosen.Count > 0)
        {
            tables = NormalizationPlan.Apply(tables, chosen);
            Console.WriteLine($"Applied {chosen.Count} normalization change(s).");
        }

        var model = DataModelBuilder.Build(tables);
        var imported = TabularImport.ToImportedData(tables);

        // Said out loud: a cap the user is not told about reads as "all your data is in there".
        foreach (var note in imported.Notes)
            Console.WriteLine(note);

        RunReviewLoop(model, AppNameFor(description, source), workspaceRoot, imported);
    }

    /// <summary>
    /// The findings of every table in one numbered list, so `n 2` means the same thing whichever
    /// table the finding came from. Notes are de-duplicated: the same skipped check reported once
    /// per table would be noise.
    /// </summary>
    internal static NormalizationReport CombineReports(IReadOnlyList<TabularTable> tables)
    {
        var findings = new List<NormalizationFinding>();
        var notes = new List<string>();

        foreach (var table in tables)
        {
            var report = NormalizationAnalyzer.Analyze(table);
            findings.AddRange(report.Findings);

            foreach (var note in report.Notes)
                if (!notes.Contains(note, StringComparer.Ordinal))
                    notes.Add(note);
        }

        return new NormalizationReport(findings, notes);
    }

    /// <summary>
    /// The project name. A description names the app when there is one; otherwise the source's base
    /// name goes through the SAME <see cref="DomainAppName.FromDescription"/> rule, so there is one
    /// naming rule in the product rather than a second one invented for this path.
    /// </summary>
    internal static string AppNameFor(string description, DataSourceSpec? source)
    {
        if (description.Length > 0)
            return DomainAppName.FromDescription(description);

        if (source is null)
            return DomainAppName.FromDescription(string.Empty);

        return DomainAppName.FromDescription(
            source.Kind == DataSourceKind.Sql
                ? source.SqlDatabase ?? string.Empty
                : Path.GetFileNameWithoutExtension(source.Location));
    }
```

Then thread the data through the review loop — change its signature and its one `Scaffold` call:

```csharp
    private static void RunReviewLoop(
        CandidateDomainModel model, string appName, string workspaceRoot, ImportedData importedData)
```

```csharp
                    var scaffold = new AppScaffolder()
                        .Scaffold(model, appName, workspaceRoot, target, persistence.Value,
                                  importedData);
```

Add the new usings at the top of the file:

```csharp
using DataIngestion;
using DataIngestion.Normalization;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildAppDataSourceTests"
```

Expected: PASS, 10 tests.

- [ ] **Step 6: Add the help-catalog entries**

In `CodeGenerationEngine/Core/CommandDictionary.cs`, inside the `#region "Build App"` block and
directly after the existing `build app: <description>` entry:

```csharp
        new()
        {
            Category = "Build App",
            Command = "build app",
            Description = "Starts the same flow with no description: asks whether you already have "
                        + "data, and builds the model from it if you do.",
            Example = "build app"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> from <format> <location>",
            Description = "Builds the model from existing data without prompting. Formats: csv, "
                        + "txt, json (excel and sql are not in this build yet). Add 'normalize' to "
                        + "apply every normalization change the data needs.",
            Example = "build app: order tracking from csv D:\\data\\orders.csv normalize"
        },
```

`CommandDictionaryDataTests` requires a non-empty description and example on every entry and
balanced `<>` in every example — both hold above.

- [ ] **Step 7: Regenerate `docs/commands.md`**

`CommandReferenceDocTests` fails until the committed doc matches the catalog. Regenerate rather
than hand-editing:

PowerShell:
```
$env:UPDATE_COMMAND_DOCS=1; dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CommandReferenceDocTests"; $env:UPDATE_COMMAND_DOCS=$null
```

Bash:
```
UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CommandReferenceDocTests"
```

Then confirm it passes without the variable set:

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CommandDictionary|FullyQualifiedName~CommandReferenceDoc"
```

Expected: PASS. Check `git diff docs/commands.md` shows only the two added commands.

- [ ] **Step 8: Verify the whole suite the way CI does**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: both succeed. `ReachableIntentGateTests` is unaffected — `build app` is handled in
`CodeGeneratorStarter` before the pipeline and has no intent.

- [ ] **Step 9: Commit**

```bash
git add CodeGenerator CodeGenerator.Tests CodeGenerationEngine docs/commands.md
git commit -m "feat(cli): ask build app whether you already have data"
```

---

## Task 17: End-to-end — a CSV becomes a project that compiles and holds the data

**Files:**
- Modify: `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs` (add two tests)

**Interfaces:**
- Consumes: `CsvReader`, `NormalizationAnalyzer`, `NormalizationPlan`, `DataModelBuilder`, `TabularImport`, `AppScaffolder.Scaffold(..., ImportedData?)`, `SeedFileWriter.SeedFolderName`; the class's existing private `RunDotNet` and `TryDeleteDirectory` helpers.
- Produces: no production code. This is the task that proves the chain.

The new tests go **in this existing class** so they reuse `RunDotNet` and `TryDeleteDirectory`
rather than duplicating a process harness. Both are `[TestCategory("Integration")]`, matching the
tests already there.

Substring assertions over generated source cannot see CS0023, CS0542 or a literal that broke out of
its string. Compiling can, which is the only reason this task exists.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`, and add
`using DataIngestion;`, `using DataIngestion.Normalization;`, `using DataIngestion.Schema.Readers;`
and `using System.Text.Json;` at the top:

```csharp
    /// <summary>
    /// The whole data path in one test: a CSV is read, normalized, turned into a model, scaffolded,
    /// and the result is COMPILED. Every stage in between can produce something that parses and
    /// still will not build -- a column named after its own class is CS0542, a seed value containing
    /// a quote breaks the JSON, a foreign key to a class that was never generated is CS0246 -- and
    /// none of that is visible to a substring assertion.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectGeneratedFromACsv_BuildsAndCarriesTheData()
    {
        // Short root: the scratchpad path plus a generated project's nesting exceeds MAX_PATH, and
        // MSBuild reports that as MSB3030 "file not found" on a file that is plainly there.
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var csv = Path.Combine(workspace, "orders.csv");

        // Deliberately awkward: an Id that must be dropped, a column named after its own class,
        // a repeating group, a partial dependency, a value carrying a quote and a brace.
        File.WriteAllText(csv, string.Join("\n",
            "Id,Order,CustomerName,CustomerCity,Qty,Item1,Item2",
            "1,\"a \"\"quoted\"\" {value}\",Ada,London,2,Nut,Bolt",
            "2,plain,Grace,York,3,Washer,",
            "3,another,Ada,London,4,Nut,",
            "4,more,Grace,York,5,Bolt,",
            "5,last,Ada,London,6,Washer,Nut",
            ""));

        try
        {
            var tables = new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, csv));

            var findings = NormalizationAnalyzer.Analyze(tables[0]).Findings;
            Assert.IsTrue(findings.Count >= 2,
                "expected at least the repeating group and the partial dependency, got: "
                + string.Join("; ", findings.Select(f => f.Headline)));

            var normalized = NormalizationPlan.Apply(tables, findings);
            var model = DataModelBuilder.Build(normalized);
            var imported = TabularImport.ToImportedData(normalized);

            Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Shop", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            // The generated project compiles. This is the assertion the others exist to enable.
            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");

            // ...and the data really is in there, as valid JSON with the awkward value intact.
            var seedFolder = Path.Combine(result.ProjectPath, "Data", SeedFileWriter.SeedFolderName);
            var orderSeed = Path.Combine(seedFolder, "Order.json");

            Assert.IsTrue(File.Exists(orderSeed), $"no seed file at {orderSeed}");

            var rows = JsonDocument.Parse(File.ReadAllText(orderSeed))
                .RootElement.EnumerateArray().ToList();

            Assert.AreEqual(5, rows.Count);
            Assert.AreEqual(1, rows[0].GetProperty("Id").GetInt32());

            // The column named after its own class was renamed, not dropped, and its hostile value
            // survived as one string.
            Assert.AreEqual("a \"quoted\" {value}", rows[0].GetProperty("OrderName").GetString());

            // The read Id column is gone -- Id is generated, and rows[0].Id above is the generated one.
            Assert.AreEqual(5, rows.Count(r => r.TryGetProperty("Id", out _)));

            // The extracted entities were scaffolded and seeded too.
            Assert.IsTrue(File.Exists(Path.Combine(seedFolder, "Customer.json")));
            Assert.IsTrue(File.Exists(Path.Combine(seedFolder, "OrderItem.json")));

            var customers = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(seedFolder, "Customer.json")))
                .RootElement.EnumerateArray().ToList();

            // Two distinct customers out of five order rows -- the point of the split.
            Assert.AreEqual(2, customers.Count);
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// The `k` (keep as-is) path must compile too. A flat table with a repeating group left in place
    /// produces Item1/Item2 as ordinary columns, and nothing about that may break the build.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectGeneratedFromACsv_WithoutNormalizing_AlsoBuilds()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var csv = Path.Combine(workspace, "orders.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Ref,Item1,Item2,Placed,Paid,Total",
            "ORD-1,Nut,Bolt,2026-01-15,yes,9.99",
            "ORD-2,Washer,,2026-02-01,no,4.50",
            ""));

        try
        {
            var tables = new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, csv));

            // No findings applied: this is exactly what `k` produces.
            var model = DataModelBuilder.Build(tables);
            var imported = TabularImport.ToImportedData(tables);

            Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Flat", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");

            var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                    result.ProjectPath, "Data", SeedFileWriter.SeedFolderName, "Order.json")))
                .RootElement.EnumerateArray().ToList();

            // Each of the five types round-tripped into JSON as itself.
            Assert.AreEqual("Nut", rows[0].GetProperty("Item1").GetString());
            Assert.AreEqual(9.99m, rows[0].GetProperty("Total").GetDecimal());
            Assert.IsTrue(rows[0].GetProperty("Paid").GetBoolean());
            Assert.AreEqual(new DateTime(2026, 1, 15), rows[0].GetProperty("Placed").GetDateTime());
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail for the right reason**

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~GeneratedProjectBuildTests"
```

Expected at this point in the plan: **PASS**, because Tasks 1–16 built everything these exercise.
That makes this a regression net rather than a red-first test, which is the honest description — so
before accepting it, break it deliberately once and confirm it catches the break. Comment out the
`InjectSeedContent(csprojPath)` call added in Task 15 step 5, re-run, and confirm the build still
succeeds but the app would start empty; then revert. If a deliberate break does not change the
result, the test is not asserting what it claims.

- [ ] **Step 3: Run the full suite the way CI does**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: both succeed. This is the gate; a green Debug build of one project proves nothing here.

- [ ] **Step 4: Drive the real CLI once, non-interactively**

`dotnet test` leaves the CLI's own `bin` stale, so rebuild before trusting what the CLI does:

```
dotnet build CodeGenerator/CodeGenerator.csproj -c Release
```

Then, from a short working directory:

```
"build app: order tracking from csv <path-to-a-test-csv> normalize" | dotnet run --project CodeGenerator -c Release --no-build
```

Expected output, in order: the tables read, the numbered normalization findings, `Applied N
normalization change(s).`, the candidate model with `[Data]` provenance, and the review-loop prompt.
Piped stdin then reaches EOF, so the loop reports `Input closed. Cancelled — nothing was generated.`
— which is correct, and confirms the front step did not consume the review loop's input.

Also confirm the unchanged path is untouched:

```
"build app: a library that lends books to members" | dotnet run --project CodeGenerator -c Release --no-build
```

Expected: no data question at all, and the same discovery output as before this branch.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator.Tests
git commit -m "test(data): compile a project generated from a CSV, with its data"
```

---

## Follow-on branches (not this plan)

1. **Excel** — `ExcelReader : IDataSourceReader` over `DocumentFormat.OpenXml`, one sheet per
   `TabularTable`, added to `DataSourceReaders.TryCreate`. Nothing downstream changes.
2. **SQL Server** — `SqlServerReader` over `Microsoft.Data.SqlClient`, plus the connection /
   database / tables prompts in `DataSourceInterview` (which today stops at the not-supported
   message). Real declared types and real declared foreign keys, so it populates
   `CandidateRelationship` directly instead of inferring from `<Target>Id` naming.
3. **Two pre-existing gaps**, unrelated to this work but adjacent to it: the WPF target gets no test
   project, and a zero-entity app does not compile.


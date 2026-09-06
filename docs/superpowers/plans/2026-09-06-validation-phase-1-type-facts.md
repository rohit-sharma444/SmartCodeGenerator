# Validation Phase 1: Type Facts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry the type facts the pipeline already reads and discards all the way to the generated validator, so validation is driven by a column's declared type instead of by five property names.

**Architecture:** A `TypeFacts` record rides on each of the three carriers a column crosses — `TabularColumn`, `CandidateField`, `PropertyDefinition` — so facts survive the review loop's renames. `SqlClientSchemaSource` asks for the three columns it never requested; `ColumnTypeInference` records what it saw while scanning a file. `DomainModelMapper.InferValidation` then reads facts rather than names, and `ValidatorTemplate.Checks` emits every rule kind those facts can produce.

**Tech Stack:** C#, .NET 10 (`net10.0`, `net10.0-windows` for UI projects), MSTest, Roslyn for compile-testing emitted code.

**Spec:** `docs/superpowers/specs/2026-09-06-validation-architecture-design.md`

## Global Constraints

- **Build must pass `-warnaserror` in Release.** CI runs `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-restore -warnaserror` then `dotnet test ... --no-build`. A local Debug build of one project hides analyzer errors — see the incremental-build trap below.
- **Analyzers only surface on a non-incremental build.** `dotnet build` prints "Build succeeded" without rerunning analyzers when nothing changed. Any `-warnaserror` check needs `--no-incremental`.
- **MSTest, not xUnit.** `[TestClass]`, `[TestMethod]`, `Assert.*`, `CollectionAssert.*`.
- **MSTEST0037 rejects `Assert.AreEqual(n, x.Count)`.** Use `Assert.HasCount(n, collection)`.
- **Emitted C# is tested by compiling it**, never by substring assertions. Use `Compiler.Compile` / `CompileSources`. Where a class is emitted as two partial halves, compile **both files together** with `CompileSources` — a concatenation hides CS0260.
- **Never propose a rule from a sample.** Phase 1 only carries *declared* facts and *complete-scan* evidence. Nothing in this phase infers from the 200-row sample.
- **Ranges come from declared bounds only**, never from observed minimum and maximum values.
- This phase changes no public CLI surface, so `docs/commands.md` and the manual's chapter-18 appendix are untouched. Later phases change both.

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `CodeGenerationEngine/Models/TypeFacts.cs` | The facts a source declared about a column | Create |
| `CodeGenerationEngine/Models/PropertyDefinition.cs` | Carries facts to the emitter | Modify |
| `DomainDiscovery/DomainDiscovery.csproj` | Gains a reference to CodeGenerationEngine | Modify |
| `DomainDiscovery/Model/CandidateDomainModel.cs` | `CandidateField` carries facts across the review loop | Modify |
| `DataIngestion/Schema/TabularColumn.cs` | Carries facts out of a reader | Modify |
| `DataIngestion/Schema/Readers/Sql/ISqlSchemaSource.cs` | `SqlColumnInfo` gains length/precision/scale | Modify |
| `DataIngestion/Schema/Readers/Sql/SqlClientSchemaSource.cs:54-73` | Asks for the three discarded columns | Modify |
| `DataIngestion/Schema/Readers/Sql/SqlServerReader.cs:37-48` | Fills `TabularColumn.Facts` | Modify |
| `DataIngestion/Schema/ColumnTypeInference.cs` | Records evidence while scanning | Modify |
| `DataIngestion/DataModelBuilder.cs:257-261` | Copies facts column → field | Modify |
| `AppScaffolding/DomainModelMapper.cs:121-123, 240-262` | Copies facts field → property; type-driven inference | Modify |
| `AppScaffolding/ValidatorTemplate.cs:298-...` | Emits every rule kind, guarded | Modify |

---

### Task 1: The `TypeFacts` carrier

`TypeFacts` must be visible to three assemblies. `PropertyDefinition` lives in `CodeGenerationEngine`, which today references no project at all; `CandidateField` lives in `DomainDiscovery`, which references only `NaturalLanguageEngine`. So `TypeFacts` is defined in `CodeGenerationEngine` (the graph root) and `DomainDiscovery` gains a reference to it. `DataIngestion` already reaches `CodeGenerationEngine` transitively via `AppScaffolding`.

The alternative — a side table keyed by field name, avoiding the new reference — is rejected: `CandidateField.Name` is settable precisely so the review loop can rename a field, and a rename would silently orphan the facts, degrading the column back to a blanket rule with nothing to show it happened.

**Files:**
- Create: `CodeGenerationEngine/Models/TypeFacts.cs`
- Modify: `CodeGenerationEngine/Models/PropertyDefinition.cs`
- Modify: `DomainDiscovery/DomainDiscovery.csproj`
- Modify: `DomainDiscovery/Model/CandidateDomainModel.cs`
- Modify: `DataIngestion/Schema/TabularColumn.cs`
- Test: `CodeGenerator.Tests/App/TypeFactsCarrierTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `CodeGenerationEngine.Models.TypeFacts` with `bool? IsNullable`, `int? MaxLength`, `bool IsUnbounded`, `int? Precision`, `int? Scale`, `string? DeclaredType`. A `TypeFacts? Facts { get; set; }` property on `PropertyDefinition`, `CandidateField` and `TabularColumn`, defaulting to `null`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/TypeFactsCarrierTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TypeFactsCarrierTests
{
    [TestMethod]
    public void TypeFacts_KnowsNothing_ByDefault()
    {
        // A discovered domain has no data and no schema. Every fact must be
        // separately absent, so inference can tell "not known" from "known to be 0".
        var facts = new TypeFacts();

        Assert.IsNull(facts.IsNullable);
        Assert.IsNull(facts.MaxLength);
        Assert.IsNull(facts.Precision);
        Assert.IsNull(facts.Scale);
        Assert.IsNull(facts.DeclaredType);
        Assert.IsFalse(facts.IsUnbounded);
    }

    [TestMethod]
    public void UnboundedIsDistinct_FromUnknownLength()
    {
        // nvarchar(max) is a KNOWN absence of a bound. nvarchar of unknown size is
        // an unknown one. Collapsing them would put a 200-char cap back on a max column.
        var unbounded = new TypeFacts { IsUnbounded = true };
        var unknown = new TypeFacts();

        Assert.IsNull(unbounded.MaxLength);
        Assert.IsNull(unknown.MaxLength);
        Assert.AreNotEqual(unbounded.IsUnbounded, unknown.IsUnbounded);
    }

    [TestMethod]
    public void AllThreeCarriers_HoldTheSameFacts()
    {
        // Proves the reference graph allows one shared type rather than three
        // parallel shapes that must be mapped between.
        var facts = new TypeFacts
        {
            IsNullable = false,
            MaxLength = 50,
            DeclaredType = "nvarchar"
        };

        var column = new TabularColumn { Name = "Surname", Facts = facts };
        var field = new CandidateField { Name = "Surname", Type = "string", Facts = facts };
        var property = new PropertyDefinition { Name = "Surname", Type = "string", Facts = facts };

        Assert.AreEqual(50, column.Facts!.MaxLength);
        Assert.AreEqual(50, field.Facts!.MaxLength);
        Assert.AreEqual(50, property.Facts!.MaxLength);
        Assert.IsFalse(property.Facts.IsNullable!.Value);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter TypeFactsCarrierTests`
Expected: FAIL to compile — `TypeFacts` does not exist, and no carrier has a `Facts` property.

- [ ] **Step 3: Create `TypeFacts`**

Create `CodeGenerationEngine/Models/TypeFacts.cs`:

```csharp
namespace CodeGenerationEngine.Models;

/// <summary>
/// What a source declared about a column, beyond the five coarse types the generator emits.
/// </summary>
/// <remarks>
/// Every fact is separately nullable because "not known" and "known to be absent" lead to
/// different rules. A discovered domain has no data and no schema and carries none of these; it
/// must still generate, so no consumer may require a fact to be present.
/// </remarks>
public sealed record TypeFacts
{
    /// <summary>False when the source declares the column NOT NULL. Null when unknown.</summary>
    public bool? IsNullable { get; init; }

    /// <summary>
    /// Declared character length. Null when unknown, and also null when deliberately unbounded —
    /// <see cref="IsUnbounded"/> separates those two.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// True for <c>nvarchar(max)</c> and friends: a KNOWN absence of a bound, as opposed to an
    /// unknown one. Collapsing the two would put a length cap back on a max column.
    /// </summary>
    public bool IsUnbounded { get; init; }

    /// <summary>Total digits for a decimal. Null when unknown.</summary>
    public int? Precision { get; init; }

    /// <summary>Digits after the point for a decimal. Null when unknown.</summary>
    public int? Scale { get; init; }

    /// <summary>
    /// The source's own type name — "tinyint", "smallint", "date". Null for a source that
    /// declares none. This is what <see cref="Readers.SqlTypeMap"/> collapses away, and it is
    /// what integer-range and date-floor rules are derived from.
    /// </summary>
    public string? DeclaredType { get; init; }
}
```

- [ ] **Step 4: Add `Facts` to the three carriers**

In `CodeGenerationEngine/Models/PropertyDefinition.cs`, after `Attributes`:

```csharp
    /// <summary>
    /// What the source declared about this property's column, or null when nothing is known.
    /// Read by DomainModelMapper.InferValidation to derive rules from the type rather than the
    /// name.
    /// </summary>
    public TypeFacts? Facts { get; set; }
```

In `DomainDiscovery/Model/CandidateDomainModel.cs`, inside `CandidateField`, after `Provenance`:

```csharp
    /// <summary>
    /// What the source declared about this field's column, carried from the reader so a rename in
    /// the review loop cannot orphan it. Null for a field no data source produced.
    /// </summary>
    public CodeGenerationEngine.Models.TypeFacts? Facts { get; set; }
```

In `DataIngestion/Schema/TabularColumn.cs`, after `Type`:

```csharp
    /// <summary>
    /// What the source declared about this column. Settable for the same reason
    /// <see cref="Type"/> is: a reader produces the name, and the schema source or
    /// <see cref="ColumnTypeInference"/> fills the facts in afterwards.
    /// </summary>
    public CodeGenerationEngine.Models.TypeFacts? Facts { get; set; }
```

- [ ] **Step 5: Add the project reference**

In `DomainDiscovery/DomainDiscovery.csproj`, alongside the existing `NaturalLanguageEngine` reference:

```xml
    <ProjectReference Include="..\CodeGenerationEngine\CodeGenerationEngine.csproj" />
```

`CodeGenerationEngine` references no project, so this cannot cycle.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter TypeFactsCarrierTests`
Expected: PASS, 3 tests.

- [ ] **Step 7: Verify the whole solution still builds under CI's flags**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`
Expected: Build succeeded. The new project reference is the risk here — a cycle or a duplicated transitive reference shows up now, not later.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerationEngine/Models/TypeFacts.cs CodeGenerationEngine/Models/PropertyDefinition.cs DomainDiscovery/DomainDiscovery.csproj DomainDiscovery/Model/CandidateDomainModel.cs DataIngestion/Schema/TabularColumn.cs CodeGenerator.Tests/App/TypeFactsCarrierTests.cs
git commit -m "Add TypeFacts, and let all three column carriers hold it"
```

---

### Task 2: SQL Server reads the facts it was discarding

**Files:**
- Modify: `DataIngestion/Schema/Readers/Sql/ISqlSchemaSource.cs:12`
- Modify: `DataIngestion/Schema/Readers/Sql/SqlClientSchemaSource.cs:54-73`
- Modify: `DataIngestion/Schema/Readers/Sql/SqlServerReader.cs:37-48`
- Test: `DataIngestion.Tests/Schema/SqlTypeFactsTests.cs`

**Interfaces:**
- Consumes: `TypeFacts` from Task 1.
- Produces: `SqlColumnInfo(string Name, string SqlType, bool Nullable, int? MaxLength = null, int? Precision = null, int? Scale = null)`. The optional parameters keep every existing construction and every existing test fake compiling unchanged.
- Produces: `SqlServerReader` sets `TabularColumn.Facts` for every column.

- [ ] **Step 1: Write the failing test**

Create `DataIngestion.Tests/Schema/SqlTypeFactsTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers.Sql;

namespace DataIngestion.Tests.Schema;

/// <summary>A schema source that answers from memory, so these tests need no database.</summary>
internal sealed class FakeSchemaSource : ISqlSchemaSource
{
    public required IReadOnlyList<SqlColumnInfo> ColumnList { get; init; }

    public IReadOnlyList<SqlColumnInfo> Columns(string connectionString, string schema, string table)
        => ColumnList;

    public IReadOnlyList<TabularTableInfo> Tables(string connectionString) =>
        [new TabularTableInfo("dbo", "Customer")];

    public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString) => [];
}

[TestClass]
public class SqlTypeFactsTests
{
    [TestMethod]
    public void NvarcharFifty_CarriesItsRealLength()
    {
        // The defect this whole phase exists to fix: today every string is bounded at 200,
        // so a generated app accepts 120 characters into a column that holds 50.
        var facts = FactsFor(new SqlColumnInfo("Surname", "nvarchar", Nullable: false, MaxLength: 50));

        Assert.AreEqual(50, facts.MaxLength);
        Assert.IsFalse(facts.IsUnbounded);
        Assert.IsFalse(facts.IsNullable!.Value);
    }

    [TestMethod]
    public void NvarcharMax_IsUnbounded_NotLengthMinusOne()
    {
        // INFORMATION_SCHEMA reports -1 for max. Carried through raw it would emit
        // "must be -1 characters or fewer", which rejects every value.
        var facts = FactsFor(new SqlColumnInfo("Notes", "nvarchar", Nullable: true, MaxLength: -1));

        Assert.IsTrue(facts.IsUnbounded);
        Assert.IsNull(facts.MaxLength);
    }

    [TestMethod]
    public void TinyintAndSmallint_KeepTheirDeclaredType()
    {
        // SqlTypeMap collapses both to "int". The declared name is the only thing
        // that can produce a 0-255 or a -32768-32767 bound.
        Assert.AreEqual("tinyint", FactsFor(new SqlColumnInfo("Rating", "tinyint", false)).DeclaredType);
        Assert.AreEqual("smallint", FactsFor(new SqlColumnInfo("Year", "smallint", false)).DeclaredType);
    }

    [TestMethod]
    public void Decimal_KeepsPrecisionAndScale()
    {
        var facts = FactsFor(
            new SqlColumnInfo("Price", "decimal", Nullable: false, Precision: 18, Scale: 2));

        Assert.AreEqual(18, facts.Precision);
        Assert.AreEqual(2, facts.Scale);
    }

    private static TypeFacts FactsFor(SqlColumnInfo column)
    {
        var reader = new SqlServerReader(new FakeSchemaSource { ColumnList = [column] });
        var table = reader.Read(new SourceSpec { Location = "irrelevant" }).Tables[0];
        return table.Columns[0].Facts!;
    }
}
```

> The implementer must check `SqlServerReader`'s real constructor and `Read` signature and the real shape of `TabularTableInfo` / `SourceSpec` before running this, and adjust the helper only — not the assertions. The four assertions are the contract.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter SqlTypeFactsTests`
Expected: FAIL to compile — `SqlColumnInfo` has no `MaxLength`, `Precision` or `Scale` parameter.

- [ ] **Step 3: Widen `SqlColumnInfo`**

In `DataIngestion/Schema/Readers/Sql/ISqlSchemaSource.cs`, replace line 12:

```csharp
/// <summary>
/// One column as INFORMATION_SCHEMA describes it. The trailing three are optional so every
/// existing construction and every test fake keeps compiling; a source that cannot supply them
/// leaves them null, which reads as "not known".
/// </summary>
public sealed record SqlColumnInfo(
    string Name,
    string SqlType,
    bool Nullable,
    int? MaxLength = null,
    int? Precision = null,
    int? Scale = null);
```

- [ ] **Step 4: Ask for the three columns**

In `DataIngestion/Schema/Readers/Sql/SqlClientSchemaSource.cs`, replace the `sql` constant and the row mapping in `Columns` (lines 54-69):

```csharp
        const string sql = """
            SELECT  COLUMN_NAME, DATA_TYPE, IS_NULLABLE,
                    CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
            FROM    INFORMATION_SCHEMA.COLUMNS
            WHERE   TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;

        var columns = new List<SqlColumnInfo>();

        foreach (var row in Query(connectionString, sql, ("@schema", schema), ("@table", table)))
        {
            columns.Add(new SqlColumnInfo(
                (string)row["COLUMN_NAME"],
                (string)row["DATA_TYPE"],
                string.Equals(
                    (string)row["IS_NULLABLE"], "YES", StringComparison.OrdinalIgnoreCase),
                AsInt(row["CHARACTER_MAXIMUM_LENGTH"]),
                AsInt(row["NUMERIC_PRECISION"]),
                AsInt(row["NUMERIC_SCALE"])));
        }

        return columns;
```

And add, beside the other private helpers in the same class:

```csharp
    /// <summary>
    /// One INFORMATION_SCHEMA numeric, or null. The columns are DBNull for types they do not
    /// apply to, and their declared types vary — NUMERIC_PRECISION is tinyint, and
    /// CHARACTER_MAXIMUM_LENGTH is int — so an unboxing cast to int would throw on real schemas.
    /// </summary>
    private static int? AsInt(object? value)
        => value is null || value is DBNull ? null : Convert.ToInt32(value);
```

- [ ] **Step 5: Fill `TabularColumn.Facts`**

In `DataIngestion/Schema/Readers/Sql/SqlServerReader.cs`, replace the column loop body at lines 37-43:

```csharp
            foreach (var column in source.Columns(spec.Location, info.Schema, info.Name))
            {
                table.Columns.Add(new TabularColumn
                {
                    Name = column.Name,
                    Type = SqlTypeMap.ToModelType(column.SqlType),
                    Facts = new TypeFacts
                    {
                        IsNullable = column.Nullable,
                        // -1 is INFORMATION_SCHEMA's spelling of nvarchar(max): a known
                        // absence of a bound, not a bound of -1.
                        MaxLength = column.MaxLength is > 0 ? column.MaxLength : null,
                        IsUnbounded = column.MaxLength == -1,
                        Precision = column.Precision,
                        Scale = column.Scale,
                        DeclaredType = column.SqlType
                    }
                });
```

Add `using CodeGenerationEngine.Models;` to the file's usings.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter SqlTypeFactsTests`
Expected: PASS, 4 tests.

- [ ] **Step 7: Run the existing SQL reader tests, which must be unaffected**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter SqlServerReader`
Expected: PASS, no regressions. The optional record parameters are what keeps this green.

- [ ] **Step 8: Commit**

```bash
git add DataIngestion/Schema/Readers/Sql/ DataIngestion.Tests/Schema/SqlTypeFactsTests.cs
git commit -m "Ask SQL Server for the length, precision and scale it was never asked for"
```

---

### Task 3: File sources record what they saw while scanning

CSV, Excel and JSON declare nothing, but `ColumnTypeInference` already reads values to decide the type. It records what it saw on the way past. This is evidence over a **complete** pass, not the 200-value type sample — the sample decides the type, the full pass decides the facts, and conflating them is what would let a rule reject the source's own data.

**Files:**
- Modify: `DataIngestion/Schema/ColumnTypeInference.cs`
- Modify: `DataIngestion/Schema/Readers/CsvReader.cs:158`
- Modify: `DataIngestion/Schema/Readers/JsonTableReader.cs:107`
- Test: `DataIngestion.Tests/Schema/ColumnEvidenceTests.cs`

**Interfaces:**
- Consumes: `TypeFacts` from Task 1.
- Produces: `ColumnTypeInference.Evidence(IEnumerable<string?> values, string inferredType)` returning `TypeFacts`, where `MaxLength` is the longest value seen, `Scale` is the greatest number of decimal places seen, and `IsNullable` is true when any value was blank.

- [ ] **Step 1: Write the failing test**

Create `DataIngestion.Tests/Schema/ColumnEvidenceTests.cs`:

```csharp
using DataIngestion.Schema;

namespace DataIngestion.Tests.Schema;

[TestClass]
public class ColumnEvidenceTests
{
    [TestMethod]
    public void LongestValueSeen_BecomesTheLengthBound()
    {
        var facts = ColumnTypeInference.Evidence(["ab", "abcdef", "abc"], "string");

        Assert.AreEqual(6, facts.MaxLength);
    }

    [TestMethod]
    public void EveryValuePresent_MeansNotNullable()
    {
        var facts = ColumnTypeInference.Evidence(["a", "b"], "string");

        Assert.IsFalse(facts.IsNullable!.Value);
    }

    [TestMethod]
    public void OneBlank_MeansNullable()
    {
        // A single blank is proof the column tolerates one. Requiring the field would
        // reject a row the source already contains.
        var facts = ColumnTypeInference.Evidence(["a", "", "b"], "string");

        Assert.IsTrue(facts.IsNullable!.Value);
    }

    [TestMethod]
    public void GreatestDecimalPlacesSeen_BecomesTheScale()
    {
        var facts = ColumnTypeInference.Evidence(["1.5", "2.25", "3"], "decimal");

        Assert.AreEqual(2, facts.Scale);
    }

    [TestMethod]
    public void EvidenceScansEveryValue_NotTheTypeSample()
    {
        // The type sample stops at 200. If evidence stopped there too, a longer value
        // further down would produce a bound that rejects the source's own data.
        var values = Enumerable.Repeat("short", 500).Append("a much longer value here").ToList();

        var facts = ColumnTypeInference.Evidence(values, "string");

        Assert.AreEqual("a much longer value here".Length, facts.MaxLength);
    }

    [TestMethod]
    public void NonStringTypes_CarryNoLengthBound()
    {
        // "3" being one character is not a bound on an int. Emitting one would be nonsense.
        var facts = ColumnTypeInference.Evidence(["3", "42"], "int");

        Assert.IsNull(facts.MaxLength);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter ColumnEvidenceTests`
Expected: FAIL to compile — `ColumnTypeInference.Evidence` does not exist.

- [ ] **Step 3: Add `Evidence`**

Add to `DataIngestion/Schema/ColumnTypeInference.cs`:

```csharp
    /// <summary>
    /// What a complete pass over <paramref name="values"/> proves about the column, for a source
    /// that declares nothing.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT limited to <see cref="TypeSampleSize"/>. The sample decides the type,
    /// which is a property of the column's domain; the facts here become RULES, and a rule drawn
    /// from a sample can reject data the source itself contains — a longer value at row 50,000
    /// would be refused by a bound learned from row 1.
    /// </remarks>
    public static TypeFacts Evidence(IEnumerable<string?> values, string inferredType)
    {
        var longest = 0;
        var scale = 0;
        var sawBlank = false;

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                sawBlank = true;
                continue;
            }

            if (value.Length > longest)
                longest = value.Length;

            var point = value.IndexOf('.');

            if (point >= 0 && value.Length - point - 1 > scale)
                scale = value.Length - point - 1;
        }

        return new TypeFacts
        {
            IsNullable = sawBlank,
            // A length bound belongs only to text. "42" being two characters says nothing
            // about an int, and a bound emitted from it would be meaningless.
            MaxLength = inferredType == "string" && longest > 0 ? longest : null,
            Scale = inferredType == "decimal" ? scale : null
        };
    }
```

Add `using CodeGenerationEngine.Models;` to the file's usings.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter ColumnEvidenceTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Call it from the file readers**

Find where each reader fills `TabularColumn.Type` from `ColumnTypeInference` (the readers create columns at `CsvReader.cs:158` and `JsonTableReader.cs:107` and type them in a later pass). At that same site, set `Facts`:

```csharp
                column.Facts = ColumnTypeInference.Evidence(valuesForColumn, column.Type);
```

where `valuesForColumn` is the same complete value sequence the reader already holds for that column. Do not re-read the file.

- [ ] **Step 6: Run the full ingestion suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter DataIngestion`
Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add DataIngestion/Schema/ColumnTypeInference.cs DataIngestion/Schema/Readers/ DataIngestion.Tests/Schema/ColumnEvidenceTests.cs
git commit -m "Record what a complete scan proves about a file column"
```

---

### Task 4: The facts reach `PropertyDefinition`

Two copies, at the two places a column changes carrier.

**Files:**
- Modify: `DataIngestion/DataModelBuilder.cs:257-261`
- Modify: `AppScaffolding/DomainModelMapper.cs:121-123`
- Test: `CodeGenerator.Tests/App/TypeFactsReachPropertiesTests.cs`

**Interfaces:**
- Consumes: `TabularColumn.Facts`, `CandidateField.Facts`, `PropertyDefinition.Facts` from Task 1.
- Produces: facts present on `PropertyDefinition` for every column a data source supplied.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/TypeFactsReachPropertiesTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TypeFactsReachPropertiesTests
{
    [TestMethod]
    public void FactsOnAField_ArriveOnItsProperty()
    {
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(new CandidateField
        {
            Name = "Surname",
            Type = "string",
            Facts = new TypeFacts { MaxLength = 50, IsNullable = false }
        });
        model.Entities.Add(entity);

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var surname = classes.Single(c => c.Name == "Customer")
            .Properties.Single(p => p.Name == "Surname");

        Assert.AreEqual(50, surname.Facts!.MaxLength);
        Assert.IsFalse(surname.Facts.IsNullable!.Value);
    }

    [TestMethod]
    public void AFieldWithNoFacts_YieldsAPropertyWithNone()
    {
        // A discovered domain supplies no facts and must still map cleanly.
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(new CandidateField { Name = "Surname", Type = "string" });
        model.Entities.Add(entity);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.IsNull(classes.Single(c => c.Name == "Customer")
            .Properties.Single(p => p.Name == "Surname").Facts);
    }

    [TestMethod]
    public void RenamingAField_KeepsItsFacts()
    {
        // CandidateField.Name is settable so the review loop can rename. Facts ride on the
        // field object, so a rename cannot orphan them — which a name-keyed side table would.
        var field = new CandidateField
        {
            Name = "Surname",
            Type = "string",
            Facts = new TypeFacts { MaxLength = 50 }
        };

        field.Name = "FamilyName";

        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(field);
        model.Entities.Add(entity);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.AreEqual(50, classes.Single(c => c.Name == "Customer")
            .Properties.Single(p => p.Name == "FamilyName").Facts!.MaxLength);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter TypeFactsReachPropertiesTests`
Expected: FAIL — `Facts` is null on the mapped property (`Assert.AreEqual(50, ...)` fails with a null reference on `.MaxLength`).

- [ ] **Step 3: Copy facts column → field**

In `DataIngestion/DataModelBuilder.cs`, in the `entity.Fields.Add(new CandidateField { ... })` at line 257, add:

```csharp
            entity.Fields.Add(new CandidateField
            {
                Name = outcome.Field!,
                Type = outcome.Type,
                Provenance = Provenance.Data,
                Facts = outcome.Column is null
                    ? null
                    : table.Columns.FirstOrDefault(c => c.Name == outcome.Column)?.Facts
            });
```

> The implementer must confirm what `outcome` exposes — if it already carries the `TabularColumn` rather than its name, use that directly rather than looking it up by name.

- [ ] **Step 4: Copy facts field → property**

In `AppScaffolding/DomainModelMapper.cs`, replace the field loop at lines 121-123:

```csharp
            foreach (var field in entity.Fields)
                definition.Properties.Add(new PropertyDefinition
                {
                    Name = field.Name,
                    Type = field.Type,
                    Facts = field.Facts
                });
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter TypeFactsReachPropertiesTests`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add DataIngestion/DataModelBuilder.cs AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/App/TypeFactsReachPropertiesTests.cs
git commit -m "Carry type facts from the column through to the property"
```

---

### Task 5: The emitter handles every rule kind, and says so when it cannot

This comes **before** inference changes, on purpose. `ValidatorTemplate.Checks` opens with `if (property.Type != "string") return checks;` — so a range rule recorded on an `int` today would be silently dropped, and the tool would report success over a check that is not there. Widening the emitter first means Task 6 cannot introduce that failure.

The guard is the important half: a recorded rule kind with no emitter must fail a test, not vanish.

**Files:**
- Modify: `AppScaffolding/ValidatorTemplate.cs:298` (`Checks`)
- Test: `CodeGenerator.Tests/App/ValidatorEmitsEveryRuleKindTests.cs`

**Interfaces:**
- Consumes: `PropertyRule` (existing: `Name`, `MinLength`, `MaxLength`, `MinValue`, `MaxValue`, `AllowedValues`, `RegexPattern`).
- Produces: two new `PropertyRule` fields, `int? Scale` and `string? MinDate`. Decimal places are not a `MaxLength`, and a date floor is not an `ExpectedType` — reusing those would give a later reader two meanings for one field, which is how a rule ends up read by the wrong emitter.
- Produces: `ValidatorTemplate.EmittableRuleNames` — an `IReadOnlyCollection<string>` of every rule name `Checks` can emit. Task 6's inference is tested against it.
- Produces: `Checks` emits for non-string properties too.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ValidatorEmitsEveryRuleKindTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ValidatorEmitsEveryRuleKindTests
{
    [TestMethod]
    public void AMinValueRuleOnAnInt_IsEmitted()
    {
        // Today Checks returns early for any non-string property, so this rule
        // disappears and the generated validator reports success over nothing.
        var source = GeneratedFor("Rating", "int",
            new PropertyRule { Name = "minvalue", MinValue = 0 });

        StringAssert.Contains(source, "item.Rating < 0");
    }

    [TestMethod]
    public void AMaxValueRuleOnAnInt_IsEmitted()
    {
        var source = GeneratedFor("Rating", "int",
            new PropertyRule { Name = "maxvalue", MaxValue = 255 });

        StringAssert.Contains(source, "item.Rating > 255");
    }

    [TestMethod]
    public void AScaleRuleOnADecimal_IsEmitted()
    {
        var source = GeneratedFor("Price", "decimal",
            new PropertyRule { Name = "scale", Scale = 2 });

        StringAssert.Contains(source, "decimal.Round(item.Price, 2)");
    }

    [TestMethod]
    public void AMinDateRuleOnADateTime_IsEmitted()
    {
        var source = GeneratedFor("OrderedOn", "DateTime",
            new PropertyRule { Name = "mindate", MinDate = "1753-01-01" });

        StringAssert.Contains(source, "1753");
    }

    [TestMethod]
    public void ADateOnlyRuleOnADateTime_IsEmitted()
    {
        var source = GeneratedFor("DueOn", "DateTime", new PropertyRule { Name = "dateonly" });

        StringAssert.Contains(source, "item.DueOn.TimeOfDay");
    }

    [TestMethod]
    public void EveryRuleNameInferenceCanRecord_HasAnEmitter()
    {
        // The anti-silent-drop guard. If a later task teaches inference a new rule name
        // and forgets the emitter, this goes red instead of shipping a missing check.
        foreach (var name in ValidatorTemplate.EmittableRuleNames)
            Assert.IsFalse(string.IsNullOrWhiteSpace(name));

        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("required"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("maxlength"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("minvalue"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("maxvalue"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("scale"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("mindate"));
        Assert.IsTrue(ValidatorTemplate.EmittableRuleNames.Contains("dateonly"));
    }

    private static string GeneratedFor(string propertyName, string type, PropertyRule rule)
    {
        var definition = new ClassDefinition { Name = "Product" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var property = new PropertyDefinition { Name = propertyName, Type = type };
        property.Rules.Add(rule);
        definition.Properties.Add(property);

        return ValidatorTemplate.Generated("Acme", definition, []);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter ValidatorEmitsEveryRuleKindTests`
Expected: FAIL — `EmittableRuleNames` does not exist, and the four emit tests find nothing because `Checks` returns early for non-string types.

- [ ] **Step 3: Give `PropertyRule` the two fields these rules need**

In `CodeGenerationEngine/Models/PropertyRule.cs`, add beside the existing bounds:

```csharp
    /// <summary>Digits allowed after the decimal point. Distinct from MaxLength, which counts
    /// characters — one rule reading the other's field is how a check lands on the wrong
    /// property kind.</summary>
    public int? Scale { get; set; }

    /// <summary>The earliest date allowed, ISO-8601. A string rather than a DateTime so the
    /// recorded rule and the emitted literal are the same text.</summary>
    public string? MinDate { get; set; }
```

- [ ] **Step 4: Widen `Checks`**

In `AppScaffolding/ValidatorTemplate.cs`, add the rule-name constants beside the existing `MaxLengthRule`:

```csharp
    private const string MinValueRule = "minvalue";
    private const string MaxValueRule = "maxvalue";
    private const string ScaleRule = "scale";
    private const string MinDateRule = "mindate";
    private const string DateOnlyRule = "dateonly";

    /// <summary>
    /// Every rule name <see cref="Checks"/> knows how to emit. Inference is tested against this,
    /// so a rule kind recorded with no emitter fails a test rather than vanishing into a
    /// generated validator that reports success over a check that is not there.
    /// </summary>
    public static IReadOnlyCollection<string> EmittableRuleNames { get; } =
    [
        RequiredRule, MaxLengthRule, MinValueRule, MaxValueRule,
        ScaleRule, MinDateRule, DateOnlyRule
    ];
```

Then replace the early return at the top of `Checks` and append the new emitters. The string-only checks stay exactly as they are, guarded by the type:

```csharp
    private static List<string> Checks(PropertyDefinition property)
    {
        var checks = new List<string>();
        var name = property.Name;

        if (property.Type == "string")
        {
            // ... every existing string check, unchanged ...
        }

        if (property.Type is "int" or "decimal")
        {
            var min = Rule(property, MinValueRule)?.MinValue;

            if (min.HasValue)
            {
                checks.Add($"if (item.{name} < {Literal(min.Value, property.Type)})");
                checks.Add($"    errors.Add(\"{name} must be {min.Value} or more.\");");
            }

            var max = Rule(property, MaxValueRule)?.MaxValue;

            if (max.HasValue)
            {
                checks.Add($"if (item.{name} > {Literal(max.Value, property.Type)})");
                checks.Add($"    errors.Add(\"{name} must be {max.Value} or less.\");");
            }
        }

        if (property.Type == "decimal" && Rule(property, ScaleRule)?.Scale is { } places)
        {
            checks.Add($"if (decimal.Round(item.{name}, {places}) != item.{name})");
            checks.Add($"    errors.Add(\"{name} must have {places} decimal places or fewer.\");");
        }

        if (property.Type == "DateTime")
        {
            if (Rule(property, MinDateRule)?.MinDate is { } floor)
            {
                checks.Add($"if (item.{name} < DateTime.Parse(\"{floor}\", System.Globalization.CultureInfo.InvariantCulture))");
                checks.Add($"    errors.Add(\"{name} must be on or after {floor}.\");");
            }

            if (Rule(property, DateOnlyRule) is not null)
            {
                checks.Add($"if (item.{name}.TimeOfDay != TimeSpan.Zero)");
                checks.Add($"    errors.Add(\"{name} must be a date with no time of day.\");");
            }
        }

        return checks;
    }

    private static PropertyRule? Rule(PropertyDefinition property, string ruleName)
        => property.Rules.FirstOrDefault(
            r => string.Equals(r.Name, ruleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A decimal literal that compiles against the property's own type. Emitting a bare
    /// <c>255</c> against a decimal is fine, but <c>2.5</c> against one needs the <c>m</c>
    /// suffix or the generated file will not compile.
    /// </summary>
    private static string Literal(decimal value, string type)
        => type == "decimal"
            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m"
            : ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter ValidatorEmitsEveryRuleKindTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Prove the emitted code actually compiles**

Add to the same test class:

```csharp
    [TestMethod]
    public void EveryNewRuleKind_EmitsCodeThatCompiles()
    {
        // Substring assertions above prove the check is PRESENT. Only compiling proves
        // it is valid C# — a decimal literal without its m suffix passes the first and
        // fails the second.
        var definition = new ClassDefinition { Name = "Product" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var rating = new PropertyDefinition { Name = "Rating", Type = "int" };
        rating.Rules.Add(new PropertyRule { Name = "minvalue", MinValue = 0 });
        rating.Rules.Add(new PropertyRule { Name = "maxvalue", MaxValue = 255 });
        definition.Properties.Add(rating);

        var price = new PropertyDefinition { Name = "Price", Type = "decimal" };
        price.Rules.Add(new PropertyRule { Name = "scale", Scale = 2 });
        // A non-integral bound on a decimal: this is the one that fails to compile
        // without the m suffix, and passes a substring assertion either way.
        price.Rules.Add(new PropertyRule { Name = "maxvalue", MaxValue = 2.5m });
        definition.Properties.Add(price);

        var ordered = new PropertyDefinition { Name = "OrderedOn", Type = "DateTime" };
        ordered.Rules.Add(new PropertyRule { Name = "mindate", MinDate = "1753-01-01" });
        ordered.Rules.Add(new PropertyRule { Name = "dateonly" });
        definition.Properties.Add(ordered);

        // Both halves together: the generated validator and the model it validates.
        var result = Compiler.CompileSources(
            ValidatorTemplate.Generated("Acme", definition, []),
            ModelSourceFor(definition),
            ValidatorTemplate.ValidatorInterface("Acme"));

        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
    }
```

> The implementer must reuse whatever helper `ValidatorCompilesTests.cs` already uses to build the model source and call the compiler, rather than writing `ModelSourceFor` fresh. Match that file's existing pattern exactly.

- [ ] **Step 7: Run it**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter ValidatorEmitsEveryRuleKindTests`
Expected: PASS, 7 tests.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerationEngine/Models/PropertyRule.cs AppScaffolding/ValidatorTemplate.cs CodeGenerator.Tests/App/ValidatorEmitsEveryRuleKindTests.cs
git commit -m "Emit checks for non-string rules, and fail a test when a rule kind has no emitter"
```

---

### Task 6: Type-driven inference replaces the name matching

**Files:**
- Modify: `AppScaffolding/DomainModelMapper.cs:240-262` (`InferValidation`)
- Test: `CodeGenerator.Tests/App/TypeDrivenInferenceTests.cs`

**Interfaces:**
- Consumes: `PropertyDefinition.Facts` (Task 4), `ValidatorTemplate.EmittableRuleNames` (Task 5).
- Produces: rules recorded on every property with facts, of the kinds listed in the spec's Field-stage table.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/TypeDrivenInferenceTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TypeDrivenInferenceTests
{
    [TestMethod]
    public void ANvarcharFifty_IsBoundedAtFifty_NotTwoHundred()
    {
        // The shipped defect: a generated app accepts 120 characters into a column
        // that holds 50, and the database rejects the insert.
        var property = Mapped("Surname", "string", new TypeFacts { MaxLength = 50 });

        Assert.AreEqual(50, Rule(property, "maxlength")!.MaxLength);
    }

    [TestMethod]
    public void AnUnboundedColumn_GetsNoLengthRule()
    {
        var property = Mapped("Notes", "string", new TypeFacts { IsUnbounded = true });

        Assert.IsNull(Rule(property, "maxlength"));
    }

    [TestMethod]
    public void AColumnWithNoKnownLength_KeepsTheTwoHundredFallback()
    {
        // A discovered domain has no facts. It must still generate, and a bound is
        // better than none for a column that will back a text box.
        var property = Mapped("Surname", "string", facts: null);

        Assert.AreEqual(200, Rule(property, "maxlength")!.MaxLength);
    }

    [TestMethod]
    public void ANotNullColumn_IsRequired_WhateverItIsCalled()
    {
        // Today only "Name" and "Title" are required, by name equality.
        var property = Mapped("Surname", "string", new TypeFacts { IsNullable = false });

        Assert.IsNotNull(Rule(property, "required"));
    }

    [TestMethod]
    public void ANullableColumn_IsNotRequired_EvenWhenCalledName()
    {
        var property = Mapped("Name", "string", new TypeFacts { IsNullable = true });

        Assert.IsNull(Rule(property, "required"));
    }

    [TestMethod]
    public void Tinyint_IsBoundedZeroToTwoFiveFive()
    {
        var property = Mapped("Rating", "int", new TypeFacts { DeclaredType = "tinyint" });

        Assert.AreEqual(0m, Rule(property, "minvalue")!.MinValue);
        Assert.AreEqual(255m, Rule(property, "maxvalue")!.MaxValue);
    }

    [TestMethod]
    public void Smallint_IsBoundedToItsRange()
    {
        var property = Mapped("Year", "int", new TypeFacts { DeclaredType = "smallint" });

        Assert.AreEqual(-32768m, Rule(property, "minvalue")!.MinValue);
        Assert.AreEqual(32767m, Rule(property, "maxvalue")!.MaxValue);
    }

    [TestMethod]
    public void DecimalEighteenTwo_LimitsItsDecimalPlaces()
    {
        var property = Mapped("Price", "decimal",
            new TypeFacts { DeclaredType = "decimal", Precision = 18, Scale = 2 });

        Assert.AreEqual(2, Rule(property, "scale")!.Scale);
    }

    [TestMethod]
    public void DecimalEighteenTwo_AlsoLimitsTheDigitsBeforeThePoint()
    {
        // decimal(18,2) overflows at 10^16. Without this the app accepts the value
        // and the database refuses the insert — the same shape as the length defect.
        var property = Mapped("Price", "decimal",
            new TypeFacts { DeclaredType = "decimal", Precision = 18, Scale = 2 });

        Assert.AreEqual(9999999999999999.99m, Rule(property, "maxvalue")!.MaxValue);
        Assert.AreEqual(-9999999999999999.99m, Rule(property, "minvalue")!.MinValue);
    }

    [TestMethod]
    public void ADecimalFromAFile_GetsItsScaleButNoPrecisionBound()
    {
        // Evidence gives a scale. It cannot give a precision, and inventing one
        // would reject a larger value the source may legitimately grow.
        var property = Mapped("Price", "decimal", new TypeFacts { Scale = 2 });

        Assert.AreEqual(2, Rule(property, "scale")!.Scale);
        Assert.IsNull(Rule(property, "maxvalue"));
    }

    [TestMethod]
    public void Datetime_GetsSqlServersOwnFloor()
    {
        var property = Mapped("OrderedOn", "DateTime", new TypeFacts { DeclaredType = "datetime" });

        Assert.AreEqual("1753-01-01", Rule(property, "mindate")!.MinDate);
    }

    [TestMethod]
    public void Datetime2_GetsNoFloor()
    {
        // datetime2 starts at year 1. Borrowing datetime's floor would refuse
        // dates the column accepts.
        var property = Mapped("OrderedOn", "DateTime", new TypeFacts { DeclaredType = "datetime2" });

        Assert.IsNull(Rule(property, "mindate"));
    }

    [TestMethod]
    public void ADateColumn_RejectsATimeOfDay()
    {
        var property = Mapped("DueOn", "DateTime", new TypeFacts { DeclaredType = "date" });

        Assert.IsNotNull(Rule(property, "dateonly"));
    }

    [TestMethod]
    public void ABitColumn_GetsNoRules()
    {
        // A bool cannot be invalid.
        var property = Mapped("IsActive", "bool", new TypeFacts { DeclaredType = "bit", IsNullable = false });

        Assert.HasCount(0, property.Rules);
    }

    [TestMethod]
    public void EveryRuleInferenceRecords_HasAnEmitter()
    {
        // Closes the silent-drop risk across the whole table above: any rule name
        // inference produces that ValidatorTemplate cannot emit fails here.
        var properties = new[]
        {
            Mapped("Surname", "string", new TypeFacts { MaxLength = 50, IsNullable = false }),
            Mapped("Rating", "int", new TypeFacts { DeclaredType = "tinyint" }),
            Mapped("Price", "decimal", new TypeFacts { DeclaredType = "decimal", Precision = 18, Scale = 2 }),
            Mapped("OrderedOn", "DateTime", new TypeFacts { DeclaredType = "datetime" }),
            Mapped("DueOn", "DateTime", new TypeFacts { DeclaredType = "date" })
        };

        foreach (var property in properties)
            foreach (var rule in property.Rules)
                Assert.IsTrue(
                    ValidatorTemplate.EmittableRuleNames.Contains(rule.Name),
                    $"Inference records '{rule.Name}' but ValidatorTemplate cannot emit it.");
    }

    private static PropertyDefinition Mapped(string name, string type, TypeFacts? facts)
    {
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(new CandidateField { Name = name, Type = type, Facts = facts });
        model.Entities.Add(entity);

        return DomainModelMapper.ToClassDefinitions(model)
            .Single(c => c.Name == "Customer").Properties.Single(p => p.Name == name);
    }

    private static PropertyRule? Rule(PropertyDefinition property, string ruleName)
        => property.Rules.FirstOrDefault(
            r => string.Equals(r.Name, ruleName, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter TypeDrivenInferenceTests`
Expected: FAIL — most of the 15 fail. `AColumnWithNoKnownLength...`, `Datetime2_GetsNoFloor` and `ABitColumn...` may already pass, which is correct: they assert behaviour that must survive the rewrite rather than behaviour it introduces.

- [ ] **Step 3: Rewrite `InferValidation`**

In `AppScaffolding/DomainModelMapper.cs`, replace `InferValidation` (lines 240-262 and its remainder) with:

```csharp
    /// <summary>
    /// Applies the inference rules to one property, mutating its Rules/Attributes in place, and
    /// returns whether it added a DataAnnotations attribute.
    /// </summary>
    /// <remarks>
    /// Driven by the property's declared type and the facts the source supplied, not by its name.
    /// A navigation property (typed as another class or a List&lt;&gt;) matches none of the five
    /// emitted types and is left alone. Every rule name recorded here must appear in
    /// <see cref="ValidatorTemplate.EmittableRuleNames"/>, or it is dropped silently.
    /// </remarks>
    private static bool InferValidation(PropertyDefinition property)
    {
        if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
            && property.Type == "int")
        {
            property.Attributes.Add("Key");
            return true;
        }

        var facts = property.Facts;

        // A column the source declared NOT NULL is required, whatever it is called. Unknown
        // nullability infers nothing: a discovered domain must not become stricter than the
        // user described it.
        if (facts?.IsNullable == false && property.Type != "bool")
            property.Rules.Add(new PropertyRule { Name = "required" });

        switch (property.Type)
        {
            case "string":
                if (!(facts?.IsUnbounded ?? false))
                    property.Rules.Add(new PropertyRule
                    {
                        Name = "maxlength",
                        MaxLength = facts?.MaxLength ?? DefaultStringLength
                    });

                return AddFormatAttribute(property);

            case "int":
                AddIntegerRange(property, facts?.DeclaredType);
                return false;

            case "decimal":
                AddDecimalRules(property, facts);
                return false;

            case "DateTime":
                // SQL Server's own floor, and only for the type that actually carries it:
                // datetime2 starts at year 1, and a rule from the wrong type would refuse
                // dates the column accepts.
                if (string.Equals(facts?.DeclaredType, "datetime", StringComparison.OrdinalIgnoreCase))
                    property.Rules.Add(new PropertyRule { Name = "mindate", MinDate = "1753-01-01" });

                if (string.Equals(facts?.DeclaredType, "date", StringComparison.OrdinalIgnoreCase))
                    property.Rules.Add(new PropertyRule { Name = "dateonly" });

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// What decimal(P,S) actually constrains: S digits after the point, and P-S before it.
    /// </summary>
    /// <remarks>
    /// The integer-part bound is the half that is easy to forget and the one the database will
    /// enforce anyway — decimal(18,2) overflows at 10^16, and without this rule the app accepts
    /// the value and the insert fails. A file source supplies a scale from evidence but no
    /// precision, so it gets the scale rule alone.
    /// </remarks>
    private static void AddDecimalRules(PropertyDefinition property, TypeFacts? facts)
    {
        if (facts?.Scale is { } scale)
            property.Rules.Add(new PropertyRule { Name = "scale", Scale = scale });

        if (facts?.Precision is not { } precision || facts.Scale is not { } places)
            return;

        var integerDigits = precision - places;

        if (integerDigits <= 0 || integerDigits > 28)
            return;

        var bound = decimal.Parse(
            new string('9', integerDigits) + "." + new string('9', places),
            System.Globalization.CultureInfo.InvariantCulture);

        property.Rules.Add(new PropertyRule { Name = "maxvalue", MaxValue = bound });
        property.Rules.Add(new PropertyRule { Name = "minvalue", MinValue = -bound });
    }

    /// <summary>The bounds SqlTypeMap collapses away when it maps every integer width to int.</summary>
    private static void AddIntegerRange(PropertyDefinition property, string? declaredType)
    {
        var (min, max) = declaredType?.ToLowerInvariant() switch
        {
            "tinyint" => (0m, 255m),
            "smallint" => (-32768m, 32767m),
            _ => (0m, 0m)
        };

        if (min == 0m && max == 0m)
            return;

        property.Rules.Add(new PropertyRule { Name = "minvalue", MinValue = min });
        property.Rules.Add(new PropertyRule { Name = "maxvalue", MaxValue = max });
    }
```

Leave `AddFormatAttribute` to Task 7 — for now, extract the existing email/phone block into a method of that name, unchanged, returning whether it added an attribute.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter TypeDrivenInferenceTests`
Expected: PASS, 15 tests.

- [ ] **Step 5: Run the full suite and read every failure**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: Some existing tests fail, and each failure must be classified before anything is changed. `required` now reaching more than `Name` and `Title` is the intended behaviour change; a fixture asserting the old blanket 200 is a fixture to update. A failure that is neither is a defect in this task — fix the code, not the test.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/
git commit -m "Infer validation from a column's declared type instead of its name"
```

---

### Task 7: Whole-word email and phone

`Name.Contains("email")` is substring matching. It is why a word like `Age` would match `Package`, `Storage` and `Message`, and the same class of defect is one rename away for `Phone`.

**Files:**
- Modify: `AppScaffolding/DomainModelMapper.cs` (`AddFormatAttribute`, extracted in Task 6)
- Test: `CodeGenerator.Tests/App/WholeWordFormatMatchingTests.cs`

**Interfaces:**
- Consumes: `AddFormatAttribute(PropertyDefinition)` from Task 6.
- Produces: no signature change; matching becomes whole-word over the PascalCase split.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/WholeWordFormatMatchingTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WholeWordFormatMatchingTests
{
    [DataTestMethod]
    [DataRow("Email")]
    [DataRow("EmailAddress")]
    [DataRow("CustomerEmail")]
    [DataRow("BillingEmailAddress")]
    public void AWholeEmailWord_Matches(string propertyName)
        => Assert.IsTrue(Mapped(propertyName).Attributes.Contains("EmailAddress"));

    [DataTestMethod]
    [DataRow("Phone")]
    [DataRow("PhoneNumber")]
    [DataRow("MobilePhone")]
    public void AWholePhoneWord_Matches(string propertyName)
        => Assert.IsTrue(Mapped(propertyName).Attributes.Contains("Phone"));

    [DataTestMethod]
    [DataRow("Emailer")]
    [DataRow("Wholesale")]
    [DataRow("Telephony")]
    [DataRow("Headphones")]
    public void AWordThatMerelyContainsOne_DoesNotMatch(string propertyName)
    {
        // Substring matching is the defect. "Headphones" contains "phone"; it is not a
        // phone number, and a format rule on it refuses every value the user types.
        var property = Mapped(propertyName);

        Assert.IsFalse(property.Attributes.Contains("EmailAddress"));
        Assert.IsFalse(property.Attributes.Contains("Phone"));
    }

    private static PropertyDefinition Mapped(string name)
    {
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(new CandidateField { Name = name, Type = "string" });
        model.Entities.Add(entity);

        return DomainModelMapper.ToClassDefinitions(model)
            .Single(c => c.Name == "Customer").Properties.Single(p => p.Name == name);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter WholeWordFormatMatchingTests`
Expected: FAIL on `Emailer`, `Wholesale`, `Telephony` and `Headphones` — all four currently match by substring.

- [ ] **Step 3: Match on whole words**

In `AppScaffolding/DomainModelMapper.cs`, rewrite `AddFormatAttribute`:

```csharp
    /// <summary>
    /// The two grandfathered name exceptions, matched on whole PascalCase words. Substring
    /// matching is what would let "Headphones" carry a phone-number rule that refuses every
    /// value; whole-word matching closes that class of defect permanently. No other name
    /// patterns are inferred — every other rule comes from the type.
    /// </summary>
    private static bool AddFormatAttribute(PropertyDefinition property)
    {
        var words = SplitPascalCase(property.Name);

        if (words.Contains("email", StringComparer.OrdinalIgnoreCase))
        {
            property.Attributes.Add("EmailAddress");
            return true;
        }

        if (words.Contains("phone", StringComparer.OrdinalIgnoreCase))
        {
            property.Attributes.Add("Phone");
            return true;
        }

        return false;
    }

    /// <summary>"BillingEmailAddress" becomes Billing, Email, Address.</summary>
    private static IReadOnlyList<string> SplitPascalCase(string name)
    {
        var words = new List<string>();
        var start = 0;

        for (var i = 1; i <= name.Length; i++)
        {
            if (i != name.Length && !char.IsUpper(name[i]))
                continue;

            if (i > start)
                words.Add(name[start..i]);

            start = i;
        }

        return words;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter WholeWordFormatMatchingTests`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/App/WholeWordFormatMatchingTests.cs
git commit -m "Match email and phone on whole words, not substrings"
```

---

### Task 8: Generate a real app and run it

Four thousand passing tests did not find the defects on the last two branches; running the generated app did. This task is not optional and its findings are not "follow-ups".

**Files:**
- Test: `CodeGenerator.Tests/App/GeneratedAppTypeFactsTests.cs`
- No production files unless the run finds a defect.

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Write an end-to-end test over a source with real facts**

Create `CodeGenerator.Tests/App/GeneratedAppTypeFactsTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class GeneratedAppTypeFactsTests
{
    [TestMethod]
    public void ACsvColumn_IsBoundedAtWhatTheFileHolds_NotAtTwoHundred()
    {
        // End to end: a CSV whose longest Surname is 7 characters must not produce
        // a validator that accepts 199. This is the defect the phase exists to fix,
        // measured at the only place that matters — the emitted file.
        var csv = "Id,Surname\n1,Smith\n2,Bardsley\n3,Khan\n";
        var folder = TempFolder();

        try
        {
            var path = Path.Combine(folder, "customers.csv");
            File.WriteAllText(path, csv);

            var validator = ValidatorSourceFor(path, entity: "Customer");

            StringAssert.Contains(validator, "Length: > 8");
            Assert.IsFalse(
                validator.Contains("Length: > 200"),
                "The blanket 200 bound survived; facts are not reaching the emitter.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void AnAppFromASourceWithNoFacts_StillGenerates()
    {
        // A discovered domain carries no facts at all and must not throw or
        // emit an uncompilable validator.
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Surname", Type = "string" });

        var source = ValidatorTemplate.Generated("Acme", definition, []);

        StringAssert.Contains(source, "class CustomerValidator");
    }
}
```

> `TempFolder()` and `ValidatorSourceFor(...)` must be taken from the existing fixture pattern in `CodeGenerator.Tests/App/BuildAppDataSourceTests.cs` rather than written fresh — reuse that file's temp-folder helper and its build-and-read-a-generated-file helper, renaming only if its names differ. The two assertions are the contract; the helpers are plumbing.

Generated apps land under a short path: the scratchpad blows MAX_PATH, and the tell is MSB3030 reporting a file not found that plainly exists.

- [ ] **Step 2: Run it**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter GeneratedAppTypeFactsTests`
Expected: PASS.

- [ ] **Step 3: Rebuild the CLI before touching it by hand**

`dotnet test` leaves the CLI's `bin` stale, and a green fix can look dead.

Run: `dotnet build SmartCodeGenerator/CodeGenerator/CodeGenerator.csproj -c Release`

- [ ] **Step 4: Generate an app from the live SQL Server**

This machine has a running SQL Server (MSSQLSERVER, with `sqlcmd`). Build an app from a table with a mix of `nvarchar(n)`, `nvarchar(max)`, `tinyint`, `decimal(18,2)` and a nullable column. Generated apps are written to `D:\SampleProjects\<AppName>`, not the working directory.

- [ ] **Step 5: Read the emitted validator by eye**

Open `D:\SampleProjects\<AppName>\Validation\<Model>Validator.Generated.cs` and confirm, for each column: the length bound matches the column, the `nvarchar(max)` column has none, the `tinyint` is bounded 0-255, the decimal limits its places, and the nullable column is not required.

- [ ] **Step 6: Run the generated app and type into it**

Launch the app. In a text box backed by an `nvarchar(50)`, paste 120 characters and save. It must be refused by the app, with a sentence naming the field — **not** accepted and then rejected by the database. That single interaction is the whole point of this phase.

- [ ] **Step 7: Record anything the run found**

Any defect found here gets a test that reproduces it, then a fix, then a commit — in that order. A defect found by running and fixed without a test will come back.

- [ ] **Step 8: Full suite in Release, exactly as CI runs it**

Run:
```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```
Expected: build succeeded, all tests pass. Read the count — a per-test `  Failed <name>` line does not match a bare `Failed!`, so grepping for the wrong one reports green over a red run.

- [ ] **Step 9: Commit**

```bash
git add CodeGenerator.Tests/App/GeneratedAppTypeFactsTests.cs
git commit -m "Prove the length bound reaches a running app, not just a test"
```

---

## In the spec's Field stage, deliberately not in Phase 1

Two Field-stage rows are held back, both because they need the counter-example scan that Phase 3 builds, not because they were missed:

- **`CHECK` constraint → its condition.** A `CHECK` listing a column's permitted values *is* a declared status, so parsing it belongs beside status recognition rather than ahead of it. Splitting them would mean writing the same constraint parser twice.
- **A pattern every value matches.** This is a proposal, and a proposal needs the zero-violation scan and a Review-card tick. Phase 1 carries declared facts and complete-scan evidence only; it proposes nothing.

Phase 1 therefore never asks the user to confirm anything. Every rule it records follows from a fact the source declared or a value the source contains.

## The remaining phases

Recorded so nothing is lost between branches. Each is planned against the code as it exists when the previous one merges, not now.

| Phase | Delivers |
|---|---|
| **2** | `ValidationContext<T>`, `ValidationOperation`, the seven-stage pipeline with short-circuiting, `IEntityLookup.CountReferencing`, the Security stage enforced at the store boundary and outside `novalidation`, and `SaveResult` gaining per-property attribution. |
| **3** | The counter-example scan (`SELECT DISTINCT TOP 51`, streaming equivalents) and the Entity stage: status recognition, uniqueness proposals, patterns, and referential integrity on delete. |
| **4** | The Business stage: verified arithmetic and inferred cross-field comparisons. |
| **5** | The Workflow stage and the transition editor on the Review card. |
| **6** | The Transaction and Database stages: the posting gate over header and lines, period checks, and constraint-violation translation. |

Phase 2 is where `novalidation` stops meaning "no validation code at all", so it carries the documentation work: `docs/commands.md` and the manual's chapter-18 appendix are both generated from `CommandDictionary` and must be regenerated together, or `ManualAppendixDocTests` goes red.

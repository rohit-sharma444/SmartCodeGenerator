using System.IO;
using AppScaffolding;
using AppScaffolding.Targets;
using DataIngestion;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using DataIngestion.Schema.Readers.Sql;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// End to end over the real reader, model builder, mapper and scaffolder: a source becomes a
/// validator file on disk. Every other test in this phase stops at one of those seams, so a fact
/// dropped at any joint between them fails here and nowhere else.
/// </summary>
[TestClass]
public class GeneratedAppTypeFactsTests
{
    [TestMethod]
    public void EveryDeclaredFact_ReachesTheEmittedValidator()
    {
        // One table declaring all five kinds of fact, read through the real SqlServerReader.
        var validator = ValidatorFromSql();

        // nvarchar(50) NOT NULL: the defect the phase exists to fix. A generated app used to
        // accept 120 characters here and leave the database to refuse the insert.
        StringAssert.Contains(validator, "string.IsNullOrWhiteSpace(item.Code)");
        StringAssert.Contains(validator, "item.Code is { Length: > 50 }");
        Assert.DoesNotContain("Length: > 200", validator,
            "The blanket 200 bound survived; facts are not reaching the emitter.");

        // nvarchar(max), nullable: a known ABSENCE of a bound, so no bound and no required.
        Assert.DoesNotContain("item.Notes", validator);

        // tinyint: the width the five-type target set collapses away, and the reason an int
        // property needs a range at all.
        StringAssert.Contains(validator, "item.Rating < 0");
        StringAssert.Contains(validator, "item.Rating > 255");

        // decimal(18,2), in both halves: the places, and the integer part the database will
        // overflow on at 10^16 whatever the app thinks.
        StringAssert.Contains(validator, "decimal.Round(item.Price, 2) != item.Price");
        StringAssert.Contains(validator, "item.Price > 9999999999999999");

        // datetime, which really does start in 1753 -- and the guard that keeps a date the user
        // never touched from being refused as 0001-01-01.
        StringAssert.Contains(validator, "item.OrderedOn != default(DateTime)");
        StringAssert.Contains(validator, "item.OrderedOn < DateTime.Parse(\"1753-01-01\"");
    }

    [TestMethod]
    public void ANullableColumn_IsNotRequired()
    {
        // The half that is easy to get backwards, and the one a blanket required would break:
        // Notes is declared NULL, so the app must accept a record that leaves it alone.
        var validator = ValidatorFromSql();

        Assert.DoesNotContain("string.IsNullOrWhiteSpace(item.Notes)", validator);
        StringAssert.Contains(validator, "string.IsNullOrWhiteSpace(item.Code)");
    }

    [TestMethod]
    public void ACsvColumn_IsNotBoundedByWhatTheFileHappensToHold()
    {
        // A file declares nothing, so it constrains nothing. The longest surname here is
        // "Bardsley"; bounding the field at 8 would generate an app that refuses
        // "Fotheringham", and create its own column as narrow as the sample besides. The
        // fallback 200 stands, exactly as it did before the phase.
        var validator = ValidatorFromCsv(
            "employees.csv",
            "Id,Surname,Nickname,Rate\n1,Smith,Smudge,19.98\n2,Bardsley,,140.00\n",
            "Employee");

        StringAssert.Contains(validator, "item.Surname is { Length: > 200 }");

        // Nor does a column that happens to have no blank row become required, nor a column
        // whose values happen to carry two decimals become bounded to two.
        Assert.DoesNotContain("is required", validator);
        Assert.DoesNotContain("decimal.Round", validator);
    }

    [TestMethod]
    public void AnAppFromASourceWithNoFacts_StillGenerates()
    {
        // A discovered domain -- Wikipedia, an LLM, or a model the user typed -- carries no
        // facts at all. It must still generate, and still fall back to the 200 bound.
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields =
            {
                new CandidateField { Name = "Id", Type = "int" },
                new CandidateField { Name = "Surname", Type = "string" }
            }
        });

        var validator = ScaffoldAndRead(model, "NoFacts", "Customer");

        StringAssert.Contains(validator, "class CustomerValidator");
        StringAssert.Contains(validator, "Length: > 200");
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(item.Surname)", validator,
            "A source that declares no nullability must not become stricter than it stated.");
    }

    /// <summary>Reads one table through the real SqlServerReader over a declared schema.</summary>
    private static string ValidatorFromSql()
    {
        var reader = new SqlServerReader(new DeclaredSchema());

        var tables = reader.Read(
            new DataSourceSpec(DataSourceKind.Sql, "Server=none", "Shop", ["Products"]));

        return ScaffoldAndRead(DataModelBuilder.Build(tables, []), "Shop", "Product");
    }

    /// <summary>
    /// Writes <paramref name="csv"/> to a real file, reads it with the real CSV reader, and
    /// returns the text of the validator the scaffolder emits for <paramref name="entity"/>.
    /// </summary>
    private static string ValidatorFromCsv(string fileName, string csv, string entity)
    {
        var folder = TempFolder();

        try
        {
            var path = Path.Combine(folder, fileName);
            File.WriteAllText(path, csv);

            Assert.IsTrue(DataSourceReaders.TryCreate(DataSourceKind.Csv, out var reader));

            var tables = reader.Read(new DataSourceSpec(DataSourceKind.Csv, path));

            return ScaffoldAndRead(DataModelBuilder.Build(tables, []), "Sample", entity);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Scaffolds a console app into a temp folder and reads one validator back.</summary>
    private static string ScaffoldAndRead(CandidateDomainModel model, string appName, string entity)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = TempFolder();

        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(model, appName, workspace, target));

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var validator = Path.Combine(
                result.ProjectPath!, "Validation", $"{entity}Validator.Generated.cs");

            Assert.IsTrue(File.Exists(validator), $"no validator was emitted at {validator}");

            return File.ReadAllText(validator);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Short path on purpose: the scratchpad blows MAX_PATH, and the tell is MSB3030
    /// reporting a file not found that plainly exists.</summary>
    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>One table declaring every fact kind, standing in for INFORMATION_SCHEMA.</summary>
    private sealed class DeclaredSchema : ISqlSchemaSource
    {
        public IReadOnlyList<SqlTableInfo> Tables(string connectionString)
            => [new SqlTableInfo("dbo", "Products", 6, 2)];

        public IReadOnlyList<SqlColumnInfo> Columns(
            string connectionString, string schema, string table)
            =>
            [
                new SqlColumnInfo("Id", "int", Nullable: false),
                new SqlColumnInfo("Code", "nvarchar", Nullable: false, MaxLength: 50),
                new SqlColumnInfo("Notes", "nvarchar", Nullable: true, MaxLength: -1),
                new SqlColumnInfo("Rating", "tinyint", Nullable: false, Precision: 3, Scale: 0),
                new SqlColumnInfo("Price", "decimal", Nullable: true, Precision: 18, Scale: 2),
                new SqlColumnInfo("OrderedOn", "datetime", Nullable: false)
            ];

        public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString) => [];

        public IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table, int limit)
            =>
            [
                ["1", "AB-1", "a note", "5", "19.98", "2026-01-01"],
                ["2", "AB-2", null, "7", "140.00", "2026-02-01"]
            ];
    }
}

using System.IO;
using AppScaffolding;
using AppScaffolding.Targets;
using DataIngestion;
using DataIngestion.Schema.Readers;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// End to end over the real reader, model builder, mapper and scaffolder: a CSV on disk becomes
/// a validator file on disk. Every other test in this phase stops at one of those seams.
/// </summary>
[TestClass]
public class GeneratedAppTypeFactsTests
{
    [TestMethod]
    public void ACsvColumn_IsBoundedAtWhatTheFileHolds_NotAtTwoHundred()
    {
        // The defect the phase exists to fix, measured at the only place that matters -- the
        // emitted file. A blanket 200 here is what let a generated app accept 120 characters
        // into a column that holds 50 and leave the database to refuse the insert.
        var validator = ValidatorEmittedFor(
            "customers.csv",
            "Id,Surname\n1,Smith\n2,Bardsley\n3,Khan\n",
            "Customer");

        StringAssert.Contains(validator, "Length: > 8");
        Assert.DoesNotContain("Length: > 200", validator,
            "The blanket 200 bound survived; facts are not reaching the emitter.");
    }

    [TestMethod]
    public void ACsvColumnWithNoBlanks_IsRequired_AndOneWithABlankIsNot()
    {
        // IsNullable is the other fact a complete file scan proves, and required is now emitted
        // for a string alone. Both halves are asserted here so a blanket required -- or none at
        // all -- fails rather than passing on the half that happens to be right.
        var validator = ValidatorEmittedFor(
            "employees.csv",
            "Id,Surname,Nickname\n1,Smith,Smudge\n2,Bardsley,\n3,Khan,KK\n",
            "Employee");

        StringAssert.Contains(validator, "string.IsNullOrWhiteSpace(item.Surname)");
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(item.Nickname)", validator,
            "A column the file shows blank in one row was made required.");
    }

    [TestMethod]
    public void ACsvNumberColumn_GetsNoRange_AndTheDecimalGetsOnlyItsPlaces()
    {
        // A file declares no width, so there is no range to carry: the integer bounds come from
        // a SQL type name and nothing else. Pinned because the easy mistake is to bound an int
        // by the largest value the file happens to hold, which refuses the next order outright.
        var validator = ValidatorEmittedFor(
            "orders.csv",
            "Id,Quantity,Total\n1,2,19.98\n2,7,140.00\n",
            "Order");

        Assert.DoesNotContain("item.Quantity", validator);
        Assert.DoesNotContain("item.Total <", validator);
        Assert.DoesNotContain("item.Total >", validator);

        // The one decimal fact a file does prove, and it proves it over every row,
        // not over a sample.
        StringAssert.Contains(validator, "decimal.Round(item.Total, 2) != item.Total");
    }

    [TestMethod]
    public void AnAppFromASourceWithNoFacts_StillGenerates()
    {
        // A discovered domain -- Wikipedia, an LLM, or a model the user typed -- carries no facts
        // at all. It must still generate, and still fall back to the 200 bound it had before.
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

    /// <summary>
    /// Writes <paramref name="csv"/> to a real file, reads it with the real CSV reader, and
    /// returns the text of the validator the scaffolder emits for <paramref name="entity"/>.
    /// </summary>
    private static string ValidatorEmittedFor(string fileName, string csv, string entity)
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
}

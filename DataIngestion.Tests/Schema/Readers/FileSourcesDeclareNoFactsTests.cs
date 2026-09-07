using DataIngestion;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

/// <summary>
/// A file declares no width, no scale and no nullability, so it supplies no
/// <c>TypeFacts</c> and the generated app infers no rule from one.
/// </summary>
/// <remarks>
/// This replaces an earlier reading, in which a complete pass over the file recorded the longest
/// value it held, the most decimal places it held, and whether any row was blank. Every one of
/// those became a RULE, and the rule then refused data the file simply did not happen to contain:
/// a CSV holding Smith, Bardsley and Khan generated an app that refused "Fotheringham", and it
/// created its own column as narrow as the sample too. A file's contents are evidence about the
/// rows in the file, not a constraint the source declared -- so only a database, which really
/// does declare its lengths, produces facts. The type is still inferred from the values; a type
/// is a property of the column's domain, and a wrong one corrupts data rather than merely
/// bounding it.
/// </remarks>
public class FileSourcesDeclareNoFactsTests
{
    [Fact]
    public void ACsvColumn_CarriesNoFacts()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Surname,Price\n1,Smith,19.98\n2,Bardsley,140.00\n");

        try
        {
            var table = Assert.Single(
                new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, path)));

            Assert.All(table.Columns, column => Assert.Null(column.Facts));

            // The type still comes from the values, which is what the sample is for.
            Assert.Equal("string", table.Columns[1].Type);
            Assert.Equal("decimal", table.Columns[2].Type);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AJsonColumn_CarriesNoFacts()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, """[{"Id":1,"Surname":"Smith"},{"Id":2,"Surname":"Bardsley"}]""");

        try
        {
            var tables = new JsonTableReader().Read(new DataSourceSpec(DataSourceKind.Json, path));

            Assert.All(tables, table =>
                Assert.All(table.Columns, column => Assert.Null(column.Facts)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

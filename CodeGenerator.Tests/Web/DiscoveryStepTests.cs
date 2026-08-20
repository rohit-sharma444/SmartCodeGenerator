using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Node 5's work, without a component. The data path only: the discovery path fetches from
/// Wikipedia and belongs in the existing discovery suites, not here.
/// </summary>
[TestClass]
public class DiscoveryStepTests
{
    private string _csv = string.Empty;

    [TestInitialize]
    public void WriteACsv()
    {
        _csv = Path.Combine(Path.GetTempPath(), $"orders-{Guid.NewGuid():N}.csv");

        File.WriteAllText(_csv, "OrderId,Customer,Total\n1,Ada,10.50\n2,Grace,22.00\n");
    }

    [TestCleanup]
    public void DeleteTheCsv()
    {
        if (File.Exists(_csv))
            File.Delete(_csv);
    }

    [TestMethod]
    public void Reads_a_csv_and_names_the_file_it_came_from()
    {
        Assert.IsTrue(
            DiscoveryStep.TryRead(new DataSourceSpec(DataSourceKind.Csv, _csv),
                out var result, out var error),
            error);

        Assert.HasCount(1, result.Tables);
        Assert.AreEqual(Path.GetFileName(_csv), result.FileLabel);
    }

    // The reader's own message already says what was wrong in the user's terms, so the step
    // passes it through rather than inventing one.
    [TestMethod]
    public void Reports_a_missing_file_rather_than_throwing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "definitely-not-here.csv");

        Assert.IsFalse(
            DiscoveryStep.TryRead(new DataSourceSpec(DataSourceKind.Csv, missing),
                out _, out var error));

        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void Builds_a_model_and_imported_rows_from_the_tables()
    {
        DiscoveryStep.TryRead(new DataSourceSpec(DataSourceKind.Csv, _csv), out var read, out _);

        var (model, imported) = DiscoveryStep.BuildFromData(read, []);

        Assert.IsNotEmpty(model.Entities);
        Assert.IsNotNull(imported);
    }

    // The second argument to DataModelBuilder.Build is what lets `why <class>` answer for a class
    // the user never named, so a chosen finding has to reach it.
    [TestMethod]
    public void An_applied_finding_leaves_a_decision_trail()
    {
        DiscoveryStep.TryRead(new DataSourceSpec(DataSourceKind.Csv, _csv), out var read, out _);

        var (model, _) = DiscoveryStep.BuildFromData(read, read.Report.Findings);

        if (read.Report.Findings.Count > 0)
            Assert.IsNotEmpty(model.Decisions);
    }

    /// <summary>
    /// A spreadsheet open in Excel is the ordinary case, and it throws IOException rather than
    /// FileNotFoundException — which the catch here did not cover, so it escaped a component
    /// handler and took the circuit with it. The doc comment promised "false with a message rather
    /// than an exception" the whole time.
    /// </summary>
    [TestMethod]
    public void A_file_another_program_is_holding_is_reported_rather_than_thrown()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cg-locked-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "Name,Age" + Environment.NewLine + "Ada,36" + Environment.NewLine);

        // Held exactly as Excel holds a workbook.
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var read = DiscoveryStep.TryRead(
                new DataSourceSpec(DataSourceKind.Csv, path), out _, out var error);

            Assert.IsFalse(read);
            Assert.IsNotEmpty(error);
        }

        File.Delete(path);
    }
}

using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Node 7's decisions. The scaffold itself is covered by the existing AppScaffolding suites;
/// what is asserted here is the naming, the collision check and the lines the console prints.
/// </summary>
[TestClass]
public class GenerateStepTests
{
    // One naming rule in the product: the description names the app, through the same
    // DomainAppName.FromDescription the console path uses — which is why the answer is "Library"
    // and not "LibrarySystem". That rule drops a trailing type noun, "system" among them, so the
    // kind of thing asked for does not become part of the identifier.
    [TestMethod]
    public void Names_the_project_from_the_description()
        => Assert.AreEqual("Library", GenerateStep.NameFor(SessionFor("a library system")));

    [TestMethod]
    public void Falls_back_to_the_source_file_name_when_there_is_no_description()
    {
        var session = new BuildSession("", Path.GetTempPath(), AppCustomization.Default,
            new DataIngestion.DataSourceSpec(DataIngestion.DataSourceKind.Csv, @"C:\data\orders.csv"));

        Assert.AreEqual("Orders", GenerateStep.NameFor(session));
    }

    [TestMethod]
    public void Reports_no_collision_for_a_name_that_is_not_on_disk()
    {
        var session = SessionFor("a library system");

        Assert.IsFalse(GenerateStep.Collides(session, "DefinitelyNotThere", new WpfTarget()));
    }

    [TestMethod]
    public void The_outcome_names_the_project_that_was_written()
    {
        var scaffold = new ScaffoldResult(
            Success: true,
            ProjectPath: @"D:\ws\LibrarySystem",
            FilesWritten: [],
            Message: string.Empty);

        var outcome = GenerateStep.OutcomeFor(scaffold, new WpfTarget());

        Assert.IsTrue(outcome.Lines.Any(l => l.Contains("LibrarySystem")));
    }

    [TestMethod]
    public void A_failed_scaffold_reports_its_own_reason()
    {
        var scaffold = new ScaffoldResult(
            Success: false,
            ProjectPath: string.Empty,
            FilesWritten: [],
            Message: "dotnet new timed out");

        var outcome = GenerateStep.OutcomeFor(scaffold, new WpfTarget());

        Assert.IsTrue(outcome.Lines.Any(l => l.Contains("timed out")));
    }

    /// <summary>
    /// The console path catches this exactly (BuildAppCommand: "Could not start `dotnet` —
    /// skipping the build check") and the design's error table promises it on the web too, where
    /// nothing caught it: ProcessRunner.RunStreaming documents that it throws Win32Exception when
    /// the executable cannot be started, so it came out of an event handler and took the circuit —
    /// and a dead circuit is reported to the console as a closed tab.
    /// </summary>
    [TestMethod]
    public void A_build_that_cannot_start_the_compiler_is_reported_rather_than_thrown()
    {
        var attempt = GenerateStep.Build(
            Path.GetTempPath(), _ => { }, executable: "cg-no-such-executable");

        Assert.IsFalse(attempt.Ran);
        Assert.IsNull(attempt.Result);
    }

    [TestMethod]
    public void A_compiler_that_does_start_is_reported_as_having_run()
    {
        // `where build` finds nothing and exits 1, which is all this needs: something that starts.
        var attempt = GenerateStep.Build(Path.GetTempPath(), _ => { }, executable: "where");

        Assert.IsTrue(attempt.Ran);
        Assert.IsNotNull(attempt.Result);
    }

    private static BuildSession SessionFor(string description)
    {
        var session = new BuildSession(description, Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(new CandidateDomainModel());
        session.Advance();

        return session;
    }
}

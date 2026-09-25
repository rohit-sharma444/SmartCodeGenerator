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
    /// The sign-off the console prints after the browser is done has to carry the one thing
    /// on it that needs doing rather than reading: an app generated before the validation
    /// pipeline keeps a write-once .Custom.cs stub declaring a hook the regenerated half no
    /// longer has, and the pair does not compile. Before the next steps, because it is the
    /// reason the next step will fail.
    /// </summary>
    [TestMethod]
    public void The_outcome_names_a_validator_stub_the_regenerate_could_not_replace()
    {
        var scaffold = new ScaffoldResult(
            Success: true,
            ProjectPath: @"D:\ws\LibrarySystem",
            FilesWritten: [],
            Message: string.Empty)
        {
            OutdatedValidatorStubs =
                ["Validation/BookValidator.Custom.cs declares ValidateCustom in the shape "
                 + "emitted before the validation pipeline"],
        };

        var outcome = GenerateStep.OutcomeFor(scaffold, new WpfTarget());

        var lines = outcome.Lines.ToList();
        var stub = lines.FindIndex(l => l.Contains("BookValidator.Custom.cs"));
        var next = lines.FindIndex(l => l.Contains("Next steps:"));

        Assert.IsGreaterThanOrEqualTo(0, stub, "the kept stub should be named");
        Assert.IsLessThan(next, stub, "it should be read before the next steps it breaks");
    }

    /// <summary>
    /// Every WPF and WinForms app used to come out with no test project and no word about it:
    /// `dotnet new xunit` refused the app's framework, and the scaffold swallowed the refusal. A
    /// test project that could not be made is a sentence on the sign-off, not an absence the user
    /// discovers by looking for a folder.
    /// </summary>
    [TestMethod]
    public void The_outcome_says_why_there_is_no_test_project()
    {
        var scaffold = new ScaffoldResult(
            Success: true,
            ProjectPath: @"D:\ws\LibrarySystem",
            FilesWritten: [],
            Message: string.Empty)
        {
            TestProjectProblem = "No test project was generated: `dotnet new xunit` failed",
        };

        var outcome = GenerateStep.OutcomeFor(scaffold, new WpfTarget());

        Assert.IsTrue(
            outcome.Lines.Any(l => l.Contains("No test project was generated")),
            "the reason there is no test project should be printed");
    }

    /// <summary>An imported value its field's type could not read is not in the app, and the
    /// sign-off is where the user learns which.</summary>
    [TestMethod]
    public void The_outcome_names_the_imported_values_left_out()
    {
        var scaffold = new ScaffoldResult(
            Success: true,
            ProjectPath: @"D:\ws\LibrarySystem",
            FilesWritten: [],
            Message: string.Empty)
        {
            UnreadableSeedValues = ["Book.Pages: 1 value(s) could not be read as int"],
        };

        var outcome = GenerateStep.OutcomeFor(scaffold, new WpfTarget());

        Assert.IsTrue(outcome.Lines.Any(l => l.Contains("Book.Pages")));
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

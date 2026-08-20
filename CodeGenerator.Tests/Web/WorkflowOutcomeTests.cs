using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// What the console is told once the browser is finished, for the ends that are not a generation.
/// </summary>
/// <remarks>
/// Both terminal messages said "Nothing was generated", which stops being true the moment the
/// generate stage has run: cancelling or closing the tab after a project is written leaves that
/// project on disk. The cancel question now promises exactly that, so the line printed underneath
/// it had better not contradict it — a warning the next line of output denies is worse than no
/// warning.
/// </remarks>
[TestClass]
public class WorkflowOutcomeTests
{
    [TestMethod]
    public void Cancelling_with_nothing_written_says_nothing_was_generated()
    {
        var session = Discovered();
        session.RequestCancel();
        session.ConfirmCancel();

        var outcome = BuildAppWebHost.OutcomeFor(session);

        Assert.AreEqual(SessionPhase.Cancelled, outcome.Phase);
        StringAssert.Contains(string.Join(" ", outcome.Lines), "Nothing was generated.");
    }

    [TestMethod]
    public void Cancelling_after_the_project_is_written_says_where_it_still_is()
    {
        var session = Written();
        session.RequestCancel();
        session.ConfirmCancel();

        var outcome = BuildAppWebHost.OutcomeFor(session);

        var lines = string.Join(" ", outcome.Lines);

        Assert.AreEqual(SessionPhase.Cancelled, outcome.Phase);
        StringAssert.Contains(lines, @"C:\ws\Shop");
        Assert.DoesNotContain("Nothing was generated", lines);
    }

    [TestMethod]
    public void Closing_the_tab_with_nothing_written_says_nothing_was_generated()
    {
        var session = Discovered();
        session.Abandon();

        var outcome = BuildAppWebHost.OutcomeFor(session);

        Assert.AreEqual(SessionPhase.Abandoned, outcome.Phase);
        StringAssert.Contains(string.Join(" ", outcome.Lines), "Nothing was generated.");
    }

    /// <summary>The same lie, down the other terminal path — a closed tab does not delete a
    /// project either.</summary>
    [TestMethod]
    public void Closing_the_tab_after_the_project_is_written_says_where_it_still_is()
    {
        var session = Written();
        session.Abandon();

        var outcome = BuildAppWebHost.OutcomeFor(session);

        var lines = string.Join(" ", outcome.Lines);

        Assert.AreEqual(SessionPhase.Abandoned, outcome.Phase);
        StringAssert.Contains(lines, @"C:\ws\Shop");
        Assert.DoesNotContain("Nothing was generated", lines);
    }

    /// <summary>A finished run reports what the generate stage put in the session, untouched.</summary>
    [TestMethod]
    public void A_completed_run_reports_its_own_outcome()
    {
        var session = Written();
        var reported = new BuildOutcome(SessionPhase.Generated, ["Wrote it."]);

        session.Complete(reported);

        Assert.AreSame(reported, BuildAppWebHost.OutcomeFor(session));
    }

    private static BuildSession Discovered()
    {
        var session = new BuildSession("a shop", @"C:\ws", AppCustomization.Default);

        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();

        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book" });
        session.SetModel(model);
        session.Advance();

        return session;
    }

    private static BuildSession Written()
    {
        var session = Discovered();

        session.SetGenerated(new GeneratedApp(
            new ScaffoldResult(true, @"C:\ws\Shop", [], string.Empty),
            BuildLog: string.Empty,
            BuildSucceeded: true));

        return session;
    }
}

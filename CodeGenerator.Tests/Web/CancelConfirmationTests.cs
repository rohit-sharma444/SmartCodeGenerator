using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The question asked before Cancel throws a whole interview away.
/// </summary>
/// <remarks>
/// Changing a description asks before it discards a model, while Cancel — on every panel — ended
/// the run and generated nothing without a word. The guard was on the reversible action and not
/// the destructive one. Session state rather than page state, like <c>PendingDiscard</c>, so the
/// second tab sees the question instead of going on as though nothing had been asked.
/// </remarks>
[TestClass]
public class CancelConfirmationTests
{
    [TestMethod]
    public void An_untouched_interview_is_cancelled_without_a_question()
    {
        // Nothing has been answered, so there is nothing to confirm losing — and a wizard that
        // asks "are you sure" over an empty first page teaches the user to click questions away.
        var session = new BuildSession(string.Empty, @"C:\ws", AppCustomization.Default);

        var cancelled = session.RequestCancel();

        Assert.IsTrue(cancelled, "with nothing to lose it should just cancel");
        Assert.AreEqual(SessionPhase.Cancelled, session.Phase);
        Assert.IsNull(session.PendingCancel);
    }

    [TestMethod]
    public void An_answered_interview_asks_before_it_is_thrown_away()
    {
        var session = new BuildSession("a shop", @"C:\ws", AppCustomization.Default);

        var cancelled = session.RequestCancel();

        Assert.IsFalse(cancelled, "the answer should have bought a question");
        Assert.AreEqual(SessionPhase.Running, session.Phase);
        Assert.IsNotNull(session.PendingCancel);
    }

    /// <summary>The same reasoning as the discard warning: "the model" is an abstraction, and the
    /// count is the afternoon the user is about to lose.</summary>
    [TestMethod]
    public void The_question_counts_the_classes_at_risk()
    {
        var session = Discovered(classes: 14);

        session.RequestCancel();

        StringAssert.Contains(session.PendingCancel!, "14 classes");
    }

    [TestMethod]
    public void One_class_is_not_called_one_classes()
    {
        var session = Discovered(classes: 1);

        session.RequestCancel();

        StringAssert.Contains(session.PendingCancel!, "1 class");
    }

    /// <summary>
    /// Once the project is on disk, cancelling does not delete it — so the question must not imply
    /// that it does. A warning that overstates what is at stake is the same failure as one that
    /// understates it.
    /// </summary>
    [TestMethod]
    public void A_project_already_written_is_said_to_stay_on_disk()
    {
        var session = Discovered();
        session.SetGenerated(new GeneratedApp(
            new ScaffoldResult(true, @"C:\ws\Shop", [], string.Empty),
            BuildLog: string.Empty,
            BuildSucceeded: true));

        session.RequestCancel();

        StringAssert.Contains(session.PendingCancel!, @"C:\ws\Shop");
        StringAssert.Contains(session.PendingCancel!, "stays");
    }

    [TestMethod]
    public void A_project_an_earlier_run_wrote_is_said_to_stay_on_disk()
    {
        // A resumed build has no Generated of its own, but the session it resumes wrote the
        // project. "Nothing will be generated" / "Nothing was generated" read as if it never was.
        var root = Path.Combine(Path.GetTempPath(), "cc-" + Guid.NewGuid().ToString("N")[..8]);
        var session = Discovered(root: root);
        var csproj = GeneratedProjectPath.CsprojPath(root, GenerateStep.NameFor(session), new WpfTarget());
        Directory.CreateDirectory(Path.GetDirectoryName(csproj)!);
        File.WriteAllText(csproj, "<Project />");

        session.RequestCancel();

        StringAssert.Contains(session.PendingCancel!, Path.GetDirectoryName(csproj)!);
        StringAssert.Contains(session.PendingCancel!, "stays on disk");
        Assert.DoesNotContain("nothing will be generated", session.PendingCancel!);

        session.ConfirmCancel();
        var lines = string.Join(" ", CodeGenerator.Web.BuildAppWebHost.OutcomeFor(session).Lines);
        Assert.DoesNotContain("Nothing was generated", lines);
        StringAssert.Contains(lines, Path.GetDirectoryName(csproj)!);
    }

    [TestMethod]
    public void Keeping_the_build_leaves_the_workflow_exactly_where_it_was()
    {
        var session = Discovered();
        var where = session.Current;

        session.RequestCancel();
        session.KeepBuilding();

        Assert.IsNull(session.PendingCancel);
        Assert.AreEqual(SessionPhase.Running, session.Phase);
        Assert.AreEqual(where, session.Current);
        Assert.IsNotNull(session.Model);
    }

    [TestMethod]
    public void Confirming_ends_the_workflow()
    {
        var session = Discovered();

        session.RequestCancel();
        session.ConfirmCancel();

        Assert.AreEqual(SessionPhase.Cancelled, session.Phase);
        Assert.IsNull(session.PendingCancel);
    }

    /// <summary>The reason this lives on the session: the tab that did not click Cancel has to be
    /// told, or it goes on offering a Continue button for a workflow that is being ended.</summary>
    [TestMethod]
    public void The_question_reaches_the_other_tab()
    {
        var session = Discovered();
        var repaints = 0;
        session.Changed += () => repaints++;

        session.RequestCancel();

        Assert.AreEqual(1, repaints);
    }

    [TestMethod]
    public void Confirming_a_cancel_nobody_asked_for_is_refused()
    {
        var session = Discovered();

        Assert.ThrowsExactly<InvalidOperationException>(() => session.ConfirmCancel());
    }

    /// <summary>A session parked on the review node with a model in hand.</summary>
    private static BuildSession Discovered(int classes = 3, string root = @"C:\ws")
    {
        var session = new BuildSession("a shop", root, AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(ModelOf(classes));
        session.Advance();

        return session;
    }

    private static CandidateDomainModel ModelOf(int classes)
    {
        var model = new CandidateDomainModel();

        for (var i = 0; i < classes; i++)
            model.Entities.Add(new CandidateEntity { Name = $"Thing{i}" });

        return model;
    }
}

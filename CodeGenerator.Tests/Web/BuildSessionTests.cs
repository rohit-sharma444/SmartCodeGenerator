using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web;
using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The workflow's state machine. Every rule the page depends on is asserted here rather than
/// through a rendered component: the decisions are the session's, and a browser proves nothing
/// about them.
/// </summary>
[TestClass]
public class BuildSessionTests
{
    private static BuildSession New(string description = "")
        => new(description, @"C:\ws", AppCustomization.Default);

    [TestMethod]
    public void Starts_at_the_first_node_with_nothing_answered()
    {
        var session = New();

        Assert.AreEqual(BuildStage.Describe, session.Current);
        Assert.AreEqual(StageState.Active, session.StateOf(BuildStage.Describe));
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.AppType));
        Assert.AreEqual(SessionPhase.Running, session.Phase);
    }

    // `build app: a library system` has already answered node 1, and re-asking would make the
    // shortcut pointless.
    [TestMethod]
    public void An_inline_description_opens_the_workflow_at_the_app_type()
    {
        var session = New("a library system");

        Assert.AreEqual(BuildStage.AppType, session.Current);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Describe));
        Assert.AreEqual("a library system", session.Description);
    }

    // `build app from csv orders.csv` has answered node 3 the same way.
    [TestMethod]
    public void An_inline_source_marks_the_data_node_answered()
    {
        var session = new BuildSession(
            "", @"C:\ws", AppCustomization.Default,
            new DataSourceSpec(DataSourceKind.Csv, @"C:\data\orders.csv"));

        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Data));
        Assert.IsTrue(session.HasData);
    }

    [TestMethod]
    public void Advancing_marks_the_current_node_done_and_opens_the_next()
    {
        var session = New();

        session.SetDescription("a shop");
        session.Advance();

        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Describe));
        Assert.AreEqual(BuildStage.AppType, session.Current);
        Assert.AreEqual(StageState.Active, session.StateOf(BuildStage.AppType));
    }

    [TestMethod]
    public void GoTo_reopens_a_done_node_without_losing_the_answers_after_it()
    {
        var session = New("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();

        session.GoTo(BuildStage.Describe);

        Assert.AreEqual(BuildStage.Describe, session.Current);
        Assert.AreEqual(StageState.Active, session.StateOf(BuildStage.Describe));
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.AppType));
        Assert.IsNotNull(session.Target);
    }

    [TestMethod]
    public void GoTo_a_pending_node_is_refused()
    {
        var session = New();

        Assert.ThrowsExactly<InvalidOperationException>(() => session.GoTo(BuildStage.Review));
    }

    // The rule the whole diagram lives on. A model discovered from one description must not
    // survive a different description. Driven the way the page drives it: a click on the green
    // Describe node, then a retype — a setter is only ever called from its own node's panel.
    // The confirmation is the change's price and DiscardConfirmationTests is where it is proved;
    // what is proved here is that saying yes really does clear everything discovery produced.
    [TestMethod]
    public void Changing_the_description_discards_the_discovered_model()
    {
        var session = Discovered();

        session.GoTo(BuildStage.Describe);
        session.SetDescription("a hotel");
        session.ConfirmPending();

        Assert.IsNull(session.Model);
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Discover));
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Review));
    }

    [TestMethod]
    public void Changing_the_data_source_discards_the_discovered_model()
    {
        var session = Discovered();

        session.SetSource(new DataSourceSpec(DataSourceKind.Csv, @"C:\data\other.csv"));
        session.ConfirmPending();

        Assert.IsNull(session.Model);
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Discover));
    }

    // The counterpart, and the reason Invalidates is not just "everything downstream": the
    // target is not read until Generate, so a discovered model is still perfectly good.
    [TestMethod]
    public void Changing_the_app_type_keeps_the_discovered_model()
    {
        var session = Discovered();

        session.SetTarget(new WebApiTarget());

        Assert.IsNotNull(session.Model);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Discover));
    }

    [TestMethod]
    public void Changing_the_customization_keeps_the_discovered_model()
    {
        var session = Discovered();

        session.SetCustomization(AppCustomization.Default with { Company = "Acme" });

        Assert.IsNotNull(session.Model);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Discover));
    }

    // Re-answering with the SAME value is not a change. Clicking back into a node to look at it
    // and clicking Next must not throw away a fetch.
    [TestMethod]
    public void Re_answering_with_the_same_value_invalidates_nothing()
    {
        var session = Discovered();

        session.SetDescription("a shop");

        Assert.IsNotNull(session.Model);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Discover));
    }

    [TestMethod]
    public void Choosing_no_data_after_a_source_clears_the_source()
    {
        var session = New("a shop");
        session.SetSource(new DataSourceSpec(DataSourceKind.Csv, @"C:\data\orders.csv"));

        session.SetNoData();

        Assert.IsNull(session.Source);
        Assert.IsFalse(session.HasData);
    }

    // Fail marks whichever node is open, so the session has to be standing on the node that
    // could actually fail — here Data, with a path that does not exist.
    [TestMethod]
    public void Failing_a_node_records_the_message_and_leaves_the_workflow_running()
    {
        var session = New("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();

        session.Fail("No file at C:\nope.csv");

        Assert.AreEqual(BuildStage.Data, session.Current);
        Assert.AreEqual(StageState.Failed, session.StateOf(BuildStage.Data));
        Assert.AreEqual("No file at C:\nope.csv", session.FailureMessage);
        Assert.AreEqual(SessionPhase.Running, session.Phase);
    }

    // The run stage starts what the generate stage wrote, so what was written has to outlive the
    // panel that wrote it.
    [TestMethod]
    public void What_was_generated_is_carried_to_the_run_stage()
    {
        var session = Discovered();
        var app = new GeneratedApp(
            new AppScaffolding.ScaffoldResult(true, @"C:\ws\Shop", [], string.Empty),
            BuildLog: "Build succeeded.",
            BuildSucceeded: true);

        session.SetGenerated(app);

        Assert.AreSame(app, session.Generated);
    }

    [TestMethod]
    public void Regenerating_from_a_changed_answer_forgets_the_app_that_was_generated()
    {
        var session = Discovered();
        session.SetGenerated(new GeneratedApp(
            new AppScaffolding.ScaffoldResult(true, @"C:\ws\Shop", [], string.Empty),
            BuildLog: string.Empty,
            BuildSucceeded: true));

        session.GoTo(BuildStage.Describe);
        session.SetDescription("a hotel");
        session.ConfirmPending();

        Assert.IsNull(session.Generated);
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Run));
    }

    // Completing marks the node the user finished on, which is Run when the app was started and
    // Generate when there was nothing to start.
    [TestMethod]
    public void Completing_marks_the_node_it_was_completed_from()
    {
        var session = Discovered();
        session.Advance();                                    // review -> output
        session.Advance();                                    // output -> entry template
        session.Advance();                                    // entry template -> generate
        session.Advance();                                    // generate -> run

        Assert.AreEqual(BuildStage.Run, session.Current);

        session.Complete(new BuildOutcome(SessionPhase.Generated, ["done"]));

        Assert.AreEqual(SessionPhase.Generated, session.Phase);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Run));
    }

    // Walking into a node and leaving again is not answering it. A green tick on the generate node
    // would say a project exists, which is exactly what the user is being asked to decide.
    [TestMethod]
    public void Leaving_a_node_that_holds_nothing_leaves_it_pending()
    {
        var session = Discovered();
        session.Advance();                                    // review -> output
        session.Advance();                                    // output -> entry template, nothing to answer
        session.Advance();                                    // entry template -> generate, nothing written

        Assert.AreEqual(BuildStage.Generate, session.Current);

        session.GoTo(BuildStage.Describe);

        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Generate));
        Assert.IsFalse(session.HoldsAnAnswer(BuildStage.Generate));
    }

    [TestMethod]
    public void Leaving_a_node_that_holds_an_answer_keeps_it_done()
    {
        var session = New("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();                                    // on data
        session.SetNoData();                                  // answered, but not continued

        session.GoTo(BuildStage.Describe);

        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Data));
    }

    // "No data" is an answer that leaves Source null, so the session records the choice itself
    // rather than trying to read it back out of a null.
    [TestMethod]
    public void Choosing_no_data_counts_as_answering_the_data_node()
    {
        var session = New("a shop");

        Assert.IsFalse(session.HoldsAnAnswer(BuildStage.Data));

        session.SetNoData();

        Assert.IsTrue(session.HoldsAnAnswer(BuildStage.Data));
    }

    // The customization always has a value, so applying it is the only signal that the node was
    // answered — the look panel applies even when the user changed nothing.
    [TestMethod]
    public void The_look_node_is_answered_by_applying_it_not_by_having_a_value()
    {
        var session = New("a shop");

        Assert.IsFalse(session.HoldsAnAnswer(BuildStage.Look));

        session.SetCustomization(session.Customization);

        Assert.IsTrue(session.HoldsAnAnswer(BuildStage.Look));
    }

    // The review node holds the model it reviews, so it stays answered even though its own step is
    // an action rather than an answer.
    [TestMethod]
    public void The_review_node_holds_the_model_it_reviews()
    {
        var session = Discovered();

        Assert.IsTrue(session.HoldsAnAnswer(BuildStage.Review));

        session.Advance();
        session.GoTo(BuildStage.Describe);

        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Review));
    }

    // Compiling is not the same as having seen it run, so the run node is answered by starting it.
    [TestMethod]
    public void The_run_node_is_answered_by_starting_the_app()
    {
        var session = Discovered();
        session.SetGenerated(new GeneratedApp(
            new AppScaffolding.ScaffoldResult(true, @"C:\ws\Shop", [], string.Empty),
            BuildLog: string.Empty,
            BuildSucceeded: true));

        Assert.IsFalse(session.HoldsAnAnswer(BuildStage.Run));

        session.MarkStarted();

        Assert.IsTrue(session.HoldsAnAnswer(BuildStage.Run));
    }

    // Regenerating means the app that was started is gone, so the run node is unanswered again.
    [TestMethod]
    public void Generating_again_forgets_that_the_last_one_was_started()
    {
        var session = Discovered();
        session.SetGenerated(new GeneratedApp(
            new AppScaffolding.ScaffoldResult(true, @"C:\ws\Shop", [], string.Empty),
            BuildLog: string.Empty,
            BuildSucceeded: true));
        session.MarkStarted();

        session.SetGenerated(new GeneratedApp(
            new AppScaffolding.ScaffoldResult(true, @"C:\ws\Shop", [], string.Empty),
            BuildLog: string.Empty,
            BuildSucceeded: true));

        Assert.IsFalse(session.Started);
    }

    [TestMethod]
    public void Cancelling_and_abandoning_are_different_endings()
    {
        var cancelled = New();
        cancelled.Cancel();
        Assert.AreEqual(SessionPhase.Cancelled, cancelled.Phase);

        var abandoned = New();
        abandoned.Abandon();
        Assert.AreEqual(SessionPhase.Abandoned, abandoned.Phase);
    }

    [TestMethod]
    public void A_terminal_session_refuses_further_answers()
    {
        var session = New();
        session.Cancel();

        Assert.ThrowsExactly<InvalidOperationException>(() => session.SetDescription("too late"));
    }

    /// <summary>A session with a model, parked on the review node — the state most of the
    /// invalidation rules are about.</summary>
    private static BuildSession Discovered()
    {
        var session = New("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(new DomainDiscovery.Model.CandidateDomainModel());
        session.Advance();

        return session;
    }
}

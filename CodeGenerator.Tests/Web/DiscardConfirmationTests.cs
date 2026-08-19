using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The question asked before a changed answer throws a model away.
/// </summary>
/// <remarks>
/// Invalidation is the rule the diagram lives on, and until now it ran silently: retyping the
/// description after twenty minutes of renaming classes destroyed every one of those edits
/// without a word. The change is held rather than applied, so nothing is lost while the question
/// is on screen — which is the whole difference between asking and apologising.
/// </remarks>
[TestClass]
public class DiscardConfirmationTests
{
    [TestMethod]
    public void A_changed_description_is_held_rather_than_applied()
    {
        var session = Discovered();

        var applied = session.SetDescription("a lending library");

        Assert.IsFalse(applied, "the change should have been held for confirmation");
        Assert.AreEqual("a shop", session.Description);
        Assert.IsNotNull(session.Model);
        Assert.IsNotNull(session.PendingDiscard);
    }

    /// <summary>The warning counts what is at risk: "the model" is abstract, "14 classes" is
    /// the thing the user spent their afternoon on.</summary>
    [TestMethod]
    public void The_warning_says_how_many_classes_are_at_risk()
    {
        var session = Discovered(classes: 14);

        session.SetDescription("a lending library");

        StringAssert.Contains(session.PendingDiscard!, "14 classes");
    }

    [TestMethod]
    public void One_class_is_not_called_one_classes()
    {
        var session = Discovered(classes: 1);

        session.SetDescription("a lending library");

        StringAssert.Contains(session.PendingDiscard!, "1 class,");
    }

    [TestMethod]
    public void Confirming_applies_the_change_and_discards_the_model()
    {
        var session = Discovered();

        session.SetDescription("a lending library");
        session.ConfirmPending();

        Assert.AreEqual("a lending library", session.Description);
        Assert.IsNull(session.Model);
        Assert.IsNull(session.PendingDiscard);
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Discover));
    }

    [TestMethod]
    public void Keeping_the_model_applies_nothing()
    {
        var session = Discovered();

        session.SetDescription("a lending library");
        session.CancelPending();

        Assert.AreEqual("a shop", session.Description);
        Assert.IsNotNull(session.Model);
        Assert.IsNull(session.PendingDiscard);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Discover));
    }

    [TestMethod]
    public void A_changed_data_source_asks_too()
    {
        var session = Discovered();

        var applied = session.SetSource(new DataSourceSpec(DataSourceKind.Csv, @"C:\o.csv"));

        Assert.IsFalse(applied);
        Assert.IsNull(session.Source);
        Assert.IsNotNull(session.PendingDiscard);
    }

    [TestMethod]
    public void Choosing_no_data_asks_too()
    {
        var session = DiscoveredFromAFile();

        var applied = session.SetNoData();

        Assert.IsFalse(applied);
        Assert.IsNotNull(session.Source);
        Assert.IsNotNull(session.PendingDiscard);
    }

    /// <summary>
    /// Nothing to lose, nothing to ask. The common path — answering the interview the first time
    /// through — must not grow a confirmation click.
    /// </summary>
    [TestMethod]
    public void With_no_model_yet_the_change_just_applies()
    {
        var session = new BuildSession("a shop", @"C:\ws", AppCustomization.Default);

        var applied = session.SetDescription("a lending library");

        Assert.IsTrue(applied);
        Assert.AreEqual("a lending library", session.Description);
        Assert.IsNull(session.PendingDiscard);
    }

    /// <summary>
    /// Clicking back into a node to read the answer and pressing Continue is not a change, and
    /// asking there would train the user to dismiss the question without reading it.
    /// </summary>
    [TestMethod]
    public void Re_answering_with_the_same_value_asks_nothing()
    {
        var session = Discovered();

        var applied = session.SetDescription("a shop");

        Assert.IsTrue(applied);
        Assert.IsNotNull(session.Model);
        Assert.IsNull(session.PendingDiscard);
    }

    /// <summary>
    /// The application type and the customization are not read until Generate, so neither costs
    /// the model anything — the invalidation table says so, and the question must follow it
    /// rather than be asked out of caution.
    /// </summary>
    [TestMethod]
    public void An_answer_that_costs_the_model_nothing_asks_nothing()
    {
        var session = Discovered();

        var applied = session.SetTarget(new ConsoleTarget());

        Assert.IsTrue(applied);
        Assert.IsNotNull(session.Model);
        Assert.IsNull(session.PendingDiscard);
    }

    /// <summary>Confirming with nothing held is a wiring bug, not a no-op: it would mean the page
    /// drew a question the session was not asking.</summary>
    [TestMethod]
    public void Confirming_nothing_is_refused()
    {
        var session = Discovered();

        Assert.ThrowsExactly<InvalidOperationException>(() => session.ConfirmPending());
    }

    /// <summary>
    /// A session parked on the review node with a model in hand. Every setter called here is
    /// called before there is a model to lose, so none of them asks anything — which is itself
    /// the shape of a first run through the interview.
    /// </summary>
    private static BuildSession Discovered(int classes = 3)
    {
        var session = new BuildSession("a shop", @"C:\ws", AppCustomization.Default);

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

    /// <summary>The same, down the other branch of node 3, so that changing the source is a
    /// change from something rather than from nothing.</summary>
    private static BuildSession DiscoveredFromAFile()
    {
        var session = new BuildSession("a shop", @"C:\ws", AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetSource(new DataSourceSpec(DataSourceKind.Csv, @"C:\rows.csv"));
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(ModelOf(3));
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

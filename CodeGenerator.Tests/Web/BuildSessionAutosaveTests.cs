using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// When the session offers itself up to be saved.
/// </summary>
[TestClass]
public class BuildSessionAutosaveTests
{
    [TestMethod]
    public void An_answer_asks_to_be_saved()
    {
        var saves = 0;
        var session = New();
        session.Autosave = _ => saves++;

        session.SetDescription("a library");

        Assert.IsGreaterThan(0, saves);
    }

    /// <summary>
    /// Re-answering with the same value is not a change — the rule that makes clicking back into
    /// a node harmless — so it is not worth a write either.
    /// </summary>
    [TestMethod]
    public void Re_answering_with_the_same_value_saves_nothing()
    {
        var session = New();
        session.SetDescription("a library");

        var saves = 0;
        session.Autosave = _ => saves++;

        session.SetDescription("a library");

        Assert.AreEqual(0, saves);
    }

    /// <summary>
    /// The wrinkle this whole feature turns on. ReviewStage edits the model IN PLACE through
    /// ModelEditor, without going through Change() — so an autosave hooked to answers alone would
    /// miss every rename, deletion and promotion, which is exactly the work being protected.
    /// </summary>
    [TestMethod]
    public void A_model_edit_asks_to_be_saved()
    {
        var session = New();
        session.SetModel(new CandidateDomainModel());

        var saves = 0;
        session.Autosave = _ => saves++;

        session.ModelEdited();

        Assert.AreEqual(1, saves);
    }

    /// <summary>Every existing test constructs a session without one of these, and none of them
    /// should start writing files.</summary>
    [TestMethod]
    public void A_session_with_no_autosave_is_untroubled()
    {
        var session = New();

        session.SetDescription("a library");
        session.SetModel(new CandidateDomainModel());
        session.ModelEdited();
    }

    [TestMethod]
    public void A_completed_build_is_not_offered_again()
    {
        var finished = 0;
        var session = New();
        session.OnFinished = _ => finished++;

        session.Complete(new BuildOutcome(SessionPhase.Generated, []));

        Assert.AreEqual(1, finished);
    }

    [TestMethod]
    public void A_cancelled_build_is_not_offered_again()
    {
        var finished = 0;
        var session = New();
        session.OnFinished = _ => finished++;

        session.Cancel();

        Assert.AreEqual(1, finished);
    }

    /// <summary>
    /// The closed tab, and the entire point of the feature. Abandoning must NOT clear.
    /// </summary>
    [TestMethod]
    public void An_abandoned_build_is_kept()
    {
        var finished = 0;
        var session = New();
        session.OnFinished = _ => finished++;

        session.Abandon();

        Assert.AreEqual(0, finished);
    }

    private static BuildSession New()
    {
        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());

        return session;
    }
}

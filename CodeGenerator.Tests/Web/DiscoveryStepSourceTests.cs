using AppScaffolding;
using CodeGenerator.Web.Workflow;
using DomainDiscovery;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// That the step returns a labelled outcome, and that the label survives to the stage that
/// actually renders it. The source itself is covered in DomainDiscovery.Tests.
/// </summary>
[TestClass]
public class DiscoveryStepSourceTests
{
    [TestMethod]
    public void DiscoverReturnsAnOutcomeCarryingItsSourceLabel()
    {
        var outcome = DiscoveryStep.Discover("a shop that sells bicycles", Path.GetTempPath());

        Assert.IsNotNull(outcome);
        Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.SourceLabel));
        Assert.IsNotNull(outcome.Model);
    }

    /// <summary>
    /// The Discover panel is torn down by Advance() the instant discovery finishes, so the source
    /// label cannot live on that component. It has to reach Review through the session.
    /// </summary>
    [TestMethod]
    public void TheSourceLabelSurvivesOnTheSession()
    {
        var session = new BuildSession(
            "a shop that sells bicycles", Path.GetTempPath(), AppCustomization.Default);

        session.SetModel(new CandidateDomainModel(), "qwen2.5:3b (Ollama)");

        Assert.AreEqual("qwen2.5:3b (Ollama)", session.ModelSource);
    }

    [TestMethod]
    public void ASetModelCallerThatNamesNoSourceLeavesItEmpty()
    {
        var session = new BuildSession(
            "a shop that sells bicycles", Path.GetTempPath(), AppCustomization.Default);

        session.SetModel(new CandidateDomainModel());

        Assert.AreEqual(string.Empty, session.ModelSource);
    }

    /// <summary>
    /// A fallback outcome still has <c>Succeeded == true</c> — the factory recovered — but its
    /// <c>Message</c> carries the reason Ollama was skipped. This exercises the actual
    /// composition — <see cref="DiscoveryOutcomeDescription.Describe"/>, the same helper
    /// DiscoverStage and BuildAppCommand both call — through the session, so it would fail if
    /// either call site stopped calling it or the helper dropped the reason. Coverage of
    /// <c>Describe</c>'s own cases with exact strings lives in
    /// DomainDiscovery.Tests.DiscoveryOutcomeDescriptionTests.
    /// </summary>
    [TestMethod]
    public void TheFallbackReasonSurvivesOnTheSession()
    {
        var session = new BuildSession(
            "a shop that sells bicycles", Path.GetTempPath(), AppCustomization.Default);

        const string sourceLabel = "Wikipedia (fallback)";
        const string message = "qwen2.5:3b (Ollama) was configured but did not answer. "
            + "Falling back to Wikipedia.";

        var outcome = new DiscoveryOutcome(new CandidateDomainModel(), sourceLabel, true, message);

        session.SetModel(new CandidateDomainModel(), outcome.Describe());

        StringAssert.Contains(session.ModelSource, sourceLabel);
        StringAssert.Contains(session.ModelSource, message);
    }
}

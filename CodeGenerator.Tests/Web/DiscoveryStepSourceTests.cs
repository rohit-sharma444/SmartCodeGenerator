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
    /// <c>Message</c> carries the reason Ollama was skipped. Composing the label the way
    /// DiscoverStage (and BuildAppCommand) now do for the success path must not drop that reason;
    /// otherwise the user sees "Classes from Wikipedia (fallback)." with no explanation why.
    /// </summary>
    [TestMethod]
    public void TheFallbackReasonSurvivesOnTheSession()
    {
        var session = new BuildSession(
            "a shop that sells bicycles", Path.GetTempPath(), AppCustomization.Default);

        const string sourceLabel = "Wikipedia (fallback)";
        const string message = "qwen2.5:3b (Ollama) was configured but did not answer. "
            + "Falling back to Wikipedia.";

        session.SetModel(new CandidateDomainModel(), $"Classes from {sourceLabel}. {message}");

        StringAssert.Contains(session.ModelSource, sourceLabel);
        StringAssert.Contains(session.ModelSource, message);
    }
}

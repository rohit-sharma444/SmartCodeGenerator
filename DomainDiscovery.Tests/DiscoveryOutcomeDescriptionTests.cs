using DomainDiscovery;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests;

/// <summary>
/// The one place the user-facing discovery line is composed, shared by the CLI
/// (<c>BuildAppCommand</c>) and the web panel (<c>DiscoverStage</c>). Both surfaces used to
/// hand-compose this string themselves — this covers the three cases directly against the shared
/// helper, with exact strings, so the two call sites cannot drift apart again unnoticed.
/// </summary>
public class DiscoveryOutcomeDescriptionTests
{
    [Fact]
    public void APlainSuccessNamesOnlyTheSource()
    {
        var outcome = new DiscoveryOutcome(new CandidateDomainModel(), "Wikipedia", true, null);

        Assert.Equal("Classes from Wikipedia.", outcome.Describe());
    }

    [Fact]
    public void ASuccessfulFallbackAppendsTheMessageWithNoStraySpacing()
    {
        const string message = "qwen2.5:3b (Ollama) was configured but did not answer. "
            + "Falling back to Wikipedia.";

        var outcome = new DiscoveryOutcome(
            new CandidateDomainModel(), "Wikipedia (fallback)", true, message);

        Assert.Equal(
            "Classes from Wikipedia (fallback). qwen2.5:3b (Ollama) was configured but did not "
            + "answer. Falling back to Wikipedia.",
            outcome.Describe());
    }

    [Fact]
    public void AFailurePutsTheSourceInParenthesesAfterTheMessage()
    {
        var outcome = new DiscoveryOutcome(
            new CandidateDomainModel(), "Ollama", false, "The model returned no usable classes.");

        Assert.Equal(
            "The model returned no usable classes. (Source: Ollama)", outcome.Describe());
    }
}

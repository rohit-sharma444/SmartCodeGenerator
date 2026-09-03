using DomainDiscovery;
using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using DomainDiscovery.Tests.Llm;   // FakeOllamaClient, defined in Task 6's test file
using Xunit;

namespace DomainDiscovery.Tests;

internal sealed class StubSource : IDomainModelSource
{
    public DiscoveryOutcome Discover(string description, Action<IReadOnlyList<string>>? onProgress = null)
        => new(new CandidateDomainModel(), "Wikipedia", true, null);
}

/// <summary>A Wikipedia stub that, like the real one when no topic is recognised, has its own
/// message to report — so a fallback wrapping it has two things to say, not one.</summary>
internal sealed class StubSourceWithMessage : IDomainModelSource
{
    public DiscoveryOutcome Discover(string description, Action<IReadOnlyList<string>>? onProgress = null)
        => new(new CandidateDomainModel(), "Wikipedia", false, "No topic was recognised.");
}

public class DomainModelSourceFactoryTests
{
    private static IDomainModelSource Wikipedia() => new StubSource();

    private static LlmDomainModel OneClass() => new()
    {
        Classes =
        {
            new LlmClass
            {
                Name = "Customer",
                Role = "Party",
                Properties = { new LlmProperty { Name = "Name", Type = "string" } }
            }
        }
    };

    [Fact]
    public void UsesOllamaWhenItIsConfiguredAndAvailable()
    {
        // Behaviour rather than type: the Ollama source is wrapped now, so that it can fall back
        // when a generation fails rather than only when the server is down.
        var built = 0;

        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            () => { built++; return new StubSource(); },
            new FakeOllamaClient { Available = true, Answer = OneClass() });

        var outcome = source.Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Equal("qwen2.5:3b (Ollama)", outcome.SourceLabel);
        Assert.Equal(0, built);
    }

    [Fact]
    public void FallsBackToWikipediaWhenTheModelIsRunningButTheGenerationFails()
    {
        // The failure this whole change is about: the probe says Ollama is up, then the
        // generation stalls or comes back unparseable. That used to leave the stage red with no
        // classes at all, while a working second source sat unused.
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            Wikipedia,
            new FakeOllamaClient { Available = true, Answer = null, Error = "it stopped early" });

        var outcome = source.Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.StartsWith("Wikipedia", outcome.SourceLabel);
    }

    [Fact]
    public void SaysWhyItFellBackAfterAFailedGeneration()
    {
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            Wikipedia,
            new FakeOllamaClient { Available = true, Answer = null, Error = "it stopped early" });

        var outcome = source.Discover("a shop");

        Assert.NotNull(outcome.Message);
        Assert.Contains("it stopped early", outcome.Message);
        Assert.Contains("Falling back to Wikipedia", outcome.Message);
    }

    [Fact]
    public void KeepsAShortAnswerFromTheModelRatherThanReplacingItWithWikipedia()
    {
        // A run stopped early still succeeded. Its classes came from the model the user asked
        // for; swapping them for encyclopaedia-derived ones would be a downgrade, not a rescue.
        var built = 0;

        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            () => { built++; return new StubSource(); },
            new FakeOllamaClient
            {
                Available = true, Answer = OneClass(), Warning = "1 class(es) were kept"
            });

        var outcome = source.Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Equal("qwen2.5:3b (Ollama)", outcome.SourceLabel);
        Assert.Equal("1 class(es) were kept", outcome.Message);
        Assert.Equal(0, built);
    }

    [Fact]
    public void PassesProgressStraightThroughToWhoeverIsDrawingIt()
    {
        var seen = new List<IReadOnlyList<string>>();

        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            Wikipedia,
            new FakeOllamaClient
            {
                Available = true, Answer = OneClass(), ProgressSteps = ["Customer [Party]"]
            });

        source.Discover("a shop", found => seen.Add(found));

        Assert.Equal(["Customer [Party]"], Assert.Single(seen));
    }

    [Fact]
    public void FallsBackToWikipediaWhenOllamaIsNotRunning()
    {
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(), Wikipedia, new FakeOllamaClient { Available = false });

        var outcome = source.Discover("a shop");

        // The underlying source really is the Wikipedia stub — wrapped now, to annotate the
        // outcome, but the discovery it runs is still Wikipedia's.
        Assert.StartsWith("Wikipedia", outcome.SourceLabel);
    }

    [Fact]
    public void UsesWikipediaWhenItIsTheConfiguredSource()
    {
        var config = new DiscoveryConfig { Source = DiscoveryConfig.WikipediaSource };

        var source = DomainModelSourceFactory.Create(
            config, Wikipedia, new FakeOllamaClient { Available = true });

        Assert.IsType<StubSource>(source);
    }

    [Fact]
    public void NeverBuildsTheWikipediaPipelineWhenOllamaAnswers()
    {
        var built = 0;

        DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            () => { built++; return new StubSource(); },
            new FakeOllamaClient { Available = true });

        Assert.Equal(0, built);
    }

    [Fact]
    public void AnnotatesTheOutcomeWhenFallingBackBecauseOllamaIsUnavailable()
    {
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(), Wikipedia, new FakeOllamaClient { Available = false });

        var outcome = source.Discover("a shop");

        Assert.True(
            outcome.SourceLabel.Contains("Ollama", StringComparison.OrdinalIgnoreCase)
            || (outcome.Message?.Contains("Ollama", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    [Fact]
    public void DoesNotAnnotateWhenWikipediaIsExplicitlyConfigured()
    {
        var config = new DiscoveryConfig { Source = DiscoveryConfig.WikipediaSource };

        var source = DomainModelSourceFactory.Create(
            config, Wikipedia, new FakeOllamaClient { Available = true });

        var outcome = source.Discover("a shop");

        Assert.Equal("Wikipedia", outcome.SourceLabel);
        Assert.False(outcome.Message?.Contains("Ollama", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    [Fact]
    public void ConcatenatesTheFallbackReasonWithTheInnerSourcesOwnMessage()
    {
        // Ollama down AND Wikipedia recognising no topic either: the user needs both facts, not
        // whichever one happened to overwrite the other.
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(), () => new StubSourceWithMessage(), new FakeOllamaClient { Available = false });

        var outcome = source.Discover("a shop");

        Assert.NotNull(outcome.Message);
        Assert.Contains("Ollama", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No topic was recognised.", outcome.Message);
    }

    [Fact]
    public void NamesTheUnrecognisedSourceValueWhenFallingBack()
    {
        var config = new DiscoveryConfig { Source = "olama" };

        var source = DomainModelSourceFactory.Create(
            config, Wikipedia, new FakeOllamaClient { Available = true });

        var outcome = source.Discover("a shop");

        Assert.NotNull(outcome.Message);
        Assert.Contains("olama", outcome.Message);
    }
}

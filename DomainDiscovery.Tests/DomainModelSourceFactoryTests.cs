using DomainDiscovery;
using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using DomainDiscovery.Tests.Llm;   // FakeOllamaClient, defined in Task 6's test file
using Xunit;

namespace DomainDiscovery.Tests;

internal sealed class StubSource : IDomainModelSource
{
    public DiscoveryOutcome Discover(string description)
        => new(new CandidateDomainModel(), "Wikipedia", true, null);
}

public class DomainModelSourceFactoryTests
{
    private static IDomainModelSource Wikipedia() => new StubSource();

    [Fact]
    public void UsesOllamaWhenItIsConfiguredAndAvailable()
    {
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(), Wikipedia, new FakeOllamaClient { Available = true });

        Assert.IsType<OllamaDomainModelSource>(source);
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

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

        Assert.IsType<StubSource>(source);
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
}

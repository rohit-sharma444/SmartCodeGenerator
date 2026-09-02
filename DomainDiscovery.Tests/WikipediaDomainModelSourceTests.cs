using DomainDiscovery;
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;   // NOT `Recognition.X`: DomainDiscovery.Tests.Recognition
                                     // already exists and would win the name lookup.
using DomainDiscovery.SeedExtraction;
using DomainDiscovery.Tests.Fakes;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests;

public class WikipediaDomainModelSourceTests
{
    private static DomainDiscoveryPipeline PipelineOver(FakeWikipediaClient client)
    {
        var analyzer = new StubAnalyzer(new SemanticResult());
        return new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client),
            client);
    }

    [Fact]
    public void ReportsFailureAndAMessageWhenNoTopicIsRecognized()
    {
        var source = new WikipediaDomainModelSource(PipelineOver(new FakeWikipediaClient()));

        var outcome = source.Discover("something with no article");

        Assert.False(outcome.Succeeded);
        Assert.Equal(WikipediaDomainModelSource.Label, outcome.SourceLabel);
        Assert.NotNull(outcome.Message);
    }

    [Fact]
    public void CarriesTheModelThroughUnchanged()
    {
        var source = new WikipediaDomainModelSource(PipelineOver(new FakeWikipediaClient()));

        var outcome = source.Discover("orders and customers");

        Assert.NotNull(outcome.Model);
    }
}

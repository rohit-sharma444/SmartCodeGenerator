using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;
using DomainDiscovery.SeedExtraction;
using DomainDiscovery.Tests.Fakes;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests;

public class DomainDiscoveryPipelineTests
{
    private static Entity Ent(string text, EntityType type)
        => new() { Text = text, Type = type, Confidence = 0.9 };

    [Fact]
    public void Discover_ExtractsSeed_RecognizesTopic_AndEnriches()
    {
        // NLE sees the user's text and yields "book" (Object).
        var userAnalysis = new SemanticResult();
        userAnalysis.Entities.Add(Ent("book", EntityType.Object));

        // The article analysis (same stub, since Discover calls Analyze twice)
        // also yields "book"; enrichment adds nothing new but must not throw.
        var analyzer = new StubAnalyzer(userAnalysis);

        var client = new FakeWikipediaClient();
        client.SearchHits.Add(new SearchHit("Library", "https://en.wikipedia.org/wiki/Library", 0));
        client.Extracts["Library"] = "A library lends books.";

        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client));

        var result = pipeline.Discover("track books that members borrow");

        Assert.Equal("Library", result.Topic.Title);
        Assert.Contains(result.Model.Entities, e => e.Name == "Book");
    }

    [Fact]
    public void Discover_NoTopic_ReturnsSeedOnly_ShapedFromTheLexicon()
    {
        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("invoice", EntityType.Object));
        var analyzer = new StubAnalyzer(analysis);

        var client = new FakeWikipediaClient(); // no search hits

        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client));

        var result = pipeline.Discover("track invoices");

        Assert.False(result.Topic.IsRecognized);
        Assert.Equal(new[] { "Invoice" }, result.Model.Entities.Select(e => e.Name));
        Assert.Equal(
            new[] { "Number", "Amount", "DueDate", "Paid" },
            result.Model.Entities[0].Fields.Select(f => f.Name));
    }

    [Fact]
    public void Discover_MovesASeedNoun_TheLexiconAndWikipediaCannotShape_ToSuggestions()
    {
        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("widget", EntityType.Object));
        var analyzer = new StubAnalyzer(analysis);

        var client = new FakeWikipediaClient();
        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client));

        var result = pipeline.Discover("manage widgets");

        // SeedEntityExtractor turned "widget" into a Provenance.Seed entity with no fields,
        // same as every other noun in the typed phrase. The lexicon does not know it and its
        // lead cannot be fetched (FakeWikipediaClient has no entry), so it is offered as a
        // suggestion rather than kept as junk.
        Assert.Empty(result.Model.Entities);
        Assert.Equal(new[] { "Widget" }, result.Model.Suggestions.Select(e => e.Name));
        Assert.Empty(result.Model.Rejected);
    }
}

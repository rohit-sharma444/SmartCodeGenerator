using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Tests.Fakes;
using NaturalLanguageEngine.Core;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class DomainEnricherTests
{
    private static Entity Ent(string text, EntityType type)
        => new() { Text = text, Type = type, Confidence = 0.9 };

    [Fact]
    public void Enrich_AddsHarvestedEntities_KeepingSeed()
    {
        var seed = new CandidateDomainModel();
        seed.Entities.Add(new CandidateEntity { Name = "Book", Provenance = Provenance.Seed });

        // Twice each: the harvester ignores nouns an article mentions only once.
        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("loan", EntityType.Object));   // new, from wiki
        analysis.Entities.Add(Ent("loan", EntityType.Object));
        analysis.Entities.Add(Ent("book", EntityType.Object));   // dup of seed
        analysis.Entities.Add(Ent("book", EntityType.Object));

        var client = new FakeWikipediaClient();
        client.Extracts["Library"] = "ignored - stub analyzer drives output";

        var enricher = new DomainEnricher(client, new StubAnalyzer(analysis), new EntityHarvester());
        var topic = new TopicResult("Library", "https://en.wikipedia.org/wiki/Library", 0.8);

        var model = enricher.Enrich(topic, seed);

        Assert.Equal(new[] { "Book", "Loan" }, model.Entities.Select(e => e.Name));
        Assert.Equal(Provenance.Seed, model.Entities.Single(e => e.Name == "Book").Provenance);
        Assert.Equal(Provenance.Wiki, model.Entities.Single(e => e.Name == "Loan").Provenance);
    }

    [Fact]
    public void Enrich_AddsALinkedConcept_ProseNounCountingCannotSee()
    {
        // "cash register" tokenizes into two common nouns and normalizes to junk, so the
        // harvester can never surface it - but the article links it.
        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] =
            "The cash register records each sale. The cash register prints a receipt.";
        wiki.Links["Point of sale"] = new List<string> { "Cash register" };

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich(
            new TopicResult("Point of sale", "https://en.wikipedia.org/wiki/Point_of_sale", 0.5),
            new CandidateDomainModel());

        var register = Assert.Single(model.Entities, e => e.Name == "CashRegister");
        Assert.Equal("Cash register", register.SourceTitle);
        Assert.Equal(Provenance.Wiki, register.Provenance);
    }
}

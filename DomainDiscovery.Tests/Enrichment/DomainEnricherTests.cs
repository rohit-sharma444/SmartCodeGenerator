using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Tests.Fakes;
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

        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("loan", EntityType.Object));   // new, from wiki
        analysis.Entities.Add(Ent("book", EntityType.Object));   // dup of seed

        var client = new FakeWikipediaClient();
        client.Extracts["Library"] = "ignored - stub analyzer drives output";

        var enricher = new DomainEnricher(client, new StubAnalyzer(analysis), new EntityHarvester());
        var topic = new TopicResult("Library", "https://en.wikipedia.org/wiki/Library", 0.8);

        var model = enricher.Enrich(topic, seed);

        Assert.Equal(new[] { "Book", "Loan" }, model.Entities.Select(e => e.Name));
        Assert.Equal(Provenance.Seed, model.Entities.Single(e => e.Name == "Book").Provenance);
        Assert.Equal(Provenance.Wiki, model.Entities.Single(e => e.Name == "Loan").Provenance);
    }
}

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

        var model = enricher.Enrich("Library", seed);

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
            "Point of sale",
            new CandidateDomainModel());

        var register = Assert.Single(model.Entities, e => e.Name == "CashRegister");
        Assert.Equal("Cash register", register.SourceTitle);
        Assert.Equal(Provenance.Wiki, register.Provenance);
    }

    /// <summary>
    /// The real "Point of sale gave us a Smartphone class" failure, in miniature. Both candidate
    /// sources reach past the domain into whatever else the article discusses, and the article's
    /// own lead is what tells them apart.
    /// </summary>
    [Fact]
    public void Enrich_DemotesALinkedConcept_TheArticlesLeadDoesNotName()
    {
        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] = string.Join('\n',
            "The customer pays the merchant at a cash register.",
            "A cash register records each sale and prints a receipt.",
            "Hardware",
            "Modern systems run on a smartphone. A smartphone can replace the terminal.");
        wiki.Links["Point of sale"] = new List<string> { "Cash register", "Smartphone" };

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich(
            "Point of sale",
            new CandidateDomainModel());

        // Named where the article defines itself, so it is what the domain is made of.
        Assert.Contains(model.Entities, e => e.Name == "CashRegister");

        // Discussed, but only as hardware the thing runs on. Offered, not deleted.
        Assert.DoesNotContain(model.Entities, e => e.Name == "Smartphone");
        Assert.Contains(model.Suggestions, e => e.Name == "Smartphone");
    }

    [Fact]
    public void Enrich_KeepsASeededName_EvenWhenTheLeadDoesNotNameIt()
    {
        // The user typed it, so it is a class because they said so.
        var seed = new CandidateDomainModel();
        seed.Entities.Add(new CandidateEntity { Name = "Smartphone", Provenance = Provenance.Seed });

        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] = string.Join('\n',
            "The customer pays the merchant at a cash register.",
            "Hardware",
            "Modern systems run on a smartphone. A smartphone can replace the terminal.");
        wiki.Links["Point of sale"] = new List<string> { "Smartphone" };

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich(
            "Point of sale", seed);

        Assert.Contains(model.Entities, e => e.Name == "Smartphone");
        Assert.DoesNotContain(model.Suggestions, e => e.Name == "Smartphone");
    }
}

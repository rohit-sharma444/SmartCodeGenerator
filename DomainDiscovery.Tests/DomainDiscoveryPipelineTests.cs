using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;
using DomainDiscovery.SeedExtraction;
using DomainDiscovery.Tests.Fakes;
using DomainDiscovery.Wikipedia.Dump;
using Microsoft.Data.Sqlite;
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
            new EntityShaper(client),
            client);

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
            new EntityShaper(client),
            client);

        var result = pipeline.Discover("track invoices");

        Assert.False(result.Topic.IsRecognized);
        Assert.Equal(new[] { "Invoice" }, result.Model.Entities.Select(e => e.Name));

        // Curated first, in order; EntityShaper's third pass appends the Document role baseline.
        var curated = new[] { "Number", "Amount", "DueDate", "Paid" };
        Assert.Equal(
            curated,
            result.Model.Entities[0].Fields.Select(f => f.Name).Take(curated.Length));
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
            new EntityShaper(client),
            client);

        var result = pipeline.Discover("manage widgets");

        // SeedEntityExtractor turned "widget" into a Provenance.Seed entity with no fields,
        // same as every other noun in the typed phrase. The lexicon does not know it and its
        // lead cannot be fetched (FakeWikipediaClient has no entry), so it is offered as a
        // suggestion rather than kept as junk.
        Assert.Empty(result.Model.Entities);
        Assert.Equal(new[] { "Widget" }, result.Model.Suggestions.Select(e => e.Name));
        Assert.Empty(result.Model.Rejected);
    }

    [Fact]
    public void Discover_EndToEnd_ShapesALinkedConcept_AndRejectsAnAbstraction()
    {
        // Spec: docs/superpowers/specs/2026-07-12-lexicon-gate-design.md, Testing section —
        // "Pipeline integration with a fake client over a POS fixture: tier 1 contains
        // CashRegister and Receipt; System, Data, Software and Business are absent." This is
        // that test: it drives DomainEnricher -> EntityShaper together over a fixture shaped
        // like the Point of sale article, with only the Wikipedia client faked (no network).
        // Until now this behaviour was only proven by a manual CLI run.
        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("register", EntityType.Object));
        var analyzer = new StubAnalyzer(analysis);

        var client = new FakeWikipediaClient();
        // Recognition searches on the seed name ("Register"), and must land on the topic
        // article regardless of what the article's own prose says.
        client.HitsByTerm["Register"] = new List<SearchHit>
        {
            new("Point of sale", "https://en.wikipedia.org/wiki/Point_of_sale", 0)
        };

        // The article text feeds both the prose harvester (via the same stub analyzer) and
        // LinkedConceptSource's salience check, which requires a lowercase, twice-repeated
        // mention of a link title before it becomes a candidate at all.
        const string articleText =
            "A point of sale system uses a cash register to record each sale. " +
            "The cash register and the system are both central to retail operations.";
        client.Extracts["Point of sale"] = articleText;
        client.Links["Point of sale"] = new List<string> { "Cash register", "System" };

        // Each linked candidate is shaped from its OWN article's definition, per the design:
        // a device archetype for the register, an abstract head ("group") for System.
        client.Leads["Cash register"] = "A cash register is a device for registering sales.";
        client.Leads["System"] = "A system is a group of interacting elements.";

        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client),
            client);

        var result = pipeline.Discover("track registers");

        var cashRegister = Assert.Single(result.Model.Entities, e => e.Name == "CashRegister");

        // Archetype first, in order; the Device role baseline follows it.
        var archetype = new[] { "Name", "Model", "SerialNumber" };
        Assert.Equal(
            archetype,
            cashRegister.Fields.Select(f => f.Name).Take(archetype.Length));

        Assert.DoesNotContain(result.Model.Entities, e => e.Name == "System");
        Assert.Contains("System", result.Model.Rejected);
    }

    [Fact]
    public void Discover_MinesRelationships_AfterShaping_ReceiptReferencesCustomerOneWay()
    {
        // Spec: docs/superpowers/specs/2026-07-12-relationships-design.md. Mining must run
        // AFTER Shape (it needs the final class list and each class's Role) and must re-read
        // the same article DomainEnricher already fetched — via the same IWikipediaClient the
        // pipeline is given here.
        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("receipt", EntityType.Object));
        analysis.Entities.Add(Ent("customer", EntityType.Object));
        var analyzer = new StubAnalyzer(analysis);

        var client = new FakeWikipediaClient();
        client.SearchHits.Add(new SearchHit("Point of sale", "https://en.wikipedia.org/wiki/Point_of_sale", 0));

        // Receipt (Document) and Customer (Party) are both curated in DomainFieldLexicon, so
        // both resolve fields and Role from pass 1 of EntityShaper — no lead fetch needed for
        // either, so the article below only has to feed the miner.
        const string sentence = "The customer was handed a receipt by the cashier. ";
        var article = string.Concat(Enumerable.Repeat(sentence, RelationshipMiner.MinCoOccurrences));
        client.Extracts["Point of sale"] = article;

        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client),
            client);

        var result = pipeline.Discover("track receipts given to customers");

        var receipt = Assert.Single(result.Model.Entities, e => e.Name == "Receipt");
        var customer = Assert.Single(result.Model.Entities, e => e.Name == "Customer");

        Assert.Contains(receipt.Relationships, r => r.Target == "Customer" && r.Role == "references");
        Assert.Contains(receipt.Fields, f => f.Name == "CustomerId" && f.Type == "int");

        // Document (rank 4) outranks Party (rank 1): the link only goes one way.
        Assert.Empty(customer.Relationships);
        Assert.DoesNotContain(customer.Fields, f => f.Name == "ReceiptId");
    }

    [Fact]
    public void Discover_NoTopic_NeverMinesRelationships_AndDoesNotCrash()
    {
        // The unrecognized-topic path has no article to mine at all, so it must be entirely
        // unaffected by wiring the miner in: no crash, no relationships anywhere.
        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("receipt", EntityType.Object));
        analysis.Entities.Add(Ent("customer", EntityType.Object));
        var analyzer = new StubAnalyzer(analysis);

        var client = new FakeWikipediaClient(); // no search hits => topic is never recognized

        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client),
            client);

        var result = pipeline.Discover("track receipts given to customers");

        Assert.False(result.Topic.IsRecognized);
        Assert.All(result.Model.Entities, e => Assert.Empty(e.Relationships));
    }

    [Fact]
    public void CreateDefault_falls_back_to_the_http_client_when_no_dump_store_exists()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), $"cache-{Guid.NewGuid():N}");
        var missingStore = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.db");

        // Must not throw, and must not create a store: an unbuilt dump is a reason to use the
        // network, not a reason to fail.
        var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDirectory, missingStore);

        Assert.NotNull(pipeline);
        Assert.False(File.Exists(missingStore));
    }

    [Fact]
    public void CreateDefault_refuses_a_store_that_has_no_search_index()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), $"cache-{Guid.NewGuid():N}");
        var storePath = Path.Combine(Path.GetTempPath(), $"unindexed-{Guid.NewGuid():N}.db");

        try
        {
            // Precisely what an import killed before its final step leaves behind: the rows are
            // there, the search index is not.
            new WikipediaStore(storePath).CreateSchema();

            var error = Assert.Throws<InvalidOperationException>(
                () => DomainDiscoveryPipeline.CreateDefault(cacheDirectory, storePath));

            // The message has to carry the cure, not just the diagnosis.
            Assert.Contains("--index-only", error.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(storePath))
                File.Delete(storePath);
        }
    }
}

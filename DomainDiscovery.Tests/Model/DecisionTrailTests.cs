using DomainDiscovery;
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;
using DomainDiscovery.SeedExtraction;
using DomainDiscovery.Tests.Fakes;
using NaturalLanguageEngine.Core;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.Model;

public class DecisionTrailTests
{
    private static Entity Ent(string text, EntityType type, double confidence = 0.9)
        => new() { Text = text, Type = type, Confidence = confidence };

    private static SemanticResult Analysis(params Entity[] entities)
    {
        var result = new SemanticResult();
        foreach (var entity in entities)
            result.Entities.Add(entity);
        return result;
    }

    private static DecisionRecord RecordFor(CandidateDomainModel model, string name)
        => Assert.Single(model.Decisions,
            d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void SeedExtraction_RecordsAKeptEntity_WithItsConfidence()
    {
        var model = new SeedEntityExtractor().Extract(Analysis(Ent("book", EntityType.Object, 0.82)));

        var record = RecordFor(model, "Book");
        Assert.Equal(DecisionStage.SeedExtraction, record.Stage);
        Assert.Equal(DecisionOutcome.Kept, record.Outcome);
        Assert.Equal(DecisionReasons.YouNamedIt, record.Reason);
        Assert.Contains("0.82", record.Evidence);
        Assert.Contains("book", record.Evidence);
    }

    [Fact]
    public void SeedExtraction_RecordsANounTheEngineDidNotTypeAsAThing()
    {
        // This is the case behind "I said it and got no class" - the word never became a
        // candidate at all, so nothing downstream could ever explain it.
        var model = new SeedEntityExtractor().Extract(Analysis(Ent("quickly", EntityType.Unknown)));

        var record = RecordFor(model, "quickly");
        Assert.Equal(DecisionStage.SeedExtraction, record.Stage);
        Assert.Equal(DecisionOutcome.Skipped, record.Outcome);
        Assert.Equal(DecisionReasons.NotRecognizedAsAThing, record.Reason);
        Assert.Contains("Unknown", record.Evidence);
        Assert.Empty(model.Entities);
    }

    [Fact]
    public void SeedExtraction_RecordsADuplicateName()
    {
        var model = new SeedEntityExtractor().Extract(
            Analysis(Ent("book", EntityType.Object), Ent("books", EntityType.Object)));

        Assert.Single(model.Entities);

        var duplicate = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.AlreadyNamed);
        Assert.Equal(DecisionOutcome.Skipped, duplicate.Outcome);
        Assert.Equal("books", duplicate.Name);
        Assert.Contains("already taken by Book", duplicate.Evidence);
    }

    [Fact]
    public void SeedExtraction_RecordsANameThatNormalizesToNothing()
    {
        var model = new SeedEntityExtractor().Extract(Analysis(Ent("!!!", EntityType.Object)));

        var record = RecordFor(model, "!!!");
        Assert.Equal(DecisionOutcome.Skipped, record.Outcome);
        Assert.Equal(DecisionReasons.NameNormalizedToNothing, record.Reason);
        Assert.Empty(model.Entities);
    }

    [Fact]
    public void Enrichment_RecordsACandidateTheLeadDoesNotMention()
    {
        // The lead defines a point of sale without naming a smartphone, so Smartphone is
        // demoted to a suggestion. Today that happens invisibly.
        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] = string.Join('\n',
            "The point of sale is where a sale is completed.",
            "Hardware",
            "Modern systems run on a smartphone. A smartphone can replace the terminal.");

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich("Point of sale", new CandidateDomainModel());

        var demoted = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.LeadDoesNotMentionIt
                 && string.Equals(d.Name, "Smartphone", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(DecisionStage.Enrichment, demoted.Stage);
        Assert.Equal(DecisionOutcome.Suggested, demoted.Outcome);
        Assert.Contains("Point of sale", demoted.Evidence);
    }

    [Fact]
    public void Enrichment_CarriesItsTrailAcrossTheMergeIntoTheSeedModel()
    {
        // Enrich records against the HARVESTED model, which is a different instance from the
        // seed it returns. Without an explicit copy in Merge the whole stage's trail is lost -
        // silently, since the entities still arrive.
        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] = string.Join('\n',
            "The point of sale is where a sale is completed.",
            "Hardware",
            "Modern systems run on a smartphone. A smartphone can replace the terminal.");

        var seed = new CandidateDomainModel();
        seed.Record("Book", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt);

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich("Point of sale", seed);

        Assert.Same(seed, model);
        Assert.Contains(model.Decisions, d => d.Stage == DecisionStage.SeedExtraction);
        Assert.Contains(model.Decisions, d => d.Stage == DecisionStage.Enrichment);
    }

    [Fact]
    public void Enrichment_RecordsASuggestionTheUserAlreadyNamedAsAClass()
    {
        // A demoted candidate the seed already claims is dropped rather than offered back.
        // The user should be able to find out that it was not lost.
        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] = string.Join('\n',
            "The point of sale is where a sale is completed.",
            "Hardware",
            "Modern systems run on a smartphone. A smartphone can replace the terminal.");

        var seed = new CandidateDomainModel();
        seed.Entities.Add(new CandidateEntity { Name = "Smartphone", Provenance = Provenance.Seed });

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich("Point of sale", seed);

        var dropped = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.AlreadyAClassFromYourDescription);
        Assert.Equal(DecisionOutcome.Skipped, dropped.Outcome);
        Assert.Empty(model.Suggestions);
    }

    private static CandidateDomainModel WikiModel(params string[] names)
    {
        var model = new CandidateDomainModel();
        foreach (var name in names)
            model.Entities.Add(new CandidateEntity { Name = name, Provenance = Provenance.Wiki });
        return model;
    }

    [Fact]
    public void Shaping_RecordsARejection_WithTheHeadNounThatCausedIt()
    {
        var wiki = new FakeWikipediaClient();
        wiki.Leads["Administration"] = "Administration is a process of managing an organization.";

        var model = WikiModel("Administration");
        new EntityShaper(wiki).Shape(model);

        var record = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.DefinitionResolvesToAnAbstraction);
        Assert.Equal("Administration", record.Name);
        Assert.Equal(DecisionStage.Shaping, record.Stage);
        Assert.Equal(DecisionOutcome.Rejected, record.Outcome);
        Assert.Contains("process", record.Evidence);

        // The existing collections must behave exactly as before.
        Assert.Empty(model.Entities);
        Assert.Contains("Administration", model.Rejected);
    }

    [Fact]
    public void Shaping_RecordsASuggestion_WhenNoDefinitionIsRecognized()
    {
        var wiki = new FakeWikipediaClient();
        wiki.Leads["Widget"] = "Widget is a widget of widgets.";

        var model = WikiModel("Widget");
        new EntityShaper(wiki).Shape(model);

        var record = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.NoDefinitionWeRecognize);
        Assert.Equal(DecisionOutcome.Suggested, record.Outcome);
        Assert.Contains(model.Suggestions, e => e.Name == "Widget");
    }

    [Fact]
    public void Shaping_RecordsASeedKept_DespiteHavingNoDefinition()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Widget", Provenance = Provenance.Seed });

        new EntityShaper(new FakeWikipediaClient()).Shape(model);

        var record = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.YouNamedItNoDefinitionFound);
        Assert.Equal(DecisionOutcome.Kept, record.Outcome);
        Assert.Single(model.Entities);
    }

    [Fact]
    public void Shaping_RecordsACuratedClass_WhichPassTwoNeverSees()
    {
        // Recipe is in the curated lexicon, so pass 1 gives it fields and pass 2 skips it.
        // Without a record in pass 1 a curated class has no trail at all.
        var model = WikiModel("Recipe");

        new EntityShaper(new FakeWikipediaClient()).Shape(model);

        var record = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.DefinedAsADomainClass);
        Assert.Equal("Recipe", record.Name);
        Assert.Equal(DecisionOutcome.Kept, record.Outcome);
    }

    [Fact]
    public void ASeedOnlyRun_StillCarriesItsSeedExtractionTrail()
    {
        // No topic recognized means enrichment never runs, and Shape is called on the seed
        // model directly. The seed-extraction trail must survive that path too - it is the
        // only trail such a run has.
        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("invoice", EntityType.Object));
        analysis.Entities.Add(Ent("quickly", EntityType.Unknown));
        var analyzer = new StubAnalyzer(analysis);

        var client = new FakeWikipediaClient();   // no search hits, so no topic

        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client),
            client);

        var result = pipeline.Discover("track invoices");

        Assert.False(result.Topic.IsRecognized);
        Assert.Contains(result.Model.Decisions,
            d => d.Stage == DecisionStage.SeedExtraction
                 && d.Reason == DecisionReasons.NotRecognizedAsAThing);
        Assert.Contains(result.Model.Decisions,
            d => d.Stage == DecisionStage.SeedExtraction && d.Outcome == DecisionOutcome.Kept);
    }
}

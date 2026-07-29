using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
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
}

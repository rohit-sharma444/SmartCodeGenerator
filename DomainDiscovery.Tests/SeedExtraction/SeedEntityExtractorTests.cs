using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.SeedExtraction;

public class SeedEntityExtractorTests
{
    private static Entity Ent(string text, EntityType type)
        => new() { Text = text, Type = type, Confidence = 0.9 };

    [Fact]
    public void Extract_KeepsObjectPersonOrgLocation_AsPascalCase()
    {
        var result = new SemanticResult();
        result.Entities.Add(Ent("book", EntityType.Object));
        result.Entities.Add(Ent("member", EntityType.Person));
        result.Entities.Add(Ent("read", EntityType.Action));     // dropped: verb, and no record (see TransactionVerbs)
        result.Entities.Add(Ent("late", EntityType.Attribute));  // dropped: attribute

        var model = new SeedEntityExtractor().Extract(result);

        Assert.Equal(new[] { "Book", "Member" }, model.Entities.Select(e => e.Name));
        Assert.All(model.Entities, e => Assert.Equal(Provenance.Seed, e.Provenance));
    }

    [Fact]
    public void Extract_DeduplicatesByName_CaseInsensitive()
    {
        var result = new SemanticResult();
        result.Entities.Add(Ent("Book", EntityType.Object));
        result.Entities.Add(Ent("book", EntityType.Object));

        var model = new SeedEntityExtractor().Extract(result);

        Assert.Single(model.Entities);
    }

    [Fact]
    public void Extract_LeavesFieldsToTheFieldAssigner()
    {
        var result = new SemanticResult();
        result.Entities.Add(Ent("Book", EntityType.Object));

        var model = new SeedEntityExtractor().Extract(result);

        // Seed names drive topic recognition, so they are shaped only once the article is known.
        Assert.Empty(model.Entities[0].Fields);
    }
}

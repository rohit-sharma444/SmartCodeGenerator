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
        result.Entities.Add(Ent("borrow", EntityType.Action));   // dropped: verb
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
    public void Extract_GivesEveryEntity_AnEditableNameField()
    {
        var result = new SemanticResult();
        result.Entities.Add(Ent("Book", EntityType.Object));

        var model = new SeedEntityExtractor().Extract(result);

        var field = Assert.Single(model.Entities[0].Fields);
        Assert.Equal("Name", field.Name);
        Assert.Equal("string", field.Type);
        Assert.Equal(Provenance.Seed, field.Provenance);
    }
}

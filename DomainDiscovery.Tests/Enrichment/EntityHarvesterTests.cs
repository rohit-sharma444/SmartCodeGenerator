using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class EntityHarvesterTests
{
    private static Entity Ent(string text, EntityType type)
        => new() { Text = text, Type = type, Confidence = 0.9 };

    [Fact]
    public void Harvest_RanksEntitiesByFrequency_Descending()
    {
        var r = new SemanticResult();
        r.Entities.Add(Ent("loan", EntityType.Object));
        r.Entities.Add(Ent("loan", EntityType.Object));
        r.Entities.Add(Ent("catalog", EntityType.Object));

        var model = new EntityHarvester().Harvest(r);

        Assert.Equal(new[] { "Loan", "Catalog" }, model.Entities.Select(e => e.Name));
        Assert.All(model.Entities, e => Assert.Equal(Provenance.Wiki, e.Provenance));
        Assert.True(model.Entities[0].Confidence >= model.Entities[1].Confidence);
    }

    [Fact]
    public void Harvest_AttachesAttributeRelationships_AsTypedFields()
    {
        var r = new SemanticResult();
        var loan = Ent("loan", EntityType.Object);
        var dueDate = Ent("due date", EntityType.Attribute);
        r.Entities.Add(loan);
        r.Entities.Add(dueDate);
        r.Relationships.Add(new Relationship { Source = loan, Target = dueDate });

        var model = new EntityHarvester().Harvest(r);

        var entity = Assert.Single(model.Entities);
        var field = Assert.Single(entity.Fields);
        Assert.Equal("Date", field.Name);        // NameNormalizer takes the head word
        Assert.Equal("DateTime", field.Type);
        Assert.Equal(Provenance.Wiki, field.Provenance);
    }
}

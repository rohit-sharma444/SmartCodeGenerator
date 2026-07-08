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
    public void Harvest_DropsFunctionWords_ThatThePosTaggerMistookForNouns()
    {
        var r = new SemanticResult();
        foreach (var noise in new[] { "as", "also", "all", "per", "other", "out", "the", "which" })
            r.Entities.Add(Ent(noise, EntityType.Object));
        r.Entities.Add(Ent("payroll", EntityType.Object));

        var model = new EntityHarvester().Harvest(r);

        Assert.Equal(new[] { "Payroll" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void Harvest_DropsTokenFragments_TooShortToBeAnEntity()
    {
        var r = new SemanticResult();
        // Tokenizing "18th century" / "Washington D.C." leaves "th" and "d" behind.
        foreach (var fragment in new[] { "th", "d", "s" })
            r.Entities.Add(Ent(fragment, EntityType.Object));
        r.Entities.Add(Ent("tax", EntityType.Object));   // three letters is a real entity

        var model = new EntityHarvester().Harvest(r);

        Assert.Equal(new[] { "Tax" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void Harvest_CountsSingularAndPluralMentions_AsOneEntity()
    {
        var r = new SemanticResult();
        r.Entities.Add(Ent("employee", EntityType.Object));
        r.Entities.Add(Ent("employees", EntityType.Object));
        r.Entities.Add(Ent("employees", EntityType.Object));
        r.Entities.Add(Ent("tax", EntityType.Object));
        r.Entities.Add(Ent("taxes", EntityType.Object));

        var model = new EntityHarvester().Harvest(r);

        // Employee (3 mentions) outranks Tax (2); neither appears twice.
        Assert.Equal(new[] { "Employee", "Tax" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void Harvest_CapsTheModel_KeepingTheMostFrequentEntities()
    {
        var r = new SemanticResult();
        // "Signal" is mentioned twice; 30 distinct one-off nouns compete with it.
        r.Entities.Add(Ent("signal", EntityType.Object));
        r.Entities.Add(Ent("signal", EntityType.Object));
        foreach (var i in Enumerable.Range(1, 30))
            r.Entities.Add(Ent($"noise{i}", EntityType.Object));

        var model = new EntityHarvester(maxEntities: 5).Harvest(r);

        Assert.Equal(5, model.Entities.Count);
        Assert.Equal("Signal", model.Entities[0].Name);
    }

    [Fact]
    public void Harvest_DefaultCap_KeepsTheReviewLoopUsable()
    {
        var r = new SemanticResult();
        foreach (var i in Enumerable.Range(1, 400))
            r.Entities.Add(Ent($"word{i}", EntityType.Object));

        var model = new EntityHarvester().Harvest(r);

        Assert.InRange(model.Entities.Count, 1, 25);
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

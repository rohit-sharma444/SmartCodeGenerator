using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using NaturalLanguageEngine.Core;
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

        var model = new EntityHarvester(minMentions: 1).Harvest(r);

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

        var model = new EntityHarvester(minMentions: 1).Harvest(r);

        Assert.Equal(new[] { "Payroll" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void Harvest_DropsNumberWords_ThatThePosTaggerMistookForNouns()
    {
        var r = new SemanticResult();
        // "One tablet ..." — frequent enough in prose to outrank real entities.
        foreach (var number in new[] { "one", "two", "three" })
        {
            r.Entities.Add(Ent(number, EntityType.Object));
            r.Entities.Add(Ent(number, EntityType.Object));
        }
        r.Entities.Add(Ent("ingredient", EntityType.Object));
        r.Entities.Add(Ent("ingredient", EntityType.Object));

        var model = new EntityHarvester().Harvest(r);

        Assert.Equal(new[] { "Ingredient" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void Harvest_IgnoresProperNouns_FoundInArticleProse()
    {
        var r = new SemanticResult();
        // EntityRecognizer falls back to Person for any capitalized head noun, so an
        // encyclopedia article's history section yields Athenaeus, Greek, Latin — never
        // domain classes. Organizations and locations are proper nouns for the same reason.
        foreach (var proper in new[] { "Athenaeus", "Greek" })
        {
            r.Entities.Add(Ent(proper, EntityType.Person));
            r.Entities.Add(Ent(proper, EntityType.Person));
            r.Entities.Add(Ent(proper, EntityType.Person));
        }
        r.Entities.Add(Ent("Acme Corp", EntityType.Organization));
        r.Entities.Add(Ent("Acme Corp", EntityType.Organization));
        r.Entities.Add(Ent("Babylonia", EntityType.Location));
        r.Entities.Add(Ent("Babylonia", EntityType.Location));

        r.Entities.Add(Ent("ingredient", EntityType.Object));
        r.Entities.Add(Ent("ingredient", EntityType.Object));

        var model = new EntityHarvester().Harvest(r);

        Assert.Equal(new[] { "Ingredient" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void Harvest_DropsNouns_MentionedOnlyOnce()
    {
        var r = new SemanticResult();
        // A whole article mentions its real subjects repeatedly. A noun that appears once is
        // passing prose ("Coquinaria", "Appetizer"), and would otherwise backfill the cap.
        r.Entities.Add(Ent("coquinaria", EntityType.Object));
        r.Entities.Add(Ent("appetizer", EntityType.Object));
        r.Entities.Add(Ent("ingredient", EntityType.Object));
        r.Entities.Add(Ent("ingredient", EntityType.Object));

        var model = new EntityHarvester().Harvest(r);

        Assert.Equal(new[] { "Ingredient" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void Harvest_DropsTokenFragments_TooShortToBeAnEntity()
    {
        var r = new SemanticResult();
        // Tokenizing "18th century" / "Washington D.C." leaves "th" and "d" behind.
        foreach (var fragment in new[] { "th", "d", "s" })
            r.Entities.Add(Ent(fragment, EntityType.Object));
        r.Entities.Add(Ent("tax", EntityType.Object));   // three letters is a real entity

        var model = new EntityHarvester(minMentions: 1).Harvest(r);

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

        var model = new EntityHarvester(maxEntities: 5, minMentions: 1).Harvest(r);

        Assert.Equal(5, model.Entities.Count);
        Assert.Equal("Signal", model.Entities[0].Name);
    }

    [Fact]
    public void Harvest_DefaultCap_KeepsTheReviewLoopUsable()
    {
        var r = new SemanticResult();
        foreach (var i in Enumerable.Range(1, 400))
        {
            r.Entities.Add(Ent($"word{i}", EntityType.Object));
            r.Entities.Add(Ent($"word{i}", EntityType.Object));
        }

        var model = new EntityHarvester().Harvest(r);

        Assert.InRange(model.Entities.Count, 1, 25);
    }

    /// <summary>
    /// Driven by the REAL NLE, not a hand-built SemanticResult — which is the point. Every other
    /// test here builds the analysis by hand and types "book" as an Object, so none of them could
    /// ever see that the live analyzer types it as an Action. A library lending system was
    /// generated with no Book class, and no test failed.
    /// </summary>
    [Fact]
    public void Harvest_FindsANounThatIsAlsoAVerb_WhenTheRealAnalyzerReadsRealProse()
    {
        var analyzer = EngineBuilder.BuildDefault().Analyzer;

        var model = new EntityHarvester().Harvest(analyzer.Analyze(
            "Public libraries lend books to their members. The books are returned to the library."));

        Assert.Contains(model.Entities, e => e.Name == "Book");
    }

    [Fact]
    public void Harvest_LeavesFieldsToTheFieldAssigner()
    {
        var r = new SemanticResult();
        r.Entities.Add(Ent("loan", EntityType.Object));
        r.Entities.Add(Ent("loan", EntityType.Object));

        var model = new EntityHarvester().Harvest(r);

        // Prose carries no field information; EntityShaper shapes the entity later.
        Assert.Empty(Assert.Single(model.Entities).Fields);
    }
}

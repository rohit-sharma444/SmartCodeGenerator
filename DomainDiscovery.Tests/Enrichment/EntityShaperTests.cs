using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Tests.Fakes;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class EntityShaperTests
{
    private readonly FakeWikipediaClient _wiki = new();

    private EntityShaper Shaper => new(_wiki);

    private static CandidateDomainModel ModelOf(params string[] names)
    {
        var model = new CandidateDomainModel();
        foreach (var n in names)
            model.Entities.Add(new CandidateEntity { Name = n, Provenance = Provenance.Wiki });
        return model;
    }

    [Fact]
    public void Shape_GivesAKnownEntity_ItsOwnFields()
    {
        var model = ModelOf("Recipe");

        Shaper.Shape(model);

        var recipe = Assert.Single(model.Entities);
        Assert.Equal(
            new[] { "Name", "Servings", "PrepMinutes", "Instructions" },
            recipe.Fields.Select(f => f.Name));
        Assert.Equal("int", recipe.Fields.Single(f => f.Name == "Servings").Type);
    }

    [Fact]
    public void Shape_KeepsFieldsAlreadyOnTheEntity_AndDoesNotDuplicateThem()
    {
        var model = ModelOf("Recipe");
        model.Entities[0].Fields.Add(new CandidateField
        {
            Name = "Name", Type = "string", Provenance = Provenance.Seed
        });

        Shaper.Shape(model);

        var recipe = Assert.Single(model.Entities);
        Assert.Single(recipe.Fields, f => f.Name == "Name");
        Assert.Equal(Provenance.Seed, recipe.Fields.Single(f => f.Name == "Name").Provenance);
    }

    [Fact]
    public void Shape_MovesAnUnshapeableSeedEntity_ToSuggestions()
    {
        // SeedEntityExtractor turns every noun in the user's typed phrase into a
        // Provenance.Seed entity with no fields — an extracted seed is a guess like any
        // other, so it takes the same lead-fetch / hypernym / archetype gate as everything
        // else. Nothing shapes "Widget" here (no lexicon entry, no fetchable lead), so it is
        // suggested rather than kept as junk or silently deleted.
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Widget", Provenance = Provenance.Seed });

        Shaper.Shape(model);

        Assert.Empty(model.Entities);
        Assert.Equal(new[] { "Widget" }, model.Suggestions.Select(e => e.Name));
        Assert.Empty(model.Rejected);
    }

    [Fact]
    public void Shape_KeepsAnEntity_ThatAlreadyHasFields()
    {
        // ModelEditor.AddEntity gives a hand-added entity a field (its own lexicon shape, or
        // the fallback Name) before EntityShaper ever sees it. That existing field — not its
        // provenance — is what exempts it from the shape gate, so it is kept even though
        // nothing here could shape it from scratch.
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Widget", Provenance = Provenance.Seed };
        entity.Fields.Add(new CandidateField
        {
            Name = DefaultFields.NameField, Type = "string", Provenance = Provenance.Seed
        });
        model.Entities.Add(entity);

        Shaper.Shape(model);

        var widget = Assert.Single(model.Entities);
        Assert.Equal(DefaultFields.NameField, Assert.Single(widget.Fields).Name);
        Assert.Empty(model.Suggestions);
    }

    [Fact]
    public void Shape_TheCuratedLexicon_BeatsTheArchetype()
    {
        // Customer is curated; its definition would otherwise shape it as a "recipient".
        _wiki.Leads["Customer"] = "A customer is the recipient of a good.";
        var model = ModelOf("Customer");

        Shaper.Shape(model);

        var customer = Assert.Single(model.Entities);
        Assert.Equal(
            new[] { "FullName", "Email", "Phone" },
            customer.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Shape_ShapesAnUncuratedEntity_FromItsOwnDefinition()
    {
        _wiki.Leads["Cash register"] = "A cash register is a device for registering sales.";
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "CashRegister", SourceTitle = "Cash register", Provenance = Provenance.Wiki
        });

        Shaper.Shape(model);

        var register = Assert.Single(model.Entities);
        Assert.Equal(
            new[] { "Name", "Model", "SerialNumber" },
            register.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Shape_RejectsAnAbstraction_AndSuggestsWhatItCannotShape()
    {
        // Replaces DomainFieldAssignerTests.Apply_DropsEntities_WhoseShapeIsUnknown.
        // System is prose (an abstraction) and is rejected; Kiosk is simply unknown, so it
        // is offered rather than deleted.
        _wiki.Leads["System"] = "A system is a group of interacting elements.";
        _wiki.Leads["Kiosk"] = "A kiosk is a small booth.";
        var model = ModelOf("Recipe", "System", "Kiosk");

        Shaper.Shape(model);

        Assert.Equal(new[] { "Recipe" }, model.Entities.Select(e => e.Name));
        Assert.Equal(new[] { "Kiosk" }, model.Suggestions.Select(e => e.Name));
        Assert.Equal(new[] { "System" }, model.Rejected);
    }

    [Fact]
    public void Shape_WhenTheLeadCannotBeFetched_SuggestsRatherThanDeletes()
    {
        // Offline, or a 429: we know nothing, so we must not pretend to know it is not a class.
        var model = ModelOf("Barcode");

        Shaper.Shape(model);

        Assert.Empty(model.Entities);
        Assert.Equal(new[] { "Barcode" }, model.Suggestions.Select(e => e.Name));
        Assert.Empty(model.Rejected);
    }

    [Fact]
    public void Shape_SkipsAFieldNamedAfterItsOwnEntity_ToAvoidCS0542()
    {
        // ModelEditor.AddField already refuses a field whose name equals its entity's name,
        // because `class Model { public string Model { get; set; } }` is CS0542 and would not
        // compile. EntityShaper's archetype path is name-agnostic, so an entity that happens to
        // be named after one of its own archetype's fields ("Model", from the device archetype)
        // must have that one field skipped while keeping the rest.
        _wiki.Leads["Model"] = "A model is a device for demonstrating a concept.";
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Model", Provenance = Provenance.Wiki });

        Shaper.Shape(model);

        var entity = Assert.Single(model.Entities);
        Assert.Equal(new[] { "Name", "SerialNumber" }, entity.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Shape_FetchesAllLeadsInOneBatchCall_NotOnePerEntity()
    {
        // The whole point of Task 12: a real run needed a lead per uncurated candidate, and
        // fetching those one at a time is what drove Wikipedia to 429 after ~15 requests.
        // EntityShaper must collect every title up front and make a single FetchLeads call.
        _wiki.Leads["Cash register"] = "A cash register is a device for registering sales.";
        _wiki.Leads["Kiosk"] = "A kiosk is a small booth.";
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "CashRegister", SourceTitle = "Cash register", Provenance = Provenance.Wiki
        });
        model.Entities.Add(new CandidateEntity { Name = "Kiosk", Provenance = Provenance.Wiki });

        Shaper.Shape(model);

        Assert.Single(_wiki.FetchLeadsCalls);
        Assert.Equal(
            new[] { "Cash register", "Kiosk" },
            _wiki.FetchLeadsCalls[0].OrderBy(t => t));
        Assert.Empty(_wiki.FetchLeadCalls);
    }
}

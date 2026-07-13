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

        // The curated fields come FIRST and in order. RoleTemplate appends its baseline after
        // them (Shape's third pass), so this is a prefix rather than the whole list — but the
        // precedence this test exists to pin down is still exactly asserted.
        var curated = new[] { "Name", "Servings", "PrepMinutes", "Instructions" };
        Assert.Equal(curated, recipe.Fields.Select(f => f.Name).Take(curated.Length));
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

        // Curated first, in order; the Party role template appends its baseline behind them.
        var curated = new[] { "FullName", "Email", "Phone" };
        Assert.Equal(curated, customer.Fields.Select(f => f.Name).Take(curated.Length));
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

        // Archetype fields first, in order; the Device role template appends the rest.
        var archetype = new[] { "Name", "Model", "SerialNumber" };
        Assert.Equal(archetype, register.Fields.Select(f => f.Name).Take(archetype.Length));
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
        var names = entity.Fields.Select(f => f.Name).ToList();

        // The guard now has to hold on BOTH paths: the device archetype offers a "Model" field
        // and so does the Device role template, and neither may land on a class called Model.
        Assert.DoesNotContain("Model", names);
        Assert.Equal(new[] { "Name", "SerialNumber" }, names.Take(2));
    }

    [Fact]
    public void Shape_ACuratedEntity_TakesTheLexiconsRole()
    {
        var model = ModelOf("Customer");

        Shaper.Shape(model);

        var customer = Assert.Single(model.Entities);
        Assert.Equal(EntityRole.Party, customer.Role);
    }

    [Fact]
    public void Shape_AnArchetypeShapedEntity_TakesTheArchetypesRole()
    {
        _wiki.Leads["Cash register"] = "A cash register is a device for registering sales.";
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "CashRegister", SourceTitle = "Cash register", Provenance = Provenance.Wiki
        });

        Shaper.Shape(model);

        var register = Assert.Single(model.Entities);
        Assert.Equal(EntityRole.Device, register.Role);
    }

    [Fact]
    public void Shape_ASuggestedEntity_KeepsRoleNone()
    {
        // Barcode has no lexicon entry and no fetchable lead, so it becomes a suggestion —
        // neither shape source ever ran, so it must keep the default, unassigned role.
        var model = ModelOf("Barcode");

        Shaper.Shape(model);

        var suggestion = Assert.Single(model.Suggestions);
        Assert.Equal(EntityRole.None, suggestion.Role);
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

    [Fact]
    public void Adds_the_role_template_fields_to_a_curated_entity()
    {
        var client = new FakeWikipediaClient();
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Invoice", Provenance = Provenance.Wiki });

        new EntityShaper(client).Shape(model);

        var invoice = model.Entities.Single(e => e.Name == "Invoice");

        // Curated (Invoice is a Document in DomainFieldLexicon) ...
        Assert.Contains(invoice.Fields, f => f.Name == "Number");
        // ... plus the Document role template, which curation alone never gave it.
        Assert.Contains(invoice.Fields, f => f.Name == "Status");
    }

    [Fact]
    public void Mines_properties_from_the_entitys_own_article()
    {
        var client = new FakeWikipediaClient();
        client.Extracts["Invoice"] = """
            An invoice is a commercial document.
            An invoice typically contains:
            * shipping terms
            * delivery date
            """;

        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Invoice", Provenance = Provenance.Wiki });

        new EntityShaper(client).Shape(model);

        var names = model.Entities.Single(e => e.Name == "Invoice").Fields.Select(f => f.Name).ToList();

        Assert.Contains("ShippingTerms", names);
        Assert.Contains("DeliveryDate", names);
    }

    [Fact]
    public void An_entity_whose_article_says_nothing_keeps_the_shape_it_already_had()
    {
        var client = new FakeWikipediaClient();
        client.Extracts["Invoice"] = "An invoice is a commercial document. Nothing more.";

        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Invoice", Provenance = Provenance.Wiki });

        new EntityShaper(client).Shape(model);

        // The no-op contract: still curated + templated, just not mined.
        Assert.Contains(model.Entities.Single(e => e.Name == "Invoice").Fields, f => f.Name == "Number");
    }

    [Fact]
    public void A_rejected_entity_is_never_mined()
    {
        var client = new FakeWikipediaClient();
        client.Leads["System"] = "A system is a group of interacting elements.";

        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "System", Provenance = Provenance.Wiki });

        new EntityShaper(client).Shape(model);

        Assert.Contains("System", model.Rejected);
        Assert.Empty(model.Entities);
    }
}

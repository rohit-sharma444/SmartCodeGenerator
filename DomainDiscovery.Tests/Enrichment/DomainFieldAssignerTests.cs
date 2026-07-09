using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class DomainFieldAssignerTests
{
    private static CandidateDomainModel ModelOf(params string[] names)
    {
        var model = new CandidateDomainModel();
        foreach (var n in names)
            model.Entities.Add(new CandidateEntity { Name = n, Provenance = Provenance.Wiki });
        return model;
    }

    [Fact]
    public void Apply_GivesAKnownEntity_ItsOwnFields()
    {
        var model = ModelOf("Recipe");

        DomainFieldAssigner.Apply(model);

        var recipe = Assert.Single(model.Entities);
        Assert.Equal(
            new[] { "Name", "Servings", "PrepMinutes", "Instructions" },
            recipe.Fields.Select(f => f.Name));
        Assert.Equal("int", recipe.Fields.Single(f => f.Name == "Servings").Type);
        Assert.All(recipe.Fields, f => Assert.Equal(Provenance.Wiki, f.Provenance));
    }

    [Fact]
    public void Apply_ShapesDifferentEntities_Differently()
    {
        var model = ModelOf("Ingredient", "Cookbook");

        DomainFieldAssigner.Apply(model);

        Assert.Equal(new[] { "Name", "Quantity", "Unit" }, model.Entities[0].Fields.Select(f => f.Name));
        Assert.Equal(new[] { "Title", "Author", "Published" }, model.Entities[1].Fields.Select(f => f.Name));
    }

    [Fact]
    public void Apply_DropsEntities_WhoseShapeIsUnknown()
    {
        // Harvested from the Recipe article's history section; prose, not domain classes.
        var model = ModelOf("Recipe", "Century", "Tablet", "Ingredient", "Management");

        DomainFieldAssigner.Apply(model);

        Assert.Equal(new[] { "Recipe", "Ingredient" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void Apply_MatchesTheEntityName_CaseInsensitively()
    {
        var model = ModelOf("recipe");

        DomainFieldAssigner.Apply(model);

        Assert.Single(model.Entities);
    }

    [Fact]
    public void Apply_KeepsFieldsAlreadyOnTheEntity_AndDoesNotDuplicateThem()
    {
        var model = ModelOf("Recipe");
        model.Entities[0].Fields.Add(new CandidateField
        {
            Name = "Name", Type = "string", Provenance = Provenance.Seed
        });
        model.Entities[0].Fields.Add(new CandidateField
        {
            Name = "Difficulty", Type = "string", Provenance = Provenance.Seed
        });

        DomainFieldAssigner.Apply(model);

        var recipe = Assert.Single(model.Entities);
        Assert.Single(recipe.Fields, f => f.Name == "Name");
        Assert.Contains(recipe.Fields, f => f.Name == "Difficulty");
        Assert.Equal(Provenance.Seed, recipe.Fields.Single(f => f.Name == "Name").Provenance);
    }

    [Fact]
    public void Apply_KeepsAnUnknownEntity_ThatTheUserGaveFieldsTo()
    {
        var model = ModelOf("Widget");
        model.Entities[0].Fields.Add(new CandidateField
        {
            Name = "Name", Type = "string", Provenance = Provenance.Seed
        });

        DomainFieldAssigner.Apply(model);

        Assert.Single(model.Entities);
    }
}

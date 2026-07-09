using DomainDiscovery.Model;
using DomainDiscovery.Review;
using Xunit;

namespace DomainDiscovery.Tests.Review;

public class ModelEditorTests
{
    private static CandidateDomainModel ModelWith(params string[] names)
    {
        var m = new CandidateDomainModel();
        foreach (var n in names)
            m.Entities.Add(new CandidateEntity { Name = n });
        return m;
    }

    [Fact]
    public void AddEntity_AddsNormalizedName_Once()
    {
        var model = ModelWith("Book");

        Assert.True(ModelEditor.AddEntity(model, "loan"));
        Assert.False(ModelEditor.AddEntity(model, "Loan")); // duplicate

        Assert.Equal(new[] { "Book", "Loan" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void RemoveEntity_RemovesMatch_CaseInsensitive()
    {
        var model = ModelWith("Book", "Loan");

        Assert.True(ModelEditor.RemoveEntity(model, "book"));
        Assert.False(ModelEditor.RemoveEntity(model, "Missing"));

        Assert.Equal(new[] { "Loan" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void RenameEntity_RenamesExisting_RejectsCollision()
    {
        var model = ModelWith("Book", "Loan");

        Assert.True(ModelEditor.RenameEntity(model, "Book", "Publication"));
        Assert.False(ModelEditor.RenameEntity(model, "Publication", "Loan")); // collides

        Assert.Equal(new[] { "Publication", "Loan" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void AddEntity_GivesTheNewEntity_AnEditableNameField()
    {
        var model = new CandidateDomainModel();

        Assert.True(ModelEditor.AddEntity(model, "loan"));

        var field = Assert.Single(model.Entities[0].Fields);
        Assert.Equal("Name", field.Name);
        Assert.Equal("string", field.Type);
    }

    [Fact]
    public void AddField_InfersTheType_FromTheFieldName()
    {
        var model = ModelWith("Recipe");

        Assert.True(ModelEditor.AddField(model, "Recipe", "prep time"));

        var field = Assert.Single(model.Entities[0].Fields);
        Assert.Equal("PrepTime", field.Name);
        Assert.Equal("DateTime", field.Type);
        Assert.Equal(Provenance.Seed, field.Provenance);
    }

    [Fact]
    public void AddField_AcceptsAnExplicitType_WhenInferenceWouldGuessWrong()
    {
        var model = ModelWith("Recipe");

        Assert.True(ModelEditor.AddField(model, "Recipe", "servings", "int"));

        var field = Assert.Single(model.Entities[0].Fields);
        Assert.Equal("Servings", field.Name);
        Assert.Equal("int", field.Type);
    }

    [Fact]
    public void AddField_RejectsAnUnsupportedType()
    {
        var model = ModelWith("Recipe");

        Assert.False(ModelEditor.AddField(model, "Recipe", "servings", "Widget"));
        Assert.Empty(model.Entities[0].Fields);
    }

    [Fact]
    public void AddField_RejectsUnknownEntity_DuplicateField_AndBlankName()
    {
        var model = ModelWith("Recipe");

        Assert.False(ModelEditor.AddField(model, "Nope", "name"));
        Assert.False(ModelEditor.AddField(model, "Recipe", "!!!"));

        Assert.True(ModelEditor.AddField(model, "Recipe", "title"));
        Assert.False(ModelEditor.AddField(model, "Recipe", "TITLE"));   // case-insensitive duplicate

        Assert.Single(model.Entities[0].Fields);
    }

    [Fact]
    public void RemoveField_RemovesMatch_CaseInsensitive()
    {
        var model = ModelWith("Recipe");
        Assert.True(ModelEditor.AddField(model, "Recipe", "title"));

        Assert.True(ModelEditor.RemoveField(model, "recipe", "TITLE"));
        Assert.False(ModelEditor.RemoveField(model, "Recipe", "title"));  // already gone

        Assert.Empty(model.Entities[0].Fields);
    }

    [Fact]
    public void RemoveField_RefusesToRemoveName()
    {
        var model = new CandidateDomainModel();
        Assert.True(ModelEditor.AddEntity(model, "Recipe"));

        // Without Name an entity scaffolds a grid whose only column is a read-only Id,
        // leaving the user nothing to type into.
        Assert.False(ModelEditor.RemoveField(model, "Recipe", "Name"));
        Assert.False(ModelEditor.RemoveField(model, "Recipe", "name"));

        var field = Assert.Single(model.Entities[0].Fields);
        Assert.Equal("Name", field.Name);
    }
}

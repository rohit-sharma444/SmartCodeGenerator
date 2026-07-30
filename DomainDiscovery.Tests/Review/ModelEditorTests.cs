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
    public void AddEntity_ShapesAKnownEntity_FromTheLexicon()
    {
        var model = new CandidateDomainModel();

        Assert.True(ModelEditor.AddEntity(model, "loan"));

        Assert.Equal(
            new[] { "BorrowedOn", "DueDate", "Returned" },
            model.Entities[0].Fields.Select(f => f.Name));
    }

    [Fact]
    public void AddEntity_GivesAnUnknownEntity_TheFallbackNameField()
    {
        var model = new CandidateDomainModel();

        // The lexicon has nothing to say about Widget, but the user asked for it by name.
        Assert.True(ModelEditor.AddEntity(model, "widget"));

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
        Assert.True(ModelEditor.AddField(model, "Recipe", "difficulty"));

        Assert.True(ModelEditor.RemoveField(model, "recipe", "TITLE"));
        Assert.False(ModelEditor.RemoveField(model, "Recipe", "title"));  // already gone

        Assert.Equal(new[] { "Difficulty" }, model.Entities[0].Fields.Select(f => f.Name));
    }

    [Fact]
    public void AddField_RejectsId_WhichIsGeneratedAndAssignedByReflection()
    {
        var model = ModelWith("Recipe");

        // DomainModelMapper only prepends `int Id` when no Id field exists, so a string Id
        // compiles — and then JsonStore.NewItem throws ArgumentException assigning an int
        // to it the first time the user clicks Add.
        Assert.False(ModelEditor.AddField(model, "Recipe", "Id"));
        Assert.False(ModelEditor.AddField(model, "Recipe", "id", "string"));

        Assert.Empty(model.Entities[0].Fields);
    }

    [Fact]
    public void AddField_RejectsAFieldNamedAfterItsOwnEntity()
    {
        var model = ModelWith("Recipe");

        // `class Recipe { public string Recipe { get; set; } }` is CS0542.
        Assert.False(ModelEditor.AddField(model, "Recipe", "recipe"));

        Assert.Empty(model.Entities[0].Fields);
    }

    [Theory]
    [InlineData("2nd course")]
    [InlineData("123")]
    public void AddField_RejectsNamesCSharpCannotDeclare(string fieldName)
    {
        var model = ModelWith("Recipe");

        // "2nd course" normalizes to "2ndCourse", which emits CS1519.
        Assert.False(ModelEditor.AddField(model, "Recipe", fieldName));

        Assert.Empty(model.Entities[0].Fields);
    }

    [Fact]
    public void AddEntity_RejectsNamesCSharpCannotDeclare()
    {
        var model = new CandidateDomainModel();

        Assert.False(ModelEditor.AddEntity(model, "2ndthing"));

        Assert.Empty(model.Entities);
    }

    [Fact]
    public void RemoveField_RefusesToRemoveName()
    {
        var model = new CandidateDomainModel();
        Assert.True(ModelEditor.AddEntity(model, "widget"));

        // Without Name an entity scaffolds a grid whose only column is a read-only Id,
        // leaving the user nothing to type into.
        Assert.False(ModelEditor.RemoveField(model, "widget", "Name"));
        Assert.False(ModelEditor.RemoveField(model, "widget", "name"));

        var field = Assert.Single(model.Entities[0].Fields);
        Assert.Equal("Name", field.Name);
    }

    [Fact]
    public void RemoveField_RefusesToRemoveTheEntitysLastField()
    {
        var model = new CandidateDomainModel();
        Assert.True(ModelEditor.AddEntity(model, "author"));   // lexicon shape: FullName only

        // Not called Name, but stripping it leaves the same uneditable grid.
        Assert.False(ModelEditor.RemoveField(model, "Author", "FullName"));
        Assert.Single(model.Entities[0].Fields);
    }

    [Fact]
    public void AddEntity_PromotesASuggestion_KeepingItsProvenanceAndConfidence()
    {
        var model = new CandidateDomainModel();
        model.Suggestions.Add(new CandidateEntity
        {
            Name = "Barcode", Provenance = Provenance.Wiki, Confidence = 0.42
        });

        Assert.True(ModelEditor.AddEntity(model, "barcode"));

        var barcode = Assert.Single(model.Entities);
        Assert.Equal("Barcode", barcode.Name);
        Assert.Equal(Provenance.Wiki, barcode.Provenance);
        Assert.Equal(0.42, barcode.Confidence);
        Assert.Equal(DefaultFields.NameField, Assert.Single(barcode.Fields).Name);
        Assert.Empty(model.Suggestions);
    }

    [Fact]
    public void AddEntity_KeepsACompoundNameTheUserTyped()
    {
        var model = new CandidateDomainModel();

        Assert.True(ModelEditor.AddEntity(model, "CashRegister"));

        // Not "Cashregister": the user typed the class name they wanted.
        Assert.Equal("CashRegister", Assert.Single(model.Entities).Name);
    }

    [Fact]
    public void AddEntity_BuildsACompoundFromAMultiWordName()
    {
        var model = new CandidateDomainModel();

        Assert.True(ModelEditor.AddEntity(model, "cash register"));

        Assert.Equal("CashRegister", Assert.Single(model.Entities).Name);
    }

    [Fact]
    public void AddEntity_StillSingularizesWhatTheUserTyped()
    {
        var model = new CandidateDomainModel();

        Assert.True(ModelEditor.AddEntity(model, "Books"));

        Assert.Equal("Book", Assert.Single(model.Entities).Name);
    }

    [Fact]
    public void AddEntity_ReportsTheNameItCreated()
    {
        var model = new CandidateDomainModel();

        Assert.True(ModelEditor.AddEntity(model, "cash register", out var added));

        Assert.Equal("CashRegister", added);
    }

    [Fact]
    public void AddEntity_ReportsTheSuggestionsOwnName_WhenItPromotesOne()
    {
        // Promotion keeps the suggestion object, so the created name is NOT what the normalizer
        // returned for the typed text. Reporting the normalizer's answer would misname it.
        var model = new CandidateDomainModel();
        model.Suggestions.Add(new CandidateEntity
        {
            Name = "LibraryMakerspace", Provenance = Provenance.Wiki
        });

        Assert.True(ModelEditor.AddEntity(model, "librarymakerspace", out var added));

        Assert.Equal("LibraryMakerspace", added);
    }

    [Fact]
    public void AddEntity_ReportsNoName_WhenItAddsNothing()
    {
        var model = ModelWith("Book");

        Assert.False(ModelEditor.AddEntity(model, "book", out var added));

        Assert.Null(added);
    }

    [Fact]
    public void RenameEntity_RecordsBothDirections()
    {
        // Without this, the decision log still files the whole discovery trail under the OLD
        // name, so `why <new name>` finds nothing and wrongly reports a discovered entity as
        // hand-added. Recording both ways lets the user follow the rename from either side.
        var model = ModelWith("Member");

        Assert.True(ModelEditor.RenameEntity(model, "Member", "Borrower"));

        var underNew = Assert.Single(model.Decisions, d => d.Name == "Borrower");
        Assert.Equal(DecisionStage.ReviewLoop, underNew.Stage);
        Assert.Equal(DecisionReasons.YouRenamedIt, underNew.Reason);
        Assert.Contains("Member", underNew.Evidence);

        var underOld = Assert.Single(model.Decisions, d => d.Name == "Member");
        Assert.Equal(DecisionReasons.YouRenamedIt, underOld.Reason);
        Assert.Contains("Borrower", underOld.Evidence);
    }

    [Fact]
    public void RenameEntity_RecordsNothing_WhenItChangesNothing()
    {
        var model = ModelWith("Member", "Borrower");

        Assert.False(ModelEditor.RenameEntity(model, "Member", "Borrower")); // name in use

        Assert.Empty(model.Decisions);
    }

    [Fact]
    public void RenameEntity_KeepsACompoundNameTheUserTyped()
    {
        var model = ModelWith("Member");

        Assert.True(ModelEditor.RenameEntity(model, "Member", "CashRegister"));

        Assert.Equal("CashRegister", Assert.Single(model.Entities).Name);
    }

    [Fact]
    public void TheOtherVerbsStillResolveAnEntityWithInteriorCapitals()
    {
        // Lookups normalize with ToEntityName, which flattens "CashRegister" to "Cashregister".
        // They keep working only because Find compares case-insensitively — if that ever changes,
        // preserving the typed casing would strand the entity behind every other verb.
        var model = new CandidateDomainModel();
        Assert.True(ModelEditor.AddEntity(model, "CashRegister"));

        Assert.True(ModelEditor.AddField(model, "CashRegister", "Price", "decimal"));
        Assert.True(ModelEditor.RemoveField(model, "CashRegister", "Price"));
        Assert.True(ModelEditor.RemoveEntity(model, "CashRegister"));

        Assert.Empty(model.Entities);
    }
}

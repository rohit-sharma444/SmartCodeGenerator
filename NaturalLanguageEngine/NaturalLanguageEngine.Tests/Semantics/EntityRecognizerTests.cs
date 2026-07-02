using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Models.Syntax;
using NaturalLanguageEngine.Semantics;
using NaturalLanguageEngine.Tests.TestSupport;
using Xunit;

namespace NaturalLanguageEngine.Tests.Semantics;

public class EntityRecognizerTests
{
    private readonly EntityRecognizer _recognizer =
        new(EngineTestFactory.CreateEnglishRepository());

    private static Phrase SinglePhrase(
        string text,
        PhraseType phraseType,
        TokenType tokenType = TokenType.Word)
    {
        var analysis = new TokenAnalysis
        {
            Token = new Token { Text = text, Type = tokenType }
        };

        var phrase = new Phrase { Type = phraseType, Head = analysis };
        phrase.Tokens.Add(analysis);
        return phrase;
    }

    [Fact]
    public void Recognize_KnownCity_IsClassifiedAsLocation()
    {
        var entities = _recognizer.Recognize(new[] { SinglePhrase("London", PhraseType.NounPhrase) });

        var entity = Assert.Single(entities);
        Assert.Equal(EntityType.Location, entity.Type);
    }

    [Fact]
    public void Recognize_KnownOrganizationName_IsClassifiedAsOrganization()
    {
        var entities = _recognizer.Recognize(new[] { SinglePhrase("Google", PhraseType.NounPhrase) });

        var entity = Assert.Single(entities);
        Assert.Equal(EntityType.Organization, entity.Type);
    }

    [Fact]
    public void Recognize_OrganizationSuffixWord_IsClassifiedAsOrganization()
    {
        // "Corp" alone is a name-final marker, not a known org name itself.
        var entities = _recognizer.Recognize(new[] { SinglePhrase("Corp", PhraseType.NounPhrase) });

        var entity = Assert.Single(entities);
        Assert.Equal(EntityType.Organization, entity.Type);
    }

    [Fact]
    public void Recognize_UnrecognizedCapitalizedNoun_FallsBackToPerson()
    {
        var entities = _recognizer.Recognize(new[] { SinglePhrase("Sarah", PhraseType.NounPhrase) });

        var entity = Assert.Single(entities);
        Assert.Equal(EntityType.Person, entity.Type);
    }

    [Fact]
    public void Recognize_LowercaseNoun_IsClassifiedAsObject()
    {
        var entities = _recognizer.Recognize(new[] { SinglePhrase("car", PhraseType.NounPhrase) });

        var entity = Assert.Single(entities);
        Assert.Equal(EntityType.Object, entity.Type);
    }

    [Fact]
    public void Recognize_VerbPhrase_IsClassifiedAsAction()
    {
        var entities = _recognizer.Recognize(new[] { SinglePhrase("booked", PhraseType.VerbPhrase) });

        var entity = Assert.Single(entities);
        Assert.Equal(EntityType.Action, entity.Type);
    }

    [Theory]
    [InlineData(TokenType.Date, EntityType.Date)]
    [InlineData(TokenType.Time, EntityType.Time)]
    [InlineData(TokenType.Number, EntityType.Quantity)]
    [InlineData(TokenType.Url, EntityType.Value)]
    [InlineData(TokenType.Email, EntityType.Value)]
    public void Recognize_TokenTypeDrivenEntities_TakePriorityOverPhraseHeuristics(
        TokenType tokenType, EntityType expected)
    {
        var entities = _recognizer.Recognize(
            new[] { SinglePhrase("2025-06-30", PhraseType.NounPhrase, tokenType) });

        var entity = Assert.Single(entities);
        Assert.Equal(expected, entity.Type);
    }

    [Fact]
    public void Recognize_WithoutLanguageRepository_FallsBackToPersonForCapitalizedWords()
    {
        // Backward-compatible parameterless constructor: no gazetteer, so
        // even a known city falls back to the old Person heuristic.
        var recognizerWithoutGazetteer = new EntityRecognizer();

        var entities = recognizerWithoutGazetteer.Recognize(
            new[] { SinglePhrase("London", PhraseType.NounPhrase) });

        var entity = Assert.Single(entities);
        Assert.Equal(EntityType.Person, entity.Type);
    }

    [Fact]
    public void Recognize_EmptyPhraseList_ReturnsNoEntities()
    {
        Assert.Empty(_recognizer.Recognize(Array.Empty<Phrase>()));
    }

    [Fact]
    public void Recognize_SetsPhraseHeadEntity_SoDependencyEdgesCanBeMappedBackToEntities()
    {
        // RelationshipExtractor's dependency-driven path resolves a
        // Dependency's Governor/Dependent tokens back to entities via
        // TokenAnalysis.Entity; this is what wires that up.
        var phrase = SinglePhrase("London", PhraseType.NounPhrase);

        var entities = _recognizer.Recognize(new[] { phrase });

        var entity = Assert.Single(entities);
        Assert.Same(entity, phrase.Head!.Entity);
    }
}

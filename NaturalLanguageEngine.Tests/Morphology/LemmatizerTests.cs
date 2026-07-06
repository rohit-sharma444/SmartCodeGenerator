using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Morphology;
using NaturalLanguageEngine.Tests.TestSupport;
using Xunit;

namespace NaturalLanguageEngine.Tests.Morphology;

public class LemmatizerTests
{
    private readonly Lemmatizer _lemmatizer =
        new(EngineTestFactory.CreateEnglishRepository(), new Stemmer());

    private static Token Word(string text) => new() { Text = text, Type = TokenType.Word };

    // Regression coverage for a real bug found while writing these tests:
    // the bundled lexicon lists many inflected verb forms (e.g. "went",
    // "is") as their own dictionary entries with Lemma == Word, which used
    // to make GetLemma return the surface form itself instead of consulting
    // the irregular-form table. See Lemmatizer.GetLemma for the fix.
    [Theory]
    [InlineData("went", "go")]
    [InlineData("goes", "go")]
    [InlineData("going", "go")]
    [InlineData("is", "be")]
    [InlineData("was", "be")]
    [InlineData("were", "be")]
    [InlineData("took", "take")]
    [InlineData("running", "run")]
    [InlineData("children", "child")]
    [InlineData("mice", "mouse")]
    public void GetLemma_ResolvesIrregularForms(string surface, string expectedRoot)
    {
        var lemma = _lemmatizer.GetLemma(Word(surface));

        Assert.Equal(expectedRoot, lemma.Root);
        Assert.Equal(surface, lemma.Original);
    }

    [Theory]
    [InlineData("wanted", "want")]
    [InlineData("books", "book")]
    [InlineData("opened", "open")]
    [InlineData("boxes", "box")]
    public void GetLemma_FallsBackToStemmerForRegularInflections(string surface, string expectedRoot)
    {
        Assert.Equal(expectedRoot, _lemmatizer.GetLemma(Word(surface)).Root);
    }

    [Fact]
    public void GetLemma_EmptyToken_ReturnsEmptyRootAndOriginal()
    {
        var lemma = _lemmatizer.GetLemma(Word(string.Empty));

        Assert.Equal(string.Empty, lemma.Root);
        Assert.Equal(string.Empty, lemma.Original);
    }

    [Fact]
    public void GetLemma_IsCaseInsensitive()
    {
        Assert.Equal("go", _lemmatizer.GetLemma(Word("Went")).Root);
    }
}

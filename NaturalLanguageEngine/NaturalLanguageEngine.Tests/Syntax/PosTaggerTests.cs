using NaturalLanguageEngine.Abstractions.Syntax;
using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Syntax;
using NaturalLanguageEngine.Syntax;
using NaturalLanguageEngine.Tests.TestSupport;
using Xunit;

namespace NaturalLanguageEngine.Tests.Syntax;

public class PosTaggerTests
{
    private readonly PosTagger _tagger = EngineTestFactory.CreatePosTagger();

    private static TokenAnalysis Analysis(string text, TokenType type = TokenType.Word)
        => new() { Token = new Token { Text = text, Type = type } };

    [Theory]
    [InlineData("the", PartOfSpeech.Determiner)]
    [InlineData("book", PartOfSpeech.Verb)] // only listed as a verb in the bundled lexicon
    [InlineData("quickly", PartOfSpeech.Adverb)]
    [InlineData("is", PartOfSpeech.Verb)]
    public void Tag_DictionaryWord_UsesLexiconEntry(string word, PartOfSpeech expected)
    {
        var tokens = new List<TokenAnalysis> { Analysis(word) };

        _tagger.Tag(tokens);

        Assert.Equal(expected, tokens[0].PartOfSpeech);
    }

    [Fact]
    public void Tag_UnknownCapitalizedWord_InfersNounHeuristically()
    {
        var tokens = new List<TokenAnalysis> { Analysis("Zephyria") };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Noun, tokens[0].PartOfSpeech);
    }

    [Theory]
    [InlineData("sparkling")]
    [InlineData("wobbled")]
    public void Tag_UnknownWordWithVerbSuffix_InfersVerbHeuristically(string word)
    {
        var tokens = new List<TokenAnalysis> { Analysis(word) };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Verb, tokens[0].PartOfSpeech);
    }

    [Fact]
    public void Tag_NumericToken_InfersNumeral()
    {
        var tokens = new List<TokenAnalysis> { Analysis("1234", TokenType.Number) };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Numeral, tokens[0].PartOfSpeech);
    }

    [Fact]
    public void Tag_UnknownLowercaseWord_FallsBackToUnknown()
    {
        var tokens = new List<TokenAnalysis> { Analysis("xylophonic") };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Unknown, tokens[0].PartOfSpeech);
    }

    [Fact]
    public void Tag_ViaInterfaceOverload_DoesNotThrowAndMatchesPerTokenTagging()
    {
        // Regression test: this overload used to throw NotImplementedException.
        IPosTagger tagger = _tagger;
        var tokens = new List<Token>
        {
            new() { Text = "the", Type = TokenType.Word },
            new() { Text = "car", Type = TokenType.Word },
        };

        var result = tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Determiner, result[tokens[0]]);
    }
}

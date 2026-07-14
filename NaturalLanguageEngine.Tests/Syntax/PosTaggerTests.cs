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
    [InlineData("book", PartOfSpeech.Verb)] // the command language's "book a meeting"
    [InlineData("quickly", PartOfSpeech.Adverb)]
    [InlineData("is", PartOfSpeech.Verb)]
    public void Tag_DictionaryWord_UsesLexiconEntry(string word, PartOfSpeech expected)
    {
        var tokens = new List<TokenAnalysis> { Analysis(word) };

        _tagger.Tag(tokens);

        Assert.Equal(expected, tokens[0].PartOfSpeech);
    }

    /// <summary>
    /// The plural of a noun that is also a command verb must stay a NOUN. A command is imperative —
    /// "book a flight", never "books a flight" — so nothing needs the verb reading, while prose
    /// ("libraries lend books", "the customer's needs") needs the noun. Listing "books" as a verb
    /// cost a library domain its Book class entirely, silently.
    /// <para>
    /// Which words these are was measured over 4,000 dump articles: each of these is preceded by a
    /// determiner as often as an unmistakable noun is, and a determiner never precedes a finite verb.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("books")]
    [InlineData("needs")]
    [InlineData("shows")]
    [InlineData("plays")]
    [InlineData("schedules")]
    public void Tag_ThePluralOfANounThatIsAlsoACommandVerb_IsANoun(string word)
    {
        var tokens = new List<TokenAnalysis> { Analysis(word) };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Noun, tokens[0].PartOfSpeech);
    }

    /// <summary>
    /// The other half of the same rule: a word that is a verb in prose stays a verb, or the
    /// harvester turns it into a class. "lists" reads like a domain noun and measured 0.0% noun
    /// usage — the same as provides and includes — so it is not one.
    /// </summary>
    [Theory]
    [InlineData("lists")]
    [InlineData("provides")]
    [InlineData("includes")]
    [InlineData("makes")]
    [InlineData("uses")]
    public void Tag_AWordThatIsOnlyEverAVerbInProse_StaysAVerb(string word)
    {
        var tokens = new List<TokenAnalysis> { Analysis(word) };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Verb, tokens[0].PartOfSpeech);
    }

    /// <summary>The base form still belongs to the command language: "book a meeting".</summary>
    [Theory]
    [InlineData("book")]
    [InlineData("schedule")]
    [InlineData("show")]
    public void Tag_TheBaseFormOfACommandVerb_IsStillAVerb(string word)
    {
        var tokens = new List<TokenAnalysis> { Analysis(word) };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Verb, tokens[0].PartOfSpeech);
    }

    [Theory]
    [InlineData("provide", PartOfSpeech.Verb)]
    [InlineData("provides", PartOfSpeech.Verb)]
    [InlineData("offer", PartOfSpeech.Verb)]
    [InlineData("indicate", PartOfSpeech.Verb)]
    [InlineData("outsource", PartOfSpeech.Verb)]
    [InlineData("paid", PartOfSpeech.Verb)]
    [InlineData("gross", PartOfSpeech.Adjective)]
    [InlineData("federal", PartOfSpeech.Adjective)]
    [InlineData("financial", PartOfSpeech.Adjective)]
    [InlineData("physical", PartOfSpeech.Adjective)]
    [InlineData("public", PartOfSpeech.Adjective)]
    [InlineData("available", PartOfSpeech.Adjective)]
    [InlineData("accessible", PartOfSpeech.Adjective)]
    [InlineData("electronic", PartOfSpeech.Adjective)]
    [InlineData("ancient", PartOfSpeech.Adjective)]
    [InlineData("already", PartOfSpeech.Adverb)]
    [InlineData("accurately", PartOfSpeech.Adverb)]
    public void Tag_CommonContentWord_IsNotMistakenForANoun(string word, PartOfSpeech expected)
    {
        // The unknown-word fallback tags anything it doesn't know as a Noun, which promotes
        // frequent verbs/adjectives to domain entities downstream. The lexicon must know them.
        var tokens = new List<TokenAnalysis> { Analysis(word) };

        _tagger.Tag(tokens);

        Assert.Equal(expected, tokens[0].PartOfSpeech);
    }

    [Theory]
    [InlineData("pay")]
    [InlineData("state")]
    [InlineData("bill")]
    [InlineData("record")]
    [InlineData("access")]
    [InlineData("study")]
    [InlineData("cost")]
    public void Tag_WordThatIsBothNounAndVerb_StaysANoun(string word)
    {
        // These carry the domain meaning in "payroll"/"invoice"/"library" articles, so
        // tagging them as verbs would delete the entities users actually want.
        var tokens = new List<TokenAnalysis> { Analysis(word) };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Noun, tokens[0].PartOfSpeech);
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
    public void Tag_BusinessNounEndingInIng_UsesLexiconNotVerbSuffix()
    {
        // "billing" ends in -ing; without a lexicon entry the verb-suffix
        // heuristic would mis-tag it as a verb. The lexicon must win.
        var tokens = new List<TokenAnalysis> { Analysis("billing") };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Noun, tokens[0].PartOfSpeech);
    }

    [Fact]
    public void Tag_NumericToken_InfersNumeral()
    {
        var tokens = new List<TokenAnalysis> { Analysis("1234", TokenType.Number) };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Numeral, tokens[0].PartOfSpeech);
    }

    [Fact]
    public void Tag_UnknownLowercaseWord_DefaultsToNoun()
    {
        // Unknown word-type tokens fall back to Noun so free-form nouns
        // (e.g. "payroll") reach the semantic layer as entities.
        var tokens = new List<TokenAnalysis> { Analysis("xylophonic") };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Noun, tokens[0].PartOfSpeech);
    }

    [Fact]
    public void Tag_UnknownNonWordToken_StaysUnknown()
    {
        // Punctuation/symbols must NOT become nouns.
        var tokens = new List<TokenAnalysis> { Analysis(".", TokenType.Punctuation) };

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

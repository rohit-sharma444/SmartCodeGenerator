using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Syntax;
using NaturalLanguageEngine.Syntax;
using Xunit;

namespace NaturalLanguageEngine.Tests.Syntax;

public class PhraseDetectorTests
{
    private readonly PhraseDetector _detector = new();

    private static TokenAnalysis Tagged(
        string text,
        PartOfSpeech pos,
        TokenType type = TokenType.Word)
        => new()
        {
            Token = new Token { Text = text, Type = type },
            PartOfSpeech = pos
        };

    private static Sentence SentenceOf(params TokenAnalysis[] tokens)
    {
        var sentence = new Sentence();
        foreach (var t in tokens)
            sentence.Tokens.Add(t);
        return sentence;
    }

    [Fact]
    public void Detect_DeterminerAdjectiveNoun_FormsOneNounPhrase()
    {
        var sentence = SentenceOf(
            Tagged("the", PartOfSpeech.Determiner),
            Tagged("big", PartOfSpeech.Adjective),
            Tagged("car", PartOfSpeech.Noun));

        _detector.Detect(sentence);

        var phrase = Assert.Single(sentence.Phrases);
        Assert.Equal(PhraseType.NounPhrase, phrase.Type);
        Assert.Equal(3, phrase.Tokens.Count);
        Assert.Equal("car", phrase.Head!.Token.Text);
    }

    [Fact]
    public void Detect_AuxiliaryPlusVerb_FormsOneVerbPhrase()
    {
        var sentence = SentenceOf(
            Tagged("is", PartOfSpeech.Auxiliary),
            Tagged("running", PartOfSpeech.Verb));

        _detector.Detect(sentence);

        var phrase = Assert.Single(sentence.Phrases);
        Assert.Equal(PhraseType.VerbPhrase, phrase.Type);
        Assert.Equal("running", phrase.Head!.Token.Text);
    }

    [Fact]
    public void Detect_SpecialToken_IsWrappedAsSingleTokenNounPhrase()
    {
        var sentence = SentenceOf(
            Tagged("2025-06-30", PartOfSpeech.Unknown, TokenType.Date));

        _detector.Detect(sentence);

        var phrase = Assert.Single(sentence.Phrases);
        Assert.Equal(PhraseType.NounPhrase, phrase.Type);
        Assert.Single(phrase.Tokens);
    }

    [Fact]
    public void Detect_UnmatchedToken_ProducesNoPhraseAndIsSkipped()
    {
        // A lone preposition matches neither noun- nor verb-phrase rules.
        var sentence = SentenceOf(
            Tagged("to", PartOfSpeech.Preposition),
            Tagged("London", PartOfSpeech.Noun));

        _detector.Detect(sentence);

        // Only "London" becomes a phrase; "to" is skipped entirely.
        var phrase = Assert.Single(sentence.Phrases);
        Assert.Equal("London", phrase.Head!.Token.Text);
    }

    [Fact]
    public void Detect_EmptySentence_ProducesNoPhrases()
    {
        var sentence = SentenceOf();

        _detector.Detect(sentence);

        Assert.Empty(sentence.Phrases);
    }
}

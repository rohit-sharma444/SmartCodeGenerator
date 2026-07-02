using NaturalLanguageEngine.Lexical;
using NaturalLanguageEngine.Tests.TestSupport;
using Xunit;

namespace NaturalLanguageEngine.Tests.Lexical;

public class SentenceSplitterTests
{
    private readonly SentenceSplitter _splitter =
        EngineTestFactory.CreateSentenceSplitter();

    [Fact]
    public void Split_TwoSimpleSentences_ProducesTwoSentences()
    {
        var sentences = _splitter.Split("Sarah booked a flight. Send it to John.");

        Assert.Equal(2, sentences.Count);
        Assert.Equal("Sarah", sentences[0].Tokens[0].Token.Text);
        Assert.Equal("Send", sentences[1].Tokens[0].Token.Text);
    }

    [Fact]
    public void Split_QuestionAndExclamation_ProducesTwoSentences()
    {
        var sentences = _splitter.Split("Are you coming? Yes, I am!");

        Assert.Equal(2, sentences.Count);
        Assert.Equal("?", sentences[0].Tokens[^1].Token.Text);
        Assert.Equal("!", sentences[1].Tokens[^1].Token.Text);
    }

    [Fact]
    public void Split_SingleSentence_ProducesOneSentence()
    {
        var sentences = _splitter.Split("Hello world");

        Assert.Single(sentences);
    }

    [Fact]
    public void Split_AbbreviationFollowedByPeriod_DoesNotSplit()
    {
        // "Dr." must not be treated as a sentence boundary.
        var sentences = _splitter.Split("Dr. Smith arrived yesterday.");

        Assert.Single(sentences);
        Assert.Contains(sentences[0].Tokens, t => t.Token.Text == "Smith");
    }

    [Fact]
    public void Split_ThreeSentences_ProducesThreeSentences()
    {
        var sentences = _splitter.Split("First one. Second one. Third one.");

        Assert.Equal(3, sentences.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Split_BlankInput_ProducesNoSentences(string input)
    {
        var sentences = _splitter.Split(input);

        Assert.Empty(sentences);
    }

    [Fact]
    public void Split_SentenceIndexesAreSequential()
    {
        // SentenceSplitter itself doesn't assign Index (TokenizationStage
        // does), so this only checks Split returns sentences in text order.
        var sentences = _splitter.Split("One. Two. Three.");

        Assert.Equal(3, sentences.Count);
        Assert.Equal("One", sentences[0].Tokens[0].Token.Text);
        Assert.Equal("Two", sentences[1].Tokens[0].Token.Text);
        Assert.Equal("Three", sentences[2].Tokens[0].Token.Text);
    }
}

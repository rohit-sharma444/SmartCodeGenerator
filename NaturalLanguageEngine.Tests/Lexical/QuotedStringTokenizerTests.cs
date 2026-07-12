using NaturalLanguageEngine.Lexical;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Tests.TestSupport;
using Xunit;

namespace NaturalLanguageEngine.Tests.Lexical;

/// <summary>
/// A quote character only opens a quoted string when a matching quote closes it
/// nearby. Prose is full of scare quotes and apostrophes, and a greedy quote
/// tokenizer pairs them with whatever delimiter it finds next - swallowing whole
/// paragraphs (and every noun in them) into a single token.
/// </summary>
public class QuotedStringTokenizerTests
{
    private readonly Tokenizer _tokenizer = EngineTestFactory.CreateTokenizer();

    [Fact]
    public void Tokenize_UnterminatedQuote_DoesNotSwallowRestOfInput()
    {
        var tokens = _tokenizer.Tokenize("He said \"hello world");

        Assert.DoesNotContain(tokens, t => t.Type == TokenType.QuotedString);
        Assert.Contains(tokens, t => t.Type == TokenType.Word && t.Text == "hello");
        Assert.Contains(tokens, t => t.Type == TokenType.Word && t.Text == "world");
    }

    [Fact]
    public void Tokenize_QuoteClosedOnALaterLine_DoesNotSpanTheNewline()
    {
        var tokens = _tokenizer.Tokenize("He said \"hello\nworld\" loudly");

        Assert.DoesNotContain(tokens, t => t.Type == TokenType.QuotedString);
        Assert.Contains(tokens, t => t.Type == TokenType.Word && t.Text == "hello");
        Assert.Contains(tokens, t => t.Type == TokenType.Word && t.Text == "world");
    }

    [Fact]
    public void Tokenize_QuoteClosedInALaterSentence_DoesNotSpanTheSentenceBoundary()
    {
        var tokens = _tokenizer.Tokenize("A \"point of sale. Another sentence\" here.");

        Assert.DoesNotContain(tokens, t => t.Type == TokenType.QuotedString);
        Assert.Contains(tokens, t => t.Type == TokenType.Word && t.Text == "point");
        Assert.Contains(tokens, t => t.Type == TokenType.Word && t.Text == "sentence");
    }

    [Fact]
    public void Tokenize_StrayApostropheThenLaterPossessive_DoesNotPairThem()
    {
        // The lone leading apostrophe must not pair with the apostrophe inside
        // "customer's" several words later.
        var tokens = _tokenizer.Tokenize("Bring 'em to the customer's counter");

        Assert.DoesNotContain(tokens, t => t.Type == TokenType.QuotedString);
        Assert.Contains(tokens, t => t.Type == TokenType.Word && t.Text == "counter");
    }

    [Fact]
    public void Tokenize_ClosedDoubleQuote_IsStillAQuotedString()
    {
        var tokens = _tokenizer.Tokenize("Set the name to \"Order Item\" now.");

        var quoted = Assert.Single(tokens, t => t.Type == TokenType.QuotedString);
        Assert.Equal("\"Order Item\"", quoted.Text);
    }

    [Fact]
    public void Tokenize_ClosedSingleQuote_IsStillAQuotedString()
    {
        var tokens = _tokenizer.Tokenize("He said 'hello' loudly.");

        var quoted = Assert.Single(tokens, t => t.Type == TokenType.QuotedString);
        Assert.Equal("'hello'", quoted.Text);
    }

    [Fact]
    public void Tokenize_Possessive_StaysPartOfTheWord()
    {
        var tokens = _tokenizer.Tokenize("The customer's order");

        Assert.DoesNotContain(tokens, t => t.Type == TokenType.QuotedString);
        Assert.Contains(tokens, t => t.Type == TokenType.Word && t.Text == "customer's");
    }

    [Fact]
    public void Tokenize_TwoQuotedPhrasesInOneSentence_RemainSeparateTokens()
    {
        var tokens = _tokenizer.Tokenize("A \"cash register\" and a \"cash drawer\" are used");

        Assert.Equal(2, tokens.Count(t => t.Type == TokenType.QuotedString));
    }
}

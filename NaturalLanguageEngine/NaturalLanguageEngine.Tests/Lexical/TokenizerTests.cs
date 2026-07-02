using NaturalLanguageEngine.Lexical;
using NaturalLanguageEngine.Lexical.Tokenizers;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Tests.TestSupport;
using Xunit;

namespace NaturalLanguageEngine.Tests.Lexical;

public class TokenizerTests
{
    private readonly Tokenizer _tokenizer = EngineTestFactory.CreateTokenizer();

    [Fact]
    public void Tokenize_RecognizesUrl()
    {
        var tokens = _tokenizer.Tokenize("Visit https://example.com for details.");

        var url = Assert.Single(tokens, t => t.Type == TokenType.Url);
        Assert.Equal("https://example.com", url.Text);
    }

    [Fact]
    public void Tokenize_RecognizesEmail()
    {
        var tokens = _tokenizer.Tokenize("Email john@example.com about it.");

        var email = Assert.Single(tokens, t => t.Type == TokenType.Email);
        Assert.Equal("john@example.com", email.Text);
    }

    [Fact]
    public void Tokenize_RecognizesDate()
    {
        var tokens = _tokenizer.Tokenize("The date is 2025-06-30 for sure.");

        var date = Assert.Single(tokens, t => t.Type == TokenType.Date);
        Assert.Equal("2025-06-30", date.Text);
    }

    [Fact]
    public void Tokenize_RecognizesTime()
    {
        var tokens = _tokenizer.Tokenize("Meet me at 14:30 today.");

        var time = Assert.Single(tokens, t => t.Type == TokenType.Time);
        Assert.Equal("14:30", time.Text);
    }

    [Fact]
    public void Tokenize_RecognizesNumber()
    {
        var tokens = _tokenizer.Tokenize("I have 42 apples.");

        var number = Assert.Single(tokens, t => t.Type == TokenType.Number);
        Assert.Equal("42", number.Text);
    }

    [Fact]
    public void Tokenize_RecognizesWords()
    {
        var tokens = _tokenizer.Tokenize("hello world");

        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, t => Assert.Equal(TokenType.Word, t.Type));
        Assert.Equal("hello", tokens[0].Text);
        Assert.Equal("world", tokens[1].Text);
    }

    [Fact]
    public void Tokenize_RecognizesPunctuation()
    {
        var tokens = _tokenizer.Tokenize("Hi!");

        Assert.Equal(TokenType.Punctuation, tokens[^1].Type);
        Assert.Equal("!", tokens[^1].Text);
    }

    [Fact]
    public void Tokenize_UnclassifiableCharacter_FallsBackToSymbol()
    {
        // '$' is neither a letter/digit, whitespace, nor Unicode
        // punctuation - it should hit the SymbolTokenizer safety net.
        var tokens = _tokenizer.Tokenize("$");

        var symbol = Assert.Single(tokens);
        Assert.Equal(TokenType.Symbol, symbol.Type);
        Assert.Equal("$", symbol.Text);
    }

    [Fact]
    public void Tokenize_EmptyInput_ReturnsNoTokens()
    {
        Assert.Empty(_tokenizer.Tokenize(string.Empty));
    }

    [Fact]
    public void Tokenize_IgnoresWhitespaceBetweenTokens()
    {
        var tokens = _tokenizer.Tokenize("a   b");

        Assert.Equal(2, tokens.Count);
    }
}

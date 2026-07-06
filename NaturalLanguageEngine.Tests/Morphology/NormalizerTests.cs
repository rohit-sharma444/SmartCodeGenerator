using NaturalLanguageEngine.Morphology;
using Xunit;

namespace NaturalLanguageEngine.Tests.Morphology;

public class NormalizerTests
{
    private readonly Normalizer _normalizer = new();

    [Fact]
    public void Normalize_LowercasesAndCollapsesWhitespace()
    {
        Assert.Equal("hello world", _normalizer.Normalize("  Hello   World  "));
    }

    [Fact]
    public void Normalize_StripsDiacritics()
    {
        Assert.Equal("cafe", _normalizer.Normalize("Café"));
    }

    [Fact]
    public void Normalize_StandardizesCurlyQuotesAndDashes()
    {
        Assert.Equal("don't - ok", _normalizer.Normalize("Don\u2019t \u2014 OK"));
    }

    [Fact]
    public void Normalize_StandardizesCurlyDoubleQuotes()
    {
        Assert.Equal("she said \"hi\"", _normalizer.Normalize("She said \u201Chi\u201D"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankInput_ReturnsEmpty(string input)
    {
        Assert.Equal(string.Empty, _normalizer.Normalize(input));
    }
}

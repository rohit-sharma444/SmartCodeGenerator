using NaturalLanguageEngine.Morphology;
using Xunit;

namespace NaturalLanguageEngine.Tests.Morphology;

public class StemmerTests
{
    private readonly Stemmer _stemmer = new();

    [Theory]
    [InlineData("cats", "cat")]
    [InlineData("boxes", "box")]
    [InlineData("kisses", "kiss")]
    [InlineData("flies", "fly")]
    [InlineData("running", "run")]
    [InlineData("stopped", "stop")]
    [InlineData("wanted", "want")]
    [InlineData("quickly", "quick")]
    public void Stem_StripsCommonSuffixes(string input, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(input));
    }

    [Theory]
    [InlineData("cat")]
    [InlineData("the")]
    [InlineData("a")]
    public void Stem_LeavesShortWordsUnchanged(string input)
    {
        Assert.Equal(input, _stemmer.Stem(input));
    }

    [Fact]
    public void Stem_IsCaseInsensitiveAndLowercasesResult()
    {
        Assert.Equal("cat", _stemmer.Stem("CATS"));
    }

    [Fact]
    public void Stem_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _stemmer.Stem(string.Empty));
    }

    [Fact]
    public void Stem_DoesNotOverStripWordsEndingInDoubleS()
    {
        // "class" ends with "ss", so the single-trailing-"s" plural rule
        // must not fire (that rule explicitly excludes "ss" endings).
        Assert.Equal("class", _stemmer.Stem("class"));
    }

    [Fact]
    public void Stem_DropsDoubledConsonantAfterStrippingIng()
    {
        // "running" -> strip "ing" -> "runn" -> doubled "n" is not a vowel,
        // so it gets dropped -> "run".
        Assert.Equal("run", _stemmer.Stem("running"));
    }

    [Fact]
    public void Stem_KeepsSingleFinalConsonantAfterStrippingIng()
    {
        // "jumping" -> strip "ing" -> "jump", no doubled consonant to drop.
        Assert.Equal("jump", _stemmer.Stem("jumping"));
    }
}

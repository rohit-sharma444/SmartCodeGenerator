using NaturalLanguageEngine.Knowledge.Repositories;
using NaturalLanguageEngine.Tests.TestSupport;
using Xunit;

namespace NaturalLanguageEngine.Tests.Knowledge;

public class LanguageRepositoryTests
{
    private readonly ILanguageRepository _repository = EngineTestFactory.CreateEnglishRepository();

    [Theory]
    [InlineData("london")]
    [InlineData("London")]
    [InlineData("PARIS")]
    public void IsKnownLocation_KnownPlace_IsCaseInsensitivelyTrue(string word)
        => Assert.True(_repository.IsKnownLocation(word));

    [Fact]
    public void IsKnownLocation_UnknownWord_IsFalse()
        => Assert.False(_repository.IsKnownLocation("Zephyria"));

    [Theory]
    [InlineData("google")]
    [InlineData("Corp")]
    [InlineData("University")]
    public void IsKnownOrganization_KnownNameOrSuffix_IsTrue(string word)
        => Assert.True(_repository.IsKnownOrganization(word));

    [Fact]
    public void IsKnownOrganization_TrailingPeriodIsTolerated()
        => Assert.True(_repository.IsKnownOrganization("Corp."));

    [Fact]
    public void IsKnownOrganization_UnknownWord_IsFalse()
        => Assert.False(_repository.IsKnownOrganization("Zephyria"));

    [Fact]
    public void IsStopWord_CommonWord_IsTrue()
        => Assert.True(_repository.IsStopWord("the"));

    [Fact]
    public void IsStopWord_ContentWord_IsFalse()
        => Assert.False(_repository.IsStopWord("elephant"));

    [Theory]
    [InlineData("Dr")]
    [InlineData("Dr.")]
    public void IsAbbreviation_TrailingPeriodIsTolerated(string word)
        => Assert.True(_repository.IsAbbreviation(word));

    [Fact]
    public void GetIrregularForms_KnownRoot_ReturnsAllForms()
    {
        var forms = _repository.GetIrregularForms("go");

        Assert.Contains("went", forms);
        Assert.Contains("goes", forms);
        Assert.Contains("gone", forms);
        Assert.Contains("going", forms);
    }

    [Fact]
    public void GetIrregularForms_UnknownRoot_ReturnsEmpty()
        => Assert.Empty(_repository.GetIrregularForms("xyzzy"));

    [Fact]
    public void TryExpandContraction_KnownContraction_ReturnsExpansion()
    {
        var found = _repository.TryExpandContraction("don't", out var expanded);

        Assert.True(found);
        Assert.Equal("do not", expanded);
    }

    [Fact]
    public void TryGetSynonym_KnownWord_ReturnsCanonical()
    {
        var found = _repository.TryGetSynonym("purchase", out var canonical);

        Assert.True(found);
        Assert.Equal("buy", canonical);
    }

    [Fact]
    public void TryGetWord_KnownWord_ReturnsEntry()
    {
        var found = _repository.TryGetWord("the", out var entry);

        Assert.True(found);
        Assert.Equal("determiner", entry.PartOfSpeech);
    }

    [Fact]
    public void Constructor_NullProvider_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new LanguageRepository(null!));
}

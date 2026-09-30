using DomainDiscovery.Bundled;
using Xunit;

namespace DomainDiscovery.Tests.Bundled;

public class BundledDomainCatalogTests
{
    private static BundledDomain Domain(string name, string terms) => BundledDomain.Parse(
        "{ \"name\": \"" + name + "\", \"terms\": {" + terms + "}, "
        + "\"classes\": [ { \"name\": \"Thing\", \"role\": \"Item\", \"properties\": [] } ] }");

    private static readonly BundledDomainCatalog Two = new([
        Domain("Hotel", "\"hotel\": 3, \"room\": 1, \"booking\": 1"),
        Domain("Library", "\"library\": 3, \"book\": 1, \"lend\": 1, \"member\": 1")
    ]);

    [Fact]
    public void ADomainNameMatchesOnItsOwn()
    {
        var match = Two.Match("a library");
        Assert.Equal("Library", match!.Domain.Name);
        Assert.Equal(3, match.Score);
    }

    [Fact]
    public void ThreeSupportingWordsMatchAndTheWordsAreReportedAsTyped()
    {
        var match = Two.Match("lends Books to members");
        Assert.Equal("Library", match!.Domain.Name);
        Assert.Equal(new[] { "lends", "Books", "members" }, match.MatchedWords);
    }

    [Theory]
    [InlineData("a book club")]            // 1 point
    [InlineData("bookkeeping for a firm")] // not "book"
    [InlineData("")]
    [InlineData(null)]
    public void BelowTheThresholdNothingMatches(string? description) => Assert.Null(Two.Match(description));

    [Fact]
    public void ARepeatedWordCountsOnce() => Assert.Null(Two.Match("books books books"));

    [Fact]
    public void TheDomainNameBeatsASharedSupportingWord()
        => Assert.Equal("Hotel", Two.Match("a hotel booking system")!.Domain.Name);

    [Fact]
    public void ATieGoesToTheDomainWithANameHitThenToCatalogOrder()
    {
        var tied = new BundledDomainCatalog([
            Domain("Alpha", "\"xray\": 1, \"yank\": 1, \"zulu\": 1"),
            Domain("Beta", "\"beta\": 3")
        ]);
        Assert.Equal("Beta", tied.Match("xray yank zulu beta")!.Domain.Name);   // 3 vs 3: Beta has the name hit

        var plain = new BundledDomainCatalog([Domain("Alpha", "\"alpha\": 3"), Domain("Beta", "\"beta\": 3")]);
        Assert.Equal("Alpha", plain.Match("alpha beta")!.Domain.Name);
    }

    [Fact]
    public void ParseRejectsATermWithASpaceOrAWeightOutOfRange()
    {
        Assert.Throws<FormatException>(() => Domain("X", "\"point of sale\": 3"));
        Assert.Throws<FormatException>(() => Domain("X", "\"till\": 4"));
        Assert.Throws<FormatException>(() => Domain("X", "\"till\": 0"));
    }

    [Fact]
    public void WordsSharingAStemKeepTheStrongerWeight()
    {
        var domain = Domain("Purchasing", "\"purchasing\": 3, \"purchase\": 1");
        Assert.Equal(3, domain.Terms[WordStem.Of("purchase")]);
    }

    [Fact]
    public void TheDefaultCatalogLoadsTheEmbeddedFiles()
        => Assert.Contains(BundledDomainCatalog.Default.Domains, d => d.Name == "Library");
}

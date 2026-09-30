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
    public void ParseRejectsATermThatIsNotWordsOrAWeightOutOfRange()
    {
        Assert.Throws<FormatException>(() => Domain("X", "\"24x7\": 3"));
        Assert.Throws<FormatException>(() => Domain("X", "\"till\": 4"));
        Assert.Throws<FormatException>(() => Domain("X", "\"till\": 0"));
    }

    [Fact]
    public void TermsSharingAStemAtDifferentWeightsAreRefused()
    {
        // Merged, the stronger weight promoted the weaker word: "customers purchase products"
        // took purchasing's 3 and picked Purchasing. The file has to say one weight.
        Assert.Throws<FormatException>(() => Domain("Purchasing", "\"purchasing\": 3, \"purchase\": 1"));
        Assert.Equal(3, Domain("Invoicing", "\"invoice\": 3, \"invoicing\": 3").Terms[WordStem.Of("invoice")]);
    }

    [Fact]
    public void APhraseTermMatchesItsWordsSideBySideAndIsReportedAsTyped()
    {
        var catalog = new BundledDomainCatalog([Domain("Point of sale", "\"point of sale\": 3, \"till\": 1")]);

        var match = catalog.Match("a Point-of-Sale system");
        Assert.Equal("Point of sale", match!.Domain.Name);
        Assert.Equal(new[] { "Point of Sale" }, match.MatchedWords);
        Assert.Null(catalog.Match("the point is the sale"));   // the words, but not side by side
    }

    [Fact]
    public void SelfDefaultsToTheNameAndAFileCanSayOtherwise()
    {
        Assert.Equal(new[] { "library" }, Domain("Library", "\"library\": 3").Self);

        var rental = BundledDomain.Parse("""
            { "name": "Property rental", "self": [], "terms": { "landlord": 3 }, "classes": [] }
            """);
        Assert.Empty(rental.Self);
    }

    [Fact]
    public void TheDefaultCatalogLoadsTheEmbeddedFiles()
        => Assert.Contains(BundledDomainCatalog.Default.Domains, d => d.Name == "Library");
}

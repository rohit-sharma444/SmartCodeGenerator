using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class HypernymExtractorTests
{
    [Theory]
    // Real first sentences, fetched 2026-07-12.
    [InlineData("In sales, commerce and economics, a customer is the recipient of a good.", "recipient")]
    [InlineData("A receipt is a document acknowledging that a person has received money.", "document")]
    [InlineData("A restaurant is a business that prepares and serves food.", "business")]
    [InlineData("A computer is a machine that can be programmed.", "machine")]
    [InlineData("In computing, a database is an organized collection of data.", "collection")]
    [InlineData("A system is a group of interacting elements.", "group")]
    [InlineData("Business is the practice of making one's living.", "practice")]
    [InlineData("A tablet computer is a mobile device.", "device")]
    [InlineData("A wedding is a gathering of people.", "gathering")]
    [InlineData("A credit card (or charge card) is a payment card, usually issued by a bank, allowing the card's users to purchase goods and services.", "card")]
    [InlineData("A merchant is a person or company engaged in trade, conducting sales through stores or online platforms.", "company")]
    [InlineData("A debit card, also known as a check card, cheque card or bank card, is a payment card that can be used in place of cash to make purchases.", "card")]
    // Fetched 2026-07-12, from the Point of sale run that stranded these as suggestions.
    [InlineData("Self-checkouts are machines that allow customers to complete their own transactions.", "machine")]   // after singularization
    [InlineData("A touchscreen is a type of display that can detect touch input from a user.", "display")]
    [InlineData("Cash is money in the tangible form of currency.", "money")]
    public void Extract_TakesTheHeadNoun_OfThePredicateNominal(string lead, string expected)
    {
        Assert.Equal(expected, HypernymExtractor.Extract(lead));
    }

    [Fact]
    public void Extract_OfADisambiguationLead_IsEmpty()
    {
        Assert.Equal(string.Empty, HypernymExtractor.Extract("Sale may refer to:"));
    }

    [Fact]
    public void Extract_WhenThereIsNoCopula_IsEmpty()
    {
        Assert.Equal(
            string.Empty,
            HypernymExtractor.Extract("Software consists of computer programs."));
    }

    [Fact]
    public void Extract_OfBlank_IsEmpty()
    {
        Assert.Equal(string.Empty, HypernymExtractor.Extract("   "));
    }

    [Theory]
    // Each of these is a real Wikipedia lead whose head the extractor got wrong, taken from
    // the twelve-domain census in the design spec. The word it used to return is in the
    // second column of that census: often, undertaken, primarily, not.
    [InlineData("A hotel rating is often a ranking of a hotel's quality.", "ranking")]
    [InlineData("Business travel is travel undertaken for work purposes.", "travel")]
    [InlineData("A public hospital is primarily a hospital owned by a government.", "hospital")]
    [InlineData("A hostel is a low-cost building providing shared accommodation.", "building")]
    public void Extract_SkipsAModifier_AndTakesTheNounBehindIt(string lead, string expected)
    {
        Assert.Equal(expected, HypernymExtractor.Extract(lead));
    }

    [Fact]
    public void Extract_DoesNotReturnAConjunction_AsTheHead()
    {
        // "and"/"or" join phrases, they are never the thing being defined.
        Assert.NotEqual("and", HypernymExtractor.Extract("A till is and was a drawer."));
        Assert.NotEqual("or", HypernymExtractor.Extract("A ledger is or may be a record."));
    }

    [Fact]
    public void Extract_StillReturnsAHeadThatMerelyLooksLikeAModifier()
    {
        // "gathering" is the head, not a modifier of one — nothing has been captured yet when
        // it is reached. This is existing behaviour and must not regress.
        Assert.Equal("gathering", HypernymExtractor.Extract("A meeting is a gathering of people."));
    }
}

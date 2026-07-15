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
}

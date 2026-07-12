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

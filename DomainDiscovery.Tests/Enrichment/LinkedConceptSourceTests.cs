using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class LinkedConceptSourceTests
{
    private const string Article = """
        A point of sale system is used in a retail store. The cash register records each
        sale, and the cash register prints a receipt. A receipt is given to the customer.
        The customer may pay with a credit card; a credit card is read by the payment terminal.
        The payment terminal talks to the merchant bank. Gondola shelving holds the stock.
        The National Retail Federation publishes standards.
        """;

    [Fact]
    public void Candidates_KeepsALinkTheArticleActuallyTalksAbout()
    {
        var links = new[] { "Cash register", "Receipt", "Credit card", "Payment terminal" };

        var kept = LinkedConceptSource.Candidates(links, Article);

        Assert.Equal(new[] { "Cash register", "Receipt", "Credit card", "Payment terminal" }, kept);
    }

    [Fact]
    public void Candidates_DropsALinkMentionedOnlyOnce()
    {
        // Linked, but the article says it once in passing - it is not what the article is about.
        var kept = LinkedConceptSource.Candidates(new[] { "Merchant bank" }, Article);

        Assert.Empty(kept);
    }

    [Fact]
    public void Candidates_DropsAParentheticalTitle()
    {
        Assert.Empty(LinkedConceptSource.Candidates(new[] { "Gondola (retail)" }, Article));
    }

    [Fact]
    public void Candidates_DropsAnOverlongTitle()
    {
        var kept = LinkedConceptSource.Candidates(
            new[] { "Automated sales suppression device" }, Article);

        Assert.Empty(kept);
    }

    [Fact]
    public void Candidates_DropsAProperNoun()
    {
        // Two or more capitalized words: an organization, not a domain concept.
        var kept = LinkedConceptSource.Candidates(new[] { "National Retail Federation" }, Article);

        Assert.Empty(kept);
    }

    [Fact]
    public void Candidates_OfNoLinks_IsEmpty()
    {
        Assert.Empty(LinkedConceptSource.Candidates(Array.Empty<string>(), Article));
    }
}

using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class LeadSectionTests
{
    [Fact]
    public void Of_StopsAtTheFirstHeading()
    {
        var body = string.Join('\n',
            "A payroll is a list of employees entitled to compensation.",
            "Payroll is subject to federal and state regulations.",
            "Frequency",
            "Companies typically process payroll at regular intervals.");

        var lead = LeadSection.Of(body);

        Assert.Contains("compensation", lead);
        Assert.Contains("regulations", lead);
        Assert.DoesNotContain("Frequency", lead);
        Assert.DoesNotContain("intervals", lead);
    }

    [Fact]
    public void Of_TreatsARunTogetherHeadingPairAsAHeading()
    {
        // The stripper flattens "== History ==" and "=== Software before the 1990s ===" onto one
        // line when no blank line separates them. It is still a heading, and the real Point of
        // sale article really does contain this one.
        var body = string.Join('\n',
            "The point of sale is where a transaction is completed.",
            "HistorySoftware before the 1990s",
            "Early electronic cash registers were controlled with proprietary software.");

        var lead = LeadSection.Of(body);

        Assert.Contains("transaction", lead);
        Assert.DoesNotContain("proprietary", lead);
    }

    [Fact]
    public void Of_DoesNotMistakeABulletForAHeading()
    {
        var body = string.Join('\n',
            "An invoice is a commercial document.",
            "* Invoice number",
            "* Seller's logo",
            "The buyer must pay within an agreed period.");

        var lead = LeadSection.Of(body);

        Assert.Contains("buyer", lead);
    }

    [Fact]
    public void Of_DoesNotMistakeASentenceForAHeading()
    {
        // Short, but it ends like a sentence, so it is prose.
        var body = string.Join('\n',
            "A receipt records a sale.",
            "It is issued to the customer.",
            "History",
            "Receipts are ancient.");

        var lead = LeadSection.Of(body);

        Assert.Contains("customer", lead);
        Assert.DoesNotContain("ancient", lead);
    }

    [Fact]
    public void Of_ReturnsTheWholeBody_WhenItHasNoHeadingAtAll()
    {
        var body = "A stub article with a single paragraph and no sections.";

        Assert.Equal(body, LeadSection.Of(body));
    }

    [Fact]
    public void Of_IsEmptyForEmptyInput()
        => Assert.Equal(string.Empty, LeadSection.Of("   "));
}

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
    // Fix 1 changed this expectation from "company" to "person": a conjunction after the head
    // now ends the phrase, so the FIRST disjunct wins instead of the second silently
    // overwriting it — exactly the "room or area" bug this fix exists for.
    [InlineData("A merchant is a person or company engaged in trade, conducting sales through stores or online platforms.", "person")]
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
    public void Extract_SkipsAModifier_AndTakesTheNounBehindIt(string lead, string expected)
    {
        Assert.Equal(expected, HypernymExtractor.Extract(lead));
    }

    [Fact]
    public void Extract_DoesNotReturnAConjunction_AsTheHead()
    {
        // "and"/"or" join phrases, they are never the thing being defined. Asserted positively
        // (not just NotEqual "and"/"or") so the test actually fails if the scan regresses to
        // capturing some OTHER wrong word instead of skipping over the conjunction.
        Assert.Equal("drawer", HypernymExtractor.Extract("A till is and was a drawer."));
        Assert.Equal("record", HypernymExtractor.Extract("A ledger is or may be a record."));
    }

    [Fact]
    public void Extract_StopsAtAConjunctionAfterTheHead()
    {
        // Without this, the second disjunct wins and "area" — an abstraction — deletes the
        // candidate outright.
        Assert.Equal("room", HypernymExtractor.Extract("A kitchen is a room or area, part of a house."));
    }

    [Fact]
    public void Extract_StillReturnsAHeadThatMerelyLooksLikeAModifier()
    {
        // "gathering" is the head, not a modifier of one — nothing has been captured yet when
        // it is reached. This is existing behaviour and must not regress.
        Assert.Equal("gathering", HypernymExtractor.Extract("A meeting is a gathering of people."));
    }

    [Fact]
    public void Extract_DoesNotTreatAnOrdinaryNounEndingInEn_AsAParticiple()
    {
        // A suffix rule for irregular participles would fire on chicken, kitchen, garden,
        // citizen and children. The head here is the noun, not the adjective before it.
        Assert.Equal("chicken", HypernymExtractor.Extract("A hen is a female chicken."));
    }

    [Fact]
    public void Extract_StillStopsAtAnIrregularParticiple()
    {
        // A different irregular participle from the one already covered by
        // Extract_SkipsAModifier_AndTakesTheNounBehindIt ("undertaken"), so this adds real
        // coverage instead of duplicating that theory row.
        Assert.Equal("session", HypernymExtractor.Extract("A workshop is a session held for training purposes."));
    }

    [Fact]
    public void Extract_TreatsACommonLyEndingWord_AsANoun_NotAnAdverb()
    {
        // The blanket "-ly" adverb-suffix rule would otherwise strip "family" right where the
        // head sits, leaving the scan with no noun to find.
        Assert.Equal("family", HypernymExtractor.Extract("A clan is a family of related people."));
    }
}

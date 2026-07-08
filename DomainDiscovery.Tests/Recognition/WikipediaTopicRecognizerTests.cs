using DomainDiscovery.Model;
using DomainDiscovery.Recognition;
using DomainDiscovery.Tests.Fakes;
using Xunit;

namespace DomainDiscovery.Tests.Recognition;

public class WikipediaTopicRecognizerTests
{
    [Fact]
    public void Recognize_PicksRank0Hit_AsTopic()
    {
        var client = new FakeWikipediaClient();
        client.SearchHits.Add(new SearchHit("Library", "https://en.wikipedia.org/wiki/Library", 0));
        client.SearchHits.Add(new SearchHit("Librarian", "https://en.wikipedia.org/wiki/Librarian", 1));

        var topic = new WikipediaTopicRecognizer(client).Recognize(new[] { "Book", "Member" });

        Assert.Equal("Library", topic.Title);
        Assert.True(topic.Confidence > 0);
        Assert.Contains("Book", client.SearchedTerms);
    }

    [Fact]
    public void Recognize_SearchesEverySeedTermSeparately()
    {
        var client = new FakeWikipediaClient();

        new WikipediaTopicRecognizer(client).Recognize(new[] { "Book", "Member" });

        Assert.Equal(new[] { "Book", "Member" }, client.SearchedTerms);
    }

    [Fact]
    public void Recognize_PrefersTitleAgreedOnByMostSeedTerms_OverASingleTopRankedHit()
    {
        var client = new FakeWikipediaClient();
        // "Ledger" is the top hit for one term and absent for the other  -> 1.0.
        // "Accounting" is found by both terms (second, then top)         -> 0.5 + 1.0 = 1.5.
        client.HitsByTerm["Ledger"] = new()
        {
            new SearchHit("Ledger", "https://en.wikipedia.org/wiki/Ledger", 0),
            new SearchHit("Accounting", "https://en.wikipedia.org/wiki/Accounting", 1),
        };
        client.HitsByTerm["Invoice"] = new()
        {
            new SearchHit("Accounting", "https://en.wikipedia.org/wiki/Accounting", 0),
            new SearchHit("Invoice", "https://en.wikipedia.org/wiki/Invoice", 1),
        };

        var topic = new WikipediaTopicRecognizer(client).Recognize(new[] { "Ledger", "Invoice" });

        Assert.Equal("Accounting", topic.Title);
        Assert.Equal(0.75, topic.Confidence, 3);
    }

    [Fact]
    public void Recognize_ConfidenceReflectsHowManySeedTermsAgree()
    {
        var client = new FakeWikipediaClient();
        var hit = new SearchHit("Library", "https://en.wikipedia.org/wiki/Library", 0);
        client.HitsByTerm["Book"] = new() { hit };
        client.HitsByTerm["Member"] = new() { hit };
        client.HitsByTerm["Widget"] = new();   // no hits for this term

        var both = new WikipediaTopicRecognizer(client).Recognize(new[] { "Book", "Member" });
        var one = new WikipediaTopicRecognizer(client).Recognize(new[] { "Book", "Widget" });

        Assert.Equal(1.0, both.Confidence, 3);
        Assert.Equal(0.5, one.Confidence, 3);
    }

    [Fact]
    public void Recognize_DeduplicatesSeedTerms_IgnoringCase()
    {
        var client = new FakeWikipediaClient();

        new WikipediaTopicRecognizer(client).Recognize(new[] { "Book", "book", "BOOK" });

        Assert.Equal(new[] { "Book" }, client.SearchedTerms);
    }

    [Fact]
    public void Recognize_CapsTheNumberOfSearches_ForLongSeedSets()
    {
        var client = new FakeWikipediaClient();
        var terms = Enumerable.Range(1, 20).Select(i => $"Term{i}").ToArray();

        new WikipediaTopicRecognizer(client).Recognize(terms);

        Assert.Equal(6, client.SearchedTerms.Count);
    }

    [Fact]
    public void Recognize_NoHits_ReturnsNone()
    {
        var topic = new WikipediaTopicRecognizer(new FakeWikipediaClient())
            .Recognize(new[] { "Book" });

        Assert.False(topic.IsRecognized);
    }

    [Fact]
    public void Recognize_NoSeedEntities_ReturnsNone_WithoutSearching()
    {
        var client = new FakeWikipediaClient();
        client.SearchHits.Add(new SearchHit("X", "https://x", 0));

        var topic = new WikipediaTopicRecognizer(client).Recognize(Array.Empty<string>());

        Assert.False(topic.IsRecognized);
        Assert.Empty(client.SearchedTerms);
    }
}

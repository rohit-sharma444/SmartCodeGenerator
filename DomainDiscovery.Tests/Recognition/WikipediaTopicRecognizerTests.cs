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
        Assert.Contains("Book", client.LastSearchTerms);
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
        Assert.Empty(client.LastSearchTerms);
    }
}

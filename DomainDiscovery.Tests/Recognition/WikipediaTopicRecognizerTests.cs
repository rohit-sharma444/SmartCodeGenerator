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

    [Fact]
    public void Recognize_SearchesWordSequencesFromTheDescription_NotOnlySingleSeedWords()
    {
        var client = new FakeWikipediaClient();

        new WikipediaTopicRecognizer(client)
            .Recognize(new[] { "Point", "Sale" }, "a point of sale system for a retail shop");

        Assert.Contains("point of sale", client.SearchedTerms);
    }

    [Fact]
    public void Recognize_SkipsWordSequencesThatOpenOrCloseOnAFunctionWord()
    {
        var client = new FakeWikipediaClient();

        new WikipediaTopicRecognizer(client)
            .Recognize(new[] { "Point" }, "a point of sale system");

        // "of sale" and "a point" are fragments of the phrase, not names of anything.
        Assert.DoesNotContain("of sale", client.SearchedTerms);
        Assert.DoesNotContain("a point", client.SearchedTerms);
    }

    /// <summary>
    /// The real "point of sale" failure, in miniature: on the full encyclopedia the seed word
    /// "point" resolves exactly to "Point", a disambiguation page, which outscored the article the
    /// user actually meant. The phrase names it outright.
    /// </summary>
    [Fact]
    public void Recognize_PrefersTheArticleThePhraseNames_OverTheSingleWordsExactHit()
    {
        var client = new FakeWikipediaClient();
        client.HitsByTerm["Point"] = new()
        {
            new SearchHit("Point", "https://en.wikipedia.org/wiki/Point", 0),
            new SearchHit("Point of sale", "https://en.wikipedia.org/wiki/Point_of_sale", 2),
        };
        client.HitsByTerm["point of sale"] = new()
        {
            new SearchHit("Point of sale", "https://en.wikipedia.org/wiki/Point_of_sale", 0),
        };

        var topic = new WikipediaTopicRecognizer(client)
            .Recognize(new[] { "Point", "Sale" }, "a point of sale system for a retail shop");

        Assert.Equal("Point of sale", topic.Title);
    }

    [Fact]
    public void Recognize_RefusesADisambiguationPage_EvenWhenItIsTheOnlyTopRankedHit()
    {
        var client = new FakeWikipediaClient();
        client.HitsByTerm["Point"] = new()
        {
            new SearchHit("Point", "https://en.wikipedia.org/wiki/Point", 0),
        };
        // Exactly what the store holds for it.
        client.Leads["Point"] = "Point or points may refer to:";

        var topic = new WikipediaTopicRecognizer(client).Recognize(new[] { "Point" });

        // A page that only lists other pages is never a domain, so recognizing nothing is the
        // honest answer -- the pipeline falls back to the seed-only model.
        Assert.False(topic.IsRecognized);
    }

    /// <summary>
    /// The real "System" failure. "a system to manage appointment schedules for a clinic" seeds the
    /// word system, which resolves exactly to <em>System</em> — an article about systems theory. It
    /// tied with every other seed's exact hit and won on being first, and the domain came out as a
    /// single class called Structure. EntityShaper already refuses System as a CLASS; a thing that
    /// cannot be a class certainly cannot be the domain.
    /// </summary>
    [Fact]
    public void Recognize_RefusesATopicWhoseOwnDefinitionIsAnAbstraction()
    {
        var client = new FakeWikipediaClient();
        client.HitsByTerm["system"] = new()
        {
            new SearchHit("System", "https://en.wikipedia.org/wiki/System", 0),
        };
        client.HitsByTerm["clinic"] = new()
        {
            new SearchHit("Clinic", "https://en.wikipedia.org/wiki/Clinic", 0),
        };
        client.Leads["System"] = "A system is a group of interacting or interrelated elements that act according to a set of rules.";
        client.Leads["Clinic"] = "A clinic is a health facility that is primarily focused on the care of outpatients.";

        var topic = new WikipediaTopicRecognizer(client).Recognize(new[] { "system", "clinic" });

        Assert.Equal("Clinic", topic.Title);
    }

    /// <summary>
    /// And the limit of that rule. "A library is a collection of books" — whose head, "collection",
    /// is an abstraction — yet a library is exactly the sort of thing people build apps about, and
    /// DomainFieldLexicon says so. Curated knowledge outranks the archetype here for the same reason
    /// it does in EntityShaper, which is what keeps Library a class today.
    /// </summary>
    [Fact]
    public void Recognize_KeepsAnAbstractlyDefinedTopic_TheCuratedLexiconKnows()
    {
        var client = new FakeWikipediaClient();
        client.HitsByTerm["library"] = new()
        {
            new SearchHit("Library", "https://en.wikipedia.org/wiki/Library", 0),
        };
        client.Leads["Library"] =
            "A library is a collection of books, and possibly other materials and media.";

        var topic = new WikipediaTopicRecognizer(client).Recognize(new[] { "library" });

        Assert.Equal("Library", topic.Title);
    }

    [Fact]
    public void Recognize_KeepsAnArticleWhoseLeadMerelyMentionsWhatItRefersTo()
    {
        var client = new FakeWikipediaClient();
        client.HitsByTerm["Sale"] = new()
        {
            new SearchHit("Point of sale", "https://en.wikipedia.org/wiki/Point_of_sale", 0),
        };
        client.Leads["Point of sale"] =
            "The point of sale (POS) is the time and place at which a retail transaction is completed.";

        var topic = new WikipediaTopicRecognizer(client).Recognize(new[] { "Sale" });

        Assert.Equal("Point of sale", topic.Title);
    }
}

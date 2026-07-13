using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class DumpWikipediaClientTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"wikiclient-{Guid.NewGuid():N}.db");
    private readonly DumpWikipediaClient _client;

    public DumpWikipediaClientTests()
    {
        var store = new WikipediaStore(_path);
        store.CreateSchema();
        store.WriteArticles([
            new ArticleRow("Point of sale", "A point of sale is where a sale is completed.",
                "A point of sale is where a sale is completed.\nThe customer pays the merchant.",
                ["Cash register", "Receipt"]),
            new ArticleRow("Receipt", "A receipt is a document acknowledging payment.",
                "A receipt is a document acknowledging payment.", [])]);
        store.WriteRedirects([("POS", "Point of sale")]);

        _client = new DumpWikipediaClient(store);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void Search_finds_an_article_by_its_exact_title()
    {
        var hits = _client.Search("Receipt");

        Assert.Single(hits);
        Assert.Equal("Receipt", hits[0].Title);
    }

    [Fact]
    public void Search_resolves_a_redirect_to_its_target()
    {
        var hits = _client.Search("POS");

        Assert.Single(hits);
        Assert.Equal("Point of sale", hits[0].Title);
    }

    [Fact]
    public void Search_is_empty_for_an_unknown_term()
    {
        Assert.Empty(_client.Search("Nonexistent topic"));
    }

    [Fact]
    public void FetchExtract_returns_the_whole_body()
    {
        Assert.Contains("The customer pays the merchant.", _client.FetchExtract("Point of sale"));
    }

    [Fact]
    public void FetchExtract_is_empty_for_an_unknown_title()
    {
        Assert.Equal(string.Empty, _client.FetchExtract("Nonexistent"));
    }

    [Fact]
    public void FetchLinks_returns_the_articles_outgoing_links()
    {
        Assert.Equal(new[] { "Cash register", "Receipt" }, _client.FetchLinks("Point of sale"));
    }

    [Fact]
    public void FetchLead_returns_the_first_sentence()
    {
        Assert.Equal(
            "A receipt is a document acknowledging payment.", _client.FetchLead("Receipt"));
    }

    [Fact]
    public void FetchLeads_returns_a_lead_per_known_title_and_omits_the_unknown()
    {
        var leads = _client.FetchLeads(["Receipt", "Nonexistent"]);

        Assert.True(leads.ContainsKey("Receipt"));
        Assert.False(leads.ContainsKey("Nonexistent"));
    }

    [Fact]
    public void FetchLeads_is_keyed_by_the_title_the_caller_asked_for_not_the_redirect_target()
    {
        var leads = _client.FetchLeads(["POS"]);

        Assert.True(leads.ContainsKey("POS"));
    }
}

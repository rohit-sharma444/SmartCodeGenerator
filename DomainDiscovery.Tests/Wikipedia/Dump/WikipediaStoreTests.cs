using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

[Collection(SqliteStoreCollection.Name)]
public sealed class WikipediaStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"wikistore-{Guid.NewGuid():N}.db");

    private WikipediaStore NewStore()
    {
        var store = new WikipediaStore(_path);
        store.CreateSchema();
        return store;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void Reads_back_an_article_it_wrote()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Invoice", "An invoice is a document.",
            "An invoice is a document.\n* invoice number", ["Receipt"])]);

        var row = store.Read("Invoice");

        Assert.NotNull(row);
        Assert.Equal("An invoice is a document.", row!.Lead);
        Assert.Contains("* invoice number", row.Body);
        Assert.Equal(new[] { "Receipt" }, row.Links);
    }

    [Fact]
    public void Read_is_case_insensitive_on_the_title()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Point of sale", "A point of sale.", "Body.", [])]);

        Assert.NotNull(store.Read("point of sale"));
    }

    [Fact]
    public void Read_follows_a_redirect_to_the_target_article()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Point of sale", "A point of sale.", "Body.", [])]);
        store.WriteRedirects([("POS", "Point of sale")]);

        var row = store.Read("POS");

        Assert.NotNull(row);
        Assert.Equal("Point of sale", row!.Title);
    }

    [Fact]
    public void Read_is_null_for_an_unknown_title()
    {
        Assert.Null(NewStore().Read("Nonexistent"));
    }

    [Fact]
    public void ResolveTitle_prefers_an_exact_match_over_a_prefix_match()
    {
        var store = NewStore();
        store.WriteArticles([
            new ArticleRow("Payment", "A payment.", "Body.", []),
            new ArticleRow("Payment terminal", "A terminal.", "Body.", [])]);

        Assert.Equal("Payment", store.ResolveTitle("Payment"));
    }

    [Fact]
    public void ResolveTitle_falls_back_to_a_prefix_match()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Payment terminal", "A terminal.", "Body.", [])]);

        Assert.Equal("Payment terminal", store.ResolveTitle("Payment ter"));
    }

    [Fact]
    public void Remembers_the_last_page_id_so_an_import_can_resume()
    {
        var store = NewStore();
        Assert.Equal(0, store.GetLastPageId());

        store.SetLastPageId(4242);

        Assert.Equal(4242, store.GetLastPageId());
    }
}

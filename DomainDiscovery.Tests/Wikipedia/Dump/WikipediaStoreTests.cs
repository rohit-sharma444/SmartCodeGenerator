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

    // The prefix match has to rank the way MediaWiki's opensearch did, or the pipeline drowns.
    // Measured against the real store: ordering by title length answers "Point" with "Pointer",
    // "Pointcut", "Point Air" and never surfaces "Point of sale" at all, while opensearch returns
    // it at rank 2. Article size is the local stand-in for the importance opensearch ranks by.
    [Fact]
    public void PrefixSearch_ranks_the_substantial_article_above_the_obscure_one()
    {
        var store = NewStore();
        store.WriteArticles([
            new ArticleRow("Point Air", "A tiny airline.", "A stub.", []),
            new ArticleRow("Point of sale", "The point of sale is where a sale is completed.",
                new string('x', 5000), [])]);

        Assert.Equal("Point of sale", store.PrefixSearch("Point", 5)[0]);
    }

    // The title index is NOT built during the import -- that is the whole point of the schema.
    // It is built once at the end, so the store has to answer correctly either side of that.
    [Fact]
    public void Reads_work_before_and_after_the_search_index_is_built()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Invoice", "An invoice.", "Body.", [])]);
        store.WriteRedirects([("Bill", "Invoice")]);

        Assert.False(store.HasSearchIndexes());
        Assert.NotNull(store.Read("invoice"));
        Assert.Equal("Invoice", store.Read("Bill")!.Title);

        store.CreateSearchIndexes();

        Assert.True(store.HasSearchIndexes());
        Assert.NotNull(store.Read("invoice"));
        Assert.Equal("Invoice", store.Read("Bill")!.Title);
        Assert.Equal("Invoice", store.PrefixSearch("Inv", 5).Single());
    }

    [Fact]
    public void Building_the_search_index_twice_is_harmless()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Invoice", "An invoice.", "Body.", [])]);

        store.CreateSearchIndexes();
        store.CreateSearchIndexes();

        Assert.True(store.HasSearchIndexes());
        Assert.NotNull(store.Read("Invoice"));
    }

    // Wikipedia titles are case-sensitive after the first character, so "AIDS" and "Aids" are
    // different articles. The lookup is deliberately case-INsensitive, so both land and the first
    // wins -- what must never happen is that the index build trips over them, which is exactly what
    // a UNIQUE index on a NOCASE column would do.
    [Fact]
    public void Two_titles_differing_only_by_case_do_not_break_the_index_build()
    {
        var store = NewStore();
        store.WriteArticles([
            new ArticleRow("AIDS", "A disease.", "Body.", []),
            new ArticleRow("Aids", "A helper.", "Body.", [])]);

        store.CreateSearchIndexes();

        Assert.NotNull(store.Read("aids"));
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

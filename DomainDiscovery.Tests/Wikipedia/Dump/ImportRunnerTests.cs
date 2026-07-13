using DomainDiscovery.Wikipedia.Dump;
using WikipediaDumpImporter;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

[Collection(SqliteStoreCollection.Name)]
public sealed class ImportRunnerTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"wikiimport-{Guid.NewGuid():N}.db");
    private readonly WikipediaStore _store;

    public ImportRunnerTests()
    {
        _store = new WikipediaStore(_path);
        _store.CreateSchema();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private static IEnumerable<RawPage> Fixture()
    {
        using var stream = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-dump.xml"));
        return DumpPageReader.ReadXml(stream).ToList();
    }

    [Fact]
    public void Imports_the_mainspace_article_and_skips_the_talk_page()
    {
        var stats = new ImportRunner(_store).Run(Fixture());

        Assert.Equal(1, stats.Articles);
        Assert.Equal(1, stats.Redirects);
        Assert.Equal(1, stats.Skipped);
    }

    [Fact]
    public void The_imported_article_is_stripped_to_prose()
    {
        new ImportRunner(_store).Run(Fixture());

        var row = _store.Read("Invoice");

        Assert.NotNull(row);
        Assert.Equal("An invoice is a commercial document.", row!.Lead);
        Assert.DoesNotContain("'''", row.Body);
    }

    [Fact]
    public void The_imported_article_keeps_its_list_markers()
    {
        new ImportRunner(_store).Run(Fixture());

        Assert.Contains("* invoice number", _store.Read("Invoice")!.Body);
    }

    [Fact]
    public void The_imported_article_keeps_its_links()
    {
        new ImportRunner(_store).Run(Fixture());

        Assert.Equal(new[] { "commercial document" }, _store.Read("Invoice")!.Links);
    }

    [Fact]
    public void A_redirect_resolves_to_its_target_after_import()
    {
        new ImportRunner(_store).Run(Fixture());

        Assert.Equal("Invoice", _store.Read("Bill (payment)")!.Title);
    }

    [Fact]
    public void Records_the_last_page_id_so_a_rerun_can_resume()
    {
        new ImportRunner(_store).Run(Fixture());

        Assert.Equal(103, _store.GetLastPageId());
    }

    [Fact]
    public void A_resumed_run_skips_pages_already_committed()
    {
        _store.SetLastPageId(101);

        var stats = new ImportRunner(_store).Run(Fixture());

        // 101 (Invoice) is already in; only the redirect and the talk page are still to come.
        Assert.Equal(0, stats.Articles);
        Assert.Equal(1, stats.Redirects);
    }

    // The strip runs in parallel across cores, so the batch has to be bigger than one flush to
    // exercise it at all: every page must still land, and the resume point must still end up at
    // the LAST id, not merely the highest one some worker happened to finish with.
    [Fact]
    public void Every_page_of_a_large_run_lands_and_the_resume_point_is_the_final_id()
    {
        var pages = Enumerable.Range(1, 5000)
            .Select(i => new RawPage(i, $"Article {i}", 0, null, $"Article {i} is a thing."))
            .ToList();

        var stats = new ImportRunner(_store).Run(pages);

        Assert.Equal(5000, stats.Articles);
        Assert.Equal(5000, _store.ArticleCount);
        Assert.Equal(5000, _store.GetLastPageId());
        Assert.NotNull(_store.Read("Article 1"));
        Assert.NotNull(_store.Read("Article 2500"));
        Assert.NotNull(_store.Read("Article 5000"));
    }

    [Fact]
    public void A_page_with_unparseable_wikitext_does_not_stop_the_import()
    {
        var pages = new[]
        {
            new RawPage(1, "Broken", 0, null, "{{unclosed [[also unclosed"),
            new RawPage(2, "Good", 0, null, "A good article.")
        };

        var stats = new ImportRunner(_store).Run(pages);

        // The page after the broken one still lands: one bad page cannot abort the run.
        Assert.NotNull(_store.Read("Good"));

        // "Broken" is an unclosed template, so it strips to no prose at all and is skipped
        // rather than stored -- an empty body would only make FetchExtract return "".
        Assert.Equal(1, stats.Articles);
        Assert.Equal(1, stats.Skipped);
        Assert.Null(_store.Read("Broken"));
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

[Collection(SqliteStoreCollection.Name)]
public sealed class ArticleRefresherTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"refresh-{Guid.NewGuid():N}.db");

    private WikipediaStore NewStore(int? dumpDate = 20260701)
    {
        var store = new WikipediaStore(_path);
        store.CreateSchema();
        store.CreateSearchIndexes();
        if (dumpDate is { } date)
            store.SetDumpDate(date);
        return store;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    /// <summary>Answers the timestamp query, then the content query. Records both.</summary>
    private sealed class ApiHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Edited, string Wikitext)> _live;

        public ApiHandler(Dictionary<string, (string Edited, string Wikitext)> live) => _live = live;

        public List<string> ContentRequests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            var titles = query.Split("titles=")[1].Split('&')[0].Split('|');
            var wantsContent = query.Contains("rvprop=content");

            var pages = titles
                .Where(_live.ContainsKey)
                .Select(object (t) => wantsContent
                    ? new
                    {
                        title = t,
                        revisions = new[]
                        {
                            new { slots = new { main = new { content = _live[t].Wikitext } } }
                        }
                    }
                    : new { title = t, revisions = new[] { new { timestamp = _live[t].Edited } } });

            if (wantsContent)
                ContentRequests.AddRange(titles);

            var json = JsonSerializer.Serialize(new { query = new { pages } });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("no network");
    }

    [Fact]
    public void Refresh_RewritesOnlyTheArticlesEditedSinceTheDumpWasCut()
    {
        var store = NewStore();
        store.WriteArticles([
            new ArticleRow("Receipt", "Old lead.", "Old body.", []),
            new ArticleRow("Customer", "A customer buys.", "A customer buys things.", []),
        ]);

        var handler = new ApiHandler(new()
        {
            // Edited after the cut, so it must be fetched again.
            ["Receipt"] = ("2026-07-08T10:00:00Z", "A receipt records a [[Sale]]. It is new."),
            // Untouched for a year. Fetching it would be a wasted round trip.
            ["Customer"] = ("2025-05-15T10:00:00Z", "IGNORED"),
        });

        var refresher = new ArticleRefresher(store, new HttpClient(handler));

        refresher.Refresh(["Receipt", "Customer"]);

        Assert.Equal(1, refresher.LastRefreshed);
        Assert.Equal(new[] { "Receipt" }, handler.ContentRequests);

        var receipt = store.Read("Receipt");
        Assert.Contains("It is new", receipt!.Body);
        Assert.Equal(new[] { "Sale" }, receipt.Links);   // wikitext was stripped, not stored raw

        // Left exactly as the dump had it.
        Assert.Equal("A customer buys things.", store.Read("Customer")!.Body);
    }

    /// <summary>
    /// The table has no unique constraint on title (bulk import cannot afford one), so an insert
    /// alone would leave the stale revision sitting beside the new one, and Read takes the first row
    /// it finds.
    /// </summary>
    [Fact]
    public void Refresh_ReplacesTheStaleRow_RatherThanAddingASecondOne()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Receipt", "Old.", "Old body.", [])]);

        var refresher = new ArticleRefresher(store, new HttpClient(new ApiHandler(new()
        {
            ["Receipt"] = ("2026-07-08T10:00:00Z", "New body."),
        })));

        refresher.Refresh(["Receipt"]);

        Assert.Equal(1, store.ArticleCount);
        Assert.Equal("New body.", store.Read("Receipt")!.Body);
    }

    /// <summary>
    /// The store's dump date never moves, so comparing every article against it would find the same
    /// ones "edited since the dump" on every run and fetch them AGAIN, having already fetched them.
    /// What the store holds after a refresh is a REVISION, and that is what the next run must compare
    /// against.
    /// </summary>
    [Fact]
    public void Refresh_DoesNotFetchTheSameArticleTwice_WhenNothingHasChangedSince()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Receipt", "Old.", "Old body.", [])]);

        var handler = new ApiHandler(new()
        {
            ["Receipt"] = ("2026-07-08T10:00:00Z", "New body."),
        });
        var refresher = new ArticleRefresher(store, new HttpClient(handler));

        refresher.Refresh(["Receipt"]);
        Assert.Equal(1, refresher.LastRefreshed);
        Assert.Equal(new[] { "Receipt" }, handler.ContentRequests);

        // Same article, same revision, second run: the timestamp is still checked (one cheap call),
        // but nothing is downloaded.
        refresher.Refresh(["Receipt"]);

        Assert.Equal(0, refresher.LastRefreshed);
        Assert.Equal(new[] { "Receipt" }, handler.ContentRequests);   // still just the one fetch
    }

    [Fact]
    public void Refresh_FetchesAgain_WhenTheArticleHasBeenEditedSinceTheLastRefresh()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Receipt", "Old.", "Old body.", [])]);

        var live = new Dictionary<string, (string Edited, string Wikitext)>
        {
            ["Receipt"] = ("2026-07-08T10:00:00Z", "Second body."),
        };
        var handler = new ApiHandler(live);
        var refresher = new ArticleRefresher(store, new HttpClient(handler));

        refresher.Refresh(["Receipt"]);

        // Somebody edits it again.
        live["Receipt"] = ("2026-07-20T09:00:00Z", "Third body.");

        refresher.Refresh(["Receipt"]);

        Assert.Equal(1, refresher.LastRefreshed);
        Assert.Equal("Third body.", store.Read("Receipt")!.Body);
    }

    /// <summary>
    /// Running offline against a stale dump is a worse answer than running online against a fresh
    /// one, and a far better answer than not running.
    /// </summary>
    [Fact]
    public void Refresh_LeavesTheStoreAlone_WhenThereIsNoNetwork()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Receipt", "Old.", "Old body.", [])]);

        var refresher = new ArticleRefresher(store, new HttpClient(new OfflineHandler()));

        refresher.Refresh(["Receipt"]);   // must not throw

        Assert.Equal(0, refresher.LastRefreshed);
        Assert.Equal("Old body.", store.Read("Receipt")!.Body);
    }

    /// <summary>
    /// Without a dump date there is no "since", and refreshing everything unconditionally would be a
    /// fetch per article on every run — the rate limit the dump exists to escape.
    /// </summary>
    [Fact]
    public void Refresh_DoesNothing_ForAStoreThatCannotSayHowOldItIs()
    {
        var store = NewStore(dumpDate: null);
        store.WriteArticles([new ArticleRow("Receipt", "Old.", "Old body.", [])]);

        var handler = new ApiHandler(new()
        {
            ["Receipt"] = ("2026-07-08T10:00:00Z", "New body."),
        });

        new ArticleRefresher(store, new HttpClient(handler)).Refresh(["Receipt"]);

        Assert.Empty(handler.ContentRequests);
        Assert.Equal("Old body.", store.Read("Receipt")!.Body);
    }

    [Fact]
    public void Refresh_KeepsTheOldArticle_WhenTheNewOneStripsToNothing()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Receipt", "Good.", "Good body.", [])]);

        var refresher = new ArticleRefresher(store, new HttpClient(new ApiHandler(new()
        {
            // A page that is nothing but a template strips to empty. Overwriting a good row with an
            // empty one is worse than being out of date.
            ["Receipt"] = ("2026-07-08T10:00:00Z", "{{stub}}"),
        })));

        refresher.Refresh(["Receipt"]);

        Assert.Equal(0, refresher.LastRefreshed);
        Assert.Equal("Good body.", store.Read("Receipt")!.Body);
    }
}

using System.Net;
using System.Text;
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

[Collection(SqliteStoreCollection.Name)]
public sealed class DumpFreshnessTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"freshness-{Guid.NewGuid():N}.db");

    private WikipediaStore NewStore(int? dumpDate)
    {
        var store = new WikipediaStore(_path);
        store.CreateSchema();
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

    /// <summary>The shape Wikimedia's directory index really has.</summary>
    private static string Index(params int[] days)
        => string.Join('\n', days.Select(d =>
            $"<a href=\"{d}/\">{d}/</a>                    14-Jul-2026 09:12    -"));

    private sealed class CannedHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/html")
            });
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("no network");
    }

    [Fact]
    public void Check_ListsOnlyTheIncrementsPublishedAfterTheStoresDump()
    {
        var store = NewStore(20260707);
        var freshness = new DumpFreshness(
            new HttpClient(new CannedHandler(Index(20260706, 20260707, 20260708, 20260709))));

        var staleness = freshness.Check(store);

        Assert.Equal(20260707, staleness.StoreDate);
        Assert.Equal(new[] { 20260708, 20260709 }, staleness.PendingDays);
        Assert.False(staleness.IsCurrent);
    }

    [Fact]
    public void Check_SaysNothingIsPending_WhenTheStoreIsCurrent()
    {
        var store = NewStore(20260709);
        var freshness = new DumpFreshness(
            new HttpClient(new CannedHandler(Index(20260707, 20260708, 20260709))));

        Assert.True(freshness.Check(store).IsCurrent);
    }

    /// <summary>
    /// Being unable to reach Wikimedia is the NORMAL case for this app — the whole point of the dump
    /// is that discovery runs offline. A failed check must not throw, must not fail startup, and
    /// above all MUST NOT SOUND LIKE GOOD NEWS. The first version of this reported a twelve-day-old
    /// store as "up to date" because Wikimedia had refused the request (403, no User-Agent) and the
    /// empty result was read as "nothing pending".
    /// </summary>
    [Fact]
    public void Check_DoesNotClaimTheStoreIsCurrent_WhenItCouldNotAsk()
    {
        var store = NewStore(20260707);
        var freshness = new DumpFreshness(new HttpClient(new OfflineHandler()));

        var staleness = freshness.Check(store);

        Assert.False(staleness.Answered);
        Assert.False(staleness.IsCurrent);   // the whole point: silence is not freshness
        Assert.False(staleness.IsStale);
        Assert.Equal(20260707, staleness.StoreDate);
    }

    [Fact]
    public void Check_SaysUnknown_ForAStoreTooOldToKnowItsOwnDate()
    {
        var store = NewStore(dumpDate: null);
        var freshness = new DumpFreshness(
            new HttpClient(new CannedHandler(Index(20260708, 20260709))));

        var staleness = freshness.Check(store);

        Assert.Null(staleness.StoreDate);
        Assert.False(staleness.Answered);
    }

    /// <summary>An error page served as HTTP 200 is not an index, and knows nothing.</summary>
    [Fact]
    public void Check_DoesNotTrustABodyWithNoDatesInIt()
    {
        var store = NewStore(20260707);
        var freshness = new DumpFreshness(
            new HttpClient(new CannedHandler("<html>503 Service Unavailable</html>")));

        var staleness = freshness.Check(store);

        Assert.False(staleness.Answered);
        Assert.False(staleness.IsCurrent);
    }

    [Fact]
    public void PendingDays_CarryTheirDownloadCost()
    {
        var store = NewStore(20260701);
        var freshness = new DumpFreshness(
            new HttpClient(new CannedHandler(Index(20260702, 20260703, 20260704))));

        var staleness = freshness.Check(store);

        // ~780 MB apiece, measured against the real feed — the number that makes auto-applying
        // these at startup indefensible.
        Assert.Equal(3 * DumpStaleness.ApproximateDailyGigabytes, staleness.ApproximateGigabytes, 3);
    }
}

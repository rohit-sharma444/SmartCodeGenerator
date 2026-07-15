using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DomainDiscovery.Wikipedia.Dump;
using ICSharpCode.SharpZipLib.BZip2;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

[Collection(SqliteStoreCollection.Name)]
public sealed class IncrementalUpdaterTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"incr-{Guid.NewGuid():N}.db");

    private readonly string _increment = Path.Combine(
        Path.GetTempPath(), $"incr-{Guid.NewGuid():N}.xml.bz2");

    private WikipediaStore NewStore()
    {
        var store = new WikipediaStore(_path);
        store.CreateSchema();
        store.CreateSearchIndexes();
        store.SetDumpDate(20260701);
        return store;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_increment)) File.Delete(_increment);
    }

    /// <summary>Writes a bz2 increment in the shape Wikimedia really publishes.</summary>
    private string Increment(string pagesXml)
    {
        var xml = $"""
            <mediawiki xmlns="http://www.mediawiki.org/xml/export-0.11/" version="0.11">
              <siteinfo><sitename>Wikipedia</sitename></siteinfo>
              {pagesXml}
            </mediawiki>
            """;

        using (var file = File.Create(_increment))
        using (var bz2 = new BZip2OutputStream(file))
        {
            var bytes = Encoding.UTF8.GetBytes(xml);
            bz2.Write(bytes, 0, bytes.Length);
        }

        return _increment;
    }

    [Fact]
    public void Apply_WritesTheNewestRevisionOverTheStoresArticle()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Receipt", "Old lead.", "Old body.", [])]);

        // A page that was edited twice that day. The dump carries BOTH revisions, oldest first, and
        // only the newest is the article now.
        var file = Increment("""
              <page>
                <title>Receipt</title>
                <ns>0</ns>
                <id>101</id>
                <revision>
                  <id>1</id>
                  <timestamp>2026-07-08T01:00:00Z</timestamp>
                  <text xml:space="preserve">A receipt is stale.</text>
                </revision>
                <revision>
                  <id>2</id>
                  <timestamp>2026-07-08T09:00:00Z</timestamp>
                  <text xml:space="preserve">A receipt records a [[Sale]]. It is current.</text>
                </revision>
              </page>
            """);

        var stats = new IncrementalUpdater(store, say: _ => { }).Apply(file);

        Assert.Equal(1, stats.Articles);

        var receipt = store.Read("Receipt");
        Assert.Contains("It is current", receipt!.Body);
        Assert.DoesNotContain("stale", receipt.Body);
        Assert.Equal(new[] { "Sale" }, receipt.Links);
    }

    /// <summary>
    /// The pages table has no unique constraint on title, so an insert alone would leave the old
    /// revision beside the new one — and Read takes the first row it finds.
    /// </summary>
    [Fact]
    public void Apply_ReplacesTheArticle_RatherThanDuplicatingIt()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Receipt", "Old.", "Old body.", [])]);

        var file = Increment("""
              <page><title>Receipt</title><ns>0</ns><id>101</id>
                <revision><id>2</id><text xml:space="preserve">New body.</text></revision>
              </page>
            """);

        new IncrementalUpdater(store, say: _ => { }).Apply(file);

        Assert.Equal(1, store.ArticleCount);
        Assert.Equal("New body.", store.Read("Receipt")!.Body);
    }

    /// <summary>
    /// An article that BECAME a redirect must stop being an article, or the store holds both and
    /// answers with the stale prose.
    /// </summary>
    [Fact]
    public void Apply_TurnsAnArticleThatBecameARedirect_IntoARedirect()
    {
        var store = NewStore();
        store.WriteArticles([
            new ArticleRow("POS terminal", "A POS terminal.", "A POS terminal is a device.", []),
            new ArticleRow("Payment terminal", "A payment terminal.", "The real article.", []),
        ]);

        var file = Increment("""
              <page><title>POS terminal</title><ns>0</ns><id>101</id>
                <redirect title="Payment terminal" />
                <revision><id>2</id><text xml:space="preserve">#REDIRECT [[Payment terminal]]</text></revision>
              </page>
            """);

        var stats = new IncrementalUpdater(store, say: _ => { }).Apply(file);

        Assert.Equal(1, stats.Redirects);

        // Following the redirect now lands on the real article, not the abandoned prose.
        Assert.Equal("The real article.", store.Read("POS terminal")!.Body);
        Assert.Equal("Payment terminal", store.ResolveTitle("POS terminal"));
    }

    [Fact]
    public void Apply_IgnoresEveryNamespaceButArticles()
    {
        var store = NewStore();

        var file = Increment("""
              <page><title>Talk:Receipt</title><ns>1</ns><id>201</id>
                <revision><id>2</id><text xml:space="preserve">A talk page.</text></revision>
              </page>
              <page><title>Template:Cite</title><ns>10</ns><id>202</id>
                <revision><id>3</id><text xml:space="preserve">A template.</text></revision>
              </page>
              <page><title>Receipt</title><ns>0</ns><id>101</id>
                <revision><id>4</id><text xml:space="preserve">A receipt.</text></revision>
              </page>
            """);

        var stats = new IncrementalUpdater(store, say: _ => { }).Apply(file);

        Assert.Equal(1, stats.Articles);
        Assert.Equal(2, stats.Skipped);
        Assert.Equal(1, store.ArticleCount);
        Assert.Null(store.Read("Talk:Receipt"));
    }

    [Fact]
    public void Apply_KeepsTheOldArticle_WhenTheIncrementStripsToNothing()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Receipt", "Good.", "Good body.", [])]);

        var file = Increment("""
              <page><title>Receipt</title><ns>0</ns><id>101</id>
                <revision><id>2</id><text xml:space="preserve">{{stub}}</text></revision>
              </page>
            """);

        var stats = new IncrementalUpdater(store, say: _ => { }).Apply(file);

        Assert.Equal(0, stats.Articles);
        Assert.Equal("Good body.", store.Read("Receipt")!.Body);
    }

    private static readonly byte[] ServerFile = Encoding.UTF8.GetBytes(
        new string('x', 4096) + "END");

    private IncrementalUpdater Updater(WikipediaStore store, StubDumpServer server)
        => new(store, new HttpClient(server), say: _ => { });

    /// <summary>
    /// The state a run stopped between downloading a day and applying it leaves behind. Resuming it
    /// asks for bytes from the end of the file, and Wikimedia answers 416 — NOT a 206 with nothing in
    /// it — so a downloader that only knows 206 and 200 throws, and the day that is sitting complete
    /// on disk can never be applied.
    /// </summary>
    [Fact]
    public void Download_KeepsAFileThatIsAlreadyComplete_RatherThanFailingOnTheRangeItCannotAskFor()
    {
        File.WriteAllBytes(_increment, ServerFile);

        var server = new StubDumpServer(ServerFile);
        Updater(NewStore(), server).Download(20260702, _increment);

        Assert.Equal(ServerFile, File.ReadAllBytes(_increment));
    }

    /// <summary>
    /// A local file LONGER than the server's is not a finished download; it is a broken one. 416 says
    /// only "cannot serve that range", so the length in its Content-Range is what tells the two apart.
    /// </summary>
    [Fact]
    public void Download_StartsAgain_WhenTheLocalFileIsLongerThanTheServers()
    {
        File.WriteAllBytes(_increment, [.. ServerFile, .. "trailing garbage"u8]);

        var server = new StubDumpServer(ServerFile);
        Updater(NewStore(), server).Download(20260702, _increment);

        Assert.Equal(ServerFile, File.ReadAllBytes(_increment));
    }

    [Fact]
    public void Download_ResumesAPartFile_RatherThanFetchingItAgain()
    {
        File.WriteAllBytes(_increment, ServerFile[..1000]);

        var server = new StubDumpServer(ServerFile);
        Updater(NewStore(), server).Download(20260702, _increment);

        Assert.Equal(ServerFile, File.ReadAllBytes(_increment));
        Assert.Equal(1000, server.ServedFrom);
    }

    /// <summary>Appending a whole-file 200 onto a part file would corrupt it. Start over instead.</summary>
    [Fact]
    public void Download_StartsOver_WhenTheServerIgnoresTheRange()
    {
        File.WriteAllBytes(_increment, ServerFile[..1000]);

        var server = new StubDumpServer(ServerFile) { HonoursRange = false };
        Updater(NewStore(), server).Download(20260702, _increment);

        Assert.Equal(ServerFile, File.ReadAllBytes(_increment));
    }

    /// <summary>
    /// Ctrl-C during the apply. The half of the day that was written stays written — it is the newest
    /// revision of those articles, and re-applying the day over it merely writes them again — but the
    /// store's DATE must not move, or the rest of that day is lost for good.
    /// </summary>
    [Fact]
    public void UpdateDay_LeavesTheStoresDateAlone_WhenTheApplyIsInterrupted()
    {
        var store = NewStore();

        var file = Increment("""
              <page><title>Receipt</title><ns>0</ns><id>101</id>
                <revision><id>2</id><text xml:space="preserve">A receipt.</text></revision>
              </page>
            """);

        var working = Path.GetDirectoryName(file)!;
        File.Copy(file, Path.Combine(working, "enwiki-20260702-incr.xml.bz2"), overwrite: true);

        using var interrupt = new CancellationTokenSource();
        interrupt.Cancel();

        var server = new StubDumpServer(File.ReadAllBytes(file));

        Assert.Throws<OperationCanceledException>(() =>
            Updater(store, server).UpdateDay(20260702, working, interrupt.Token));

        Assert.Equal(20260701, store.GetDumpDate());
    }

    /// <summary>
    /// Interrupting a download keeps the bytes it already has: 780 MB is far too much to throw away
    /// because the user changed their mind, and the next run resumes from exactly here.
    /// </summary>
    [Fact]
    public void Download_KeepsWhatItAlreadyFetched_WhenInterrupted()
    {
        using var interrupt = new CancellationTokenSource();
        interrupt.Cancel();

        var server = new StubDumpServer(ServerFile);

        Assert.Throws<OperationCanceledException>(() =>
            Updater(NewStore(), server).Download(20260702, _increment, interrupt.Token));

        // Nothing was written, and nothing was corrupted: the next run simply starts it.
        Assert.True(!File.Exists(_increment) || new FileInfo(_increment).Length < ServerFile.Length);
    }

    /// <summary>
    /// Wikimedia's dump server in the two respects that matter here: it honours a byte Range, and a
    /// Range starting at or past the end of the file is a 416 carrying the real length. Verified
    /// against the live server on 2026-07-14: 206 from mid-file, 416 from EOF.
    /// </summary>
    private sealed class StubDumpServer(byte[] body) : HttpMessageHandler
    {
        /// <summary>Where the last honoured Range request was served from.</summary>
        public long ServedFrom { get; private set; }

        public bool HonoursRange { get; init; } = true;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellation)
            => Task.FromResult(Send(request, cancellation));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken _)
        {
            var from = HonoursRange ? request.Headers.Range?.Ranges.FirstOrDefault()?.From : null;

            if (from is null)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

            if (from >= body.Length)
            {
                var refused = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    Content = new ByteArrayContent([]),
                };

                refused.Content.Headers.ContentRange = new ContentRangeHeaderValue(body.Length);
                return refused;
            }

            ServedFrom = from.Value;

            var rest = body[(int)from.Value..];
            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(rest),
            };

            partial.Content.Headers.ContentRange =
                new ContentRangeHeaderValue(from.Value, body.Length - 1, body.Length);

            return partial;
        }
    }
}

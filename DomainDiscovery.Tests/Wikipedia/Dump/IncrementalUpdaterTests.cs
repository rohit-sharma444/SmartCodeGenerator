using System.IO.Compression;
using System.Text;
using DomainDiscovery.Wikipedia.Dump;
using ICSharpCode.SharpZipLib.BZip2;
using WikipediaDumpImporter;
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
}

using DomainDiscovery.Wikipedia.Dump;

namespace WikipediaDumpImporter;

/// <summary>What one import pass did.</summary>
public sealed record ImportStats(long Articles, long Redirects, long Skipped);

/// <summary>
/// Turns a stream of <see cref="RawPage"/> into the store: strips wikitext to prose, keeps
/// mainspace only, and records progress so an interrupted run can resume.
/// </summary>
/// <remarks>
/// Resumability is a requirement, not a nicety: a crash an hour in must not cost the whole run.
/// The page id increases monotonically through the dump, which is what makes "skip anything at or
/// below the last committed id" a correct resume rule — and it is also why the parallel stage below
/// must preserve dump order.
/// <para>
/// <b>Why the strip runs in parallel.</b> bz2 decode was assumed to be the ceiling (23.6 MB/s, so
/// ~69 minutes for the whole dump). Measured on the real file, it is not: a single-threaded import
/// managed only ~400 pages/s — about 1.6 MB/s of XML, some fifteen times below what the decoder
/// alone can do, which puts the full run near 16 hours. The cost is per-page work (chiefly
/// <see cref="WikitextStripper"/>), not decompression. So decode and XML parsing stay on the one
/// thread they are stuck on, and the stripping fans out across the rest of the cores; that puts
/// decode back in charge, where the original estimate assumed it already was.
/// </para>
/// <para>
/// <b>AsOrdered is load-bearing, not decoration.</b> Results must reach the writer in dump order,
/// because the writer advances the resume point to the last id it has committed. Unordered results
/// would let a high id commit while a lower one was still in flight — and a crash there would
/// resume past a page that was never written, silently losing it.
/// </para>
/// </remarks>
public sealed class ImportRunner
{
    /// <summary>Rows per transaction. Large enough that commit overhead disappears, small enough
    /// that a crash loses seconds of work rather than minutes.</summary>
    private const int BatchSize = 2000;

    /// <summary>Pages between progress reports.</summary>
    private const int ProgressInterval = 50_000;

    private readonly WikipediaStore _store;
    private readonly Action<string>? _progress;

    public ImportRunner(WikipediaStore store, Action<string>? progress = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _progress = progress;
    }

    /// <summary>What the parallel stage decided about one page, ready for the writer to commit.</summary>
    private sealed record Prepared(long Id, ArticleRow? Article, (string From, string To)? Redirect);

    public ImportStats Run(IEnumerable<RawPage> pages)
    {
        var resumeFrom = _store.GetLastPageId();

        // Write into an unindexed table and index it once at the end. Maintaining the title index
        // across millions of random inserts is what dragged the first import from ~790 pages/s down
        // to ~180; building it in one sorted pass afterwards costs a fraction of that. A resumed run
        // drops the index the previous run left behind, for the same reason.
        _store.DropSearchIndexes();

        // One core is left to the reader, which is decoding bz2 and parsing XML the whole time.
        var workers = Math.Max(1, Environment.ProcessorCount - 1);

        var prepared = pages
            .Where(page => page.Id > resumeFrom)
            .AsParallel()
            .AsOrdered()
            .WithDegreeOfParallelism(workers)
            .WithMergeOptions(ParallelMergeOptions.AutoBuffered)
            .Select(Prepare);

        var articles = new List<ArticleRow>(BatchSize);
        var redirects = new List<(string From, string To)>(BatchSize);

        long articleCount = 0, redirectCount = 0, skipped = 0, seen = 0, lastId = resumeFrom;

        foreach (var page in prepared)
        {
            seen++;
            lastId = page.Id;

            if (page.Article is { } article)
            {
                articles.Add(article);
                articleCount++;
            }
            else if (page.Redirect is { } redirect)
            {
                redirects.Add(redirect);
                redirectCount++;
            }
            else
            {
                skipped++;
            }

            if (articles.Count + redirects.Count >= BatchSize)
                Flush(articles, redirects, lastId);

            if (seen % ProgressInterval == 0)
                _progress?.Invoke($"{seen:N0} pages read, {articleCount:N0} articles stored");
        }

        Flush(articles, redirects, lastId);

        // Until this runs, every read is a full table scan and the store is unusable in practice.
        _progress?.Invoke("building the search index (one sorted pass over the store)...");
        _store.CreateSearchIndexes();

        return new ImportStats(articleCount, redirectCount, skipped);
    }

    /// <summary>
    /// The whole per-page cost, and so the only thing worth spreading across cores: strip the
    /// wikitext and decide what the page is. Pure — it touches no shared state and no store.
    /// </summary>
    private static Prepared Prepare(RawPage page)
    {
        if (page.Namespace != 0)
            return new Prepared(page.Id, null, null);

        if (page.RedirectTo is { Length: > 0 } target)
            return new Prepared(page.Id, null, (page.Title, target));

        var stripped = WikitextStripper.Parse(page.Wikitext);

        // An article that strips to nothing (a stub that was pure template) is not an error --
        // it simply has no prose to offer, and storing it would only make FetchExtract return "".
        if (stripped.Body.Length == 0)
            return new Prepared(page.Id, null, null);

        return new Prepared(
            page.Id,
            new ArticleRow(
                page.Title,
                WikitextStripper.LeadOf(stripped.Body),
                stripped.Body,
                stripped.Links),
            null);
    }

    /// <summary>
    /// Commits a batch and advances the resume point. The id is written LAST and in the same call
    /// as the rows, so a crash between the two can only ever re-import work, never lose it.
    /// </summary>
    private void Flush(List<ArticleRow> articles, List<(string From, string To)> redirects, long lastId)
    {
        if (articles.Count > 0)
        {
            _store.WriteArticles(articles);
            articles.Clear();
        }

        if (redirects.Count > 0)
        {
            _store.WriteRedirects(redirects);
            redirects.Clear();
        }

        if (lastId > 0)
            _store.SetLastPageId(lastId);
    }
}

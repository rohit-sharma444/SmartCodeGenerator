using DomainDiscovery.Wikipedia.Dump;

namespace WikipediaDumpImporter;

/// <summary>What one import pass did.</summary>
public sealed record ImportStats(long Articles, long Redirects, long Skipped);

/// <summary>
/// Turns a stream of <see cref="RawPage"/> into the store: strips wikitext to prose, keeps
/// mainspace only, and records progress so an interrupted run can resume.
/// </summary>
/// <remarks>
/// The full import takes 1.5-2 hours (bz2 decode is the ceiling at ~69 minutes). Resumability is
/// therefore a requirement, not a nicety: a crash 90 minutes in must not cost the whole run. The
/// page id is monotonically increasing in the dump, which is what makes "skip anything at or below
/// the last committed id" a correct resume rule.
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

    public ImportStats Run(IEnumerable<RawPage> pages)
    {
        var resumeFrom = _store.GetLastPageId();

        var articles = new List<ArticleRow>(BatchSize);
        var redirects = new List<(string From, string To)>(BatchSize);

        long articleCount = 0, redirectCount = 0, skipped = 0, seen = 0, lastId = resumeFrom;

        foreach (var page in pages)
        {
            seen++;

            if (page.Id <= resumeFrom)
                continue;

            lastId = page.Id;

            if (page.Namespace != 0)
            {
                skipped++;
            }
            else if (page.RedirectTo is { Length: > 0 } target)
            {
                redirects.Add((page.Title, target));
                redirectCount++;
            }
            else
            {
                var stripped = WikitextStripper.Parse(page.Wikitext);

                // An article that strips to nothing (a stub that was pure template) is not an
                // error -- it simply has no prose to offer, and storing it would only make
                // FetchExtract return "".
                if (stripped.Body.Length > 0)
                {
                    articles.Add(new ArticleRow(
                        page.Title,
                        WikitextStripper.LeadOf(stripped.Body),
                        stripped.Body,
                        stripped.Links));
                    articleCount++;
                }
                else
                {
                    skipped++;
                }
            }

            if (articles.Count + redirects.Count >= BatchSize)
                Flush(articles, redirects, lastId);

            if (seen % ProgressInterval == 0)
                _progress?.Invoke($"{seen:N0} pages read, {articleCount:N0} articles stored");
        }

        Flush(articles, redirects, lastId);

        return new ImportStats(articleCount, redirectCount, skipped);
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

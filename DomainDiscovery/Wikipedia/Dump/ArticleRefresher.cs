using System.Text.Json;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Updates the handful of articles a run is about to read, from the live MediaWiki API, when the
/// dump they came from has fallen behind them.
/// </summary>
/// <remarks>
/// Two calls, not thirty. The first asks for nothing but a LAST-EDIT TIMESTAMP per title, fifty at a
/// time, and almost every title comes back older than the dump — measured against the real API on the
/// 2026-07-01 store, six of the eight articles a point-of-sale run reads had not been touched since
/// the cut, and Customer had not been edited in over a year. Only what actually moved is fetched
/// again.
/// <para>
/// <b>Wikitext, not the API's plaintext extract.</b> The extract endpoint destroys list markup, and
/// <c>PropertyMiner</c> reads exactly that — "an invoice contains: * date * number" is where a class
/// gets its fields. So a refreshed article is fetched as source and put through the same
/// <see cref="WikitextStripper"/> the importer used, which is the only way the row it replaces stays
/// the same shape as the rows around it.
/// </para>
/// <para>
/// Every failure is swallowed. Running offline against a stale dump is a worse answer than running
/// online against a fresh one, and a far better answer than not running.
/// </para>
/// </remarks>
public sealed class ArticleRefresher : IArticleRefresher
{
    /// <summary>The API's cap on titles per query, and the reason this is two calls rather than N.</summary>
    private const int MaxTitlesPerQuery = 50;

    /// <summary>
    /// Content is orders of magnitude bigger than a timestamp, so it is asked for in smaller
    /// batches — and only for titles already known to have moved.
    /// </summary>
    private const int MaxTitlesPerContentQuery = 10;

    private const string ApiUrl = "https://en.wikipedia.org/w/api.php";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly WikipediaStore _store;
    private readonly HttpClient _http;

    public ArticleRefresher(WikipediaStore store, HttpClient? http = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _http = http ?? new HttpClient();
        _http.Timeout = Timeout;

        // Wikimedia refuses a request that does not identify itself: 403 without this, 200 with it.
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", "SmartCodeGenerator-DomainDiscovery/1.0");
    }

    /// <summary>How many articles the last <see cref="Refresh"/> actually rewrote. For reporting.</summary>
    public int LastRefreshed { get; private set; }

    public void Refresh(IEnumerable<string> titles)
    {
        LastRefreshed = 0;

        // A store that cannot say how old it is cannot say what has changed since. Refreshing every
        // title unconditionally would be a fetch per article on every run, which is the rate limit
        // the dump exists to escape.
        if (_store.GetDumpDate() is not { } cutoff)
            return;

        var wanted = titles
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (wanted.Count == 0)
            return;

        try
        {
            _store.EnsureFreshnessTable();

            var stale = EditedSince(wanted, cutoff);

            foreach (var chunk in Chunk(stale.Keys.ToList(), MaxTitlesPerContentQuery))
                LastRefreshed += ReplaceFromSource(chunk, stale);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or JsonException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Offline, throttled, or the API changed shape. The dump still answers.
        }
    }

    /// <summary>
    /// The titles the store is behind on, each with the revision timestamp it is behind. One request
    /// per fifty, asking for a timestamp and nothing else.
    /// </summary>
    /// <remarks>
    /// An article is compared against what the store ACTUALLY HOLDS, which is the revision this
    /// refresher last ingested, or — for one it has never touched — the dump. Comparing everything
    /// against the dump date alone was the first version, and it re-fetched the same stale articles
    /// on every single run, because the dump date never moves.
    /// </remarks>
    private Dictionary<string, string> EditedSince(IReadOnlyList<string> titles, int cutoff)
    {
        var stale = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ingested = _store.GetIngestedRevisions(titles);

        foreach (var chunk in Chunk(titles, MaxTitlesPerQuery))
        {
            var url = $"{ApiUrl}?action=query&format=json&formatversion=2&prop=revisions" +
                      $"&rvprop=timestamp&titles={Uri.EscapeDataString(string.Join('|', chunk))}";

            using var document = JsonDocument.Parse(
                _http.GetStringAsync(url).GetAwaiter().GetResult());

            foreach (var page in Pages(document))
            {
                if (!page.TryGetProperty("title", out var titleElement) ||
                    titleElement.GetString() is not { } title ||
                    !page.TryGetProperty("revisions", out var revisions) ||
                    revisions.ValueKind != JsonValueKind.Array ||
                    revisions.GetArrayLength() == 0)
                    continue;

                if (!revisions[0].TryGetProperty("timestamp", out var stamp) ||
                    stamp.GetString() is not { } timestamp)
                    continue;

                if (IsNewerThanWhatWeHold(title, timestamp, ingested, cutoff))
                    stale[title] = timestamp;
            }
        }

        return stale;
    }

    private static bool IsNewerThanWhatWeHold(
        string title, string timestamp, IReadOnlyDictionary<string, string> ingested, int cutoff)
    {
        // Already refreshed once: compare revision against revision, exactly. ISO-8601 sorts
        // chronologically as text, which is why the timestamp is kept as the API gave it.
        if (ingested.TryGetValue(title, out var held))
            return string.CompareOrdinal(timestamp, held) > 0;

        if (!DateTime.TryParse(timestamp, out var edited))
            return false;

        // Never refreshed, so the dump is what we hold. Its date is a DAY, and an edit made on that
        // day may or may not be in it — so the cut day counts as uncertain and is refetched. A
        // needless refetch costs one request; a missed edit costs the wrong answer.
        return AsYyyyMmDd(edited) >= cutoff;
    }

    /// <summary>
    /// Fetches the current WIKITEXT of each title, strips it exactly as the importer did, and writes
    /// it over the stale row.
    /// </summary>
    private int ReplaceFromSource(IReadOnlyList<string> titles, IReadOnlyDictionary<string, string> stale)
    {
        var url = $"{ApiUrl}?action=query&format=json&formatversion=2&prop=revisions" +
                  $"&rvprop=content&rvslots=main&titles={Uri.EscapeDataString(string.Join('|', titles))}";

        using var document = JsonDocument.Parse(
            _http.GetStringAsync(url).GetAwaiter().GetResult());

        var replaced = 0;

        foreach (var page in Pages(document))
        {
            if (!page.TryGetProperty("title", out var titleElement) ||
                titleElement.GetString() is not { } title)
                continue;

            if (SourceOf(page) is not { Length: > 0 } wikitext)
                continue;

            var stripped = WikitextStripper.Parse(wikitext);

            // An article that strips to nothing is not an update — it is a parse that went wrong,
            // and overwriting a good row with an empty one would be worse than being out of date.
            if (stripped.Body.Length == 0)
                continue;

            _store.ReplaceArticle(new ArticleRow(
                title,
                WikitextStripper.LeadOf(stripped.Body),
                stripped.Body,
                stripped.Links));

            // Record WHICH revision the store now holds, so the next run compares against it rather
            // than fetching this same article again.
            if (stale.TryGetValue(title, out var ingestedRevision))
                _store.SetIngestedRevision(title, ingestedRevision);

            replaced++;
        }

        return replaced;
    }

    private static string? SourceOf(JsonElement page)
    {
        if (!page.TryGetProperty("revisions", out var revisions) ||
            revisions.ValueKind != JsonValueKind.Array ||
            revisions.GetArrayLength() == 0)
            return null;

        if (!revisions[0].TryGetProperty("slots", out var slots) ||
            !slots.TryGetProperty("main", out var main) ||
            !main.TryGetProperty("content", out var content))
            return null;

        return content.GetString();
    }

    private static IEnumerable<JsonElement> Pages(JsonDocument document)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("query", out var query) ||
            query.ValueKind != JsonValueKind.Object ||
            !query.TryGetProperty("pages", out var pages) ||
            pages.ValueKind != JsonValueKind.Array)
            return Array.Empty<JsonElement>();

        // A title the API does not know comes back flagged "missing", with no revisions at all.
        return pages.EnumerateArray().Where(p => !p.TryGetProperty("missing", out _));
    }

    private static int AsYyyyMmDd(DateTime when)
        => (when.Year * 10000) + (when.Month * 100) + when.Day;

    private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.Skip(i).Take(size).ToList();
    }
}

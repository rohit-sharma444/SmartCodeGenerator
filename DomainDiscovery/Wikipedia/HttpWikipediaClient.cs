using System.Text.Json;
using DomainDiscovery.Model;

namespace DomainDiscovery.Wikipedia;

/// <summary>
/// Real <see cref="IWikipediaClient"/> over the MediaWiki API, backed by a disk
/// cache. Network failures degrade to empty results (never throw) so the pipeline
/// can fall back to seed-only discovery.
/// </summary>
public sealed class HttpWikipediaClient : IWikipediaClient
{
    private const string ApiBase = "https://en.wikipedia.org/w/api.php";

    /// <summary>
    /// Applied in place of <see cref="HttpClient"/>'s own 100-second default. Discovery
    /// issues a search per seed name plus an extract, so an unreachable Wikipedia would
    /// otherwise freeze `build app:` for minutes with nothing on screen.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Between uncached requests. A single-title request per candidate answered ~14 rapid
    /// requests with HTTP 429 during design measurement; <see cref="FetchLeads"/> now
    /// batches leads into one request per <see cref="MaxLeadBatchSize"/> titles, so this
    /// delay is paid per batch rather than per candidate.
    /// </summary>
    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// The MediaWiki action API's cap on titles per <c>exlimit=max</c> request. Above this
    /// the API silently ignores the extras, so <see cref="FetchLeads"/> chunks its input.
    /// </summary>
    private const int MaxLeadBatchSize = 20;

    private readonly WikipediaCache _cache;
    private readonly HttpClient _http;

    /// <param name="timeout">
    /// Per-request timeout. Defaults to <see cref="DefaultTimeout"/> when this client
    /// creates its own <see cref="HttpClient"/>; an injected client keeps whatever
    /// timeout its owner configured unless one is passed here.
    /// </param>
    public HttpWikipediaClient(
        string cacheDirectory, HttpClient? http = null, TimeSpan? timeout = null)
    {
        _cache = new WikipediaCache(cacheDirectory);

        if (http is null)
            _http = new HttpClient { Timeout = timeout ?? DefaultTimeout };
        else
        {
            _http = http;
            if (timeout is not null)
                _http.Timeout = timeout.Value;
        }

        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", "SmartCodeGenerator-DomainDiscovery/1.0");
    }

    public IReadOnlyList<SearchHit> Search(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Array.Empty<SearchHit>();

        var query = term.Trim();
        var url = $"{ApiBase}?action=opensearch&limit=5&format=json&search={Uri.EscapeDataString(query)}";

        var json = GetCached($"search:{query}", url);
        if (json.Length == 0)
            return Array.Empty<SearchHit>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            // opensearch returns: [ term, [titles], [descriptions], [urls] ]
            var titles = doc.RootElement[1];
            var urls = doc.RootElement[3];

            var hits = new List<SearchHit>();
            for (var i = 0; i < titles.GetArrayLength(); i++)
            {
                hits.Add(new SearchHit(
                    titles[i].GetString() ?? string.Empty,
                    urls[i].GetString() ?? string.Empty,
                    i));
            }
            return hits;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return Array.Empty<SearchHit>();
        }
    }

    public string FetchExtract(string articleTitle)
    {
        var url = $"{ApiBase}?action=query&prop=extracts&explaintext=1&redirects=1&format=json"
                + $"&titles={Uri.EscapeDataString(articleTitle)}";

        var json = GetCached($"extract:{articleTitle}", url);
        if (json.Length == 0)
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var pages = doc.RootElement.GetProperty("query").GetProperty("pages");
            foreach (var page in pages.EnumerateObject())
            {
                if (page.Value.TryGetProperty("extract", out var extract))
                    return extract.GetString() ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            // fall through
        }

        return string.Empty;
    }

    /// <summary>
    /// Mainspace article titles this article links to, or empty when unavailable. Link
    /// targets are disambiguated by editors, which is why they beat raw prose nouns.
    /// </summary>
    public IReadOnlyList<string> FetchLinks(string articleTitle)
    {
        var url = $"{ApiBase}?action=query&prop=links&plnamespace=0&pllimit=500"
                + $"&redirects=1&format=json&titles={Uri.EscapeDataString(articleTitle)}";

        var json = GetCached($"links:{articleTitle}", url);
        if (json.Length == 0)
            return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var pages = doc.RootElement.GetProperty("query").GetProperty("pages");

            foreach (var page in pages.EnumerateObject())
            {
                if (!page.Value.TryGetProperty("links", out var links))
                    continue;

                var titles = new List<string>();
                foreach (var link in links.EnumerateArray())
                {
                    if (link.TryGetProperty("title", out var title))
                        titles.Add(title.GetString() ?? string.Empty);
                }
                return titles;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            // fall through
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// The article's first sentence — its definition — or empty when unavailable.
    /// </summary>
    public string FetchLead(string articleTitle)
    {
        var url = $"{ApiBase}?action=query&prop=extracts&exintro=1&explaintext=1&exsentences=1"
                + $"&redirects=1&format=json&titles={Uri.EscapeDataString(articleTitle)}";

        var json = GetCached($"lead:{articleTitle}", url);
        return ExtractLeadFromBody(json, articleTitle);
    }

    /// <summary>
    /// The first sentence of every article named in <paramref name="articleTitles"/>, fetched
    /// in as few requests as the API allows. See the interface doc for why this exists and
    /// what the never-throw contract means for a caller.
    /// </summary>
    public IReadOnlyDictionary<string, string> FetchLeads(IReadOnlyList<string> articleTitles)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (articleTitles.Count == 0)
            return result;

        var toFetch = new List<string>();
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var title in articleTitles)
        {
            if (string.IsNullOrWhiteSpace(title) || !requested.Add(title))
                continue;

            // Cached per title (Task 5's "lead:{title}" key), so a title already resolved by
            // an earlier FetchLead or FetchLeads call is a disk hit here, not a repeat request.
            if (_cache.TryGet($"lead:{title}", out var cachedBody))
            {
                var lead = ExtractLeadFromBody(cachedBody, title);
                if (lead.Length > 0)
                    result[title] = lead;
            }
            else
            {
                toFetch.Add(title);
            }
        }

        foreach (var chunk in Chunk(toFetch, MaxLeadBatchSize))
            FetchLeadsBatch(chunk, result);

        return result;
    }

    /// <summary>
    /// Fetches leads for one chunk of at-most-<see cref="MaxLeadBatchSize"/> titles in a
    /// single request, then splits the response back into the same per-title cache entries a
    /// single-title <see cref="FetchLead"/> would have written — so later reads (single or
    /// batched) hit the cache regardless of which method originally populated it.
    /// </summary>
    private void FetchLeadsBatch(IReadOnlyList<string> titles, Dictionary<string, string> result)
    {
        if (titles.Count == 0)
            return;

        var joinedTitles = string.Join("%7C", titles.Select(Uri.EscapeDataString));
        var url = $"{ApiBase}?action=query&prop=extracts&exintro=1&explaintext=1&exsentences=1"
                + $"&exlimit=max&redirects=1&format=json&titles={joinedTitles}";

        Thread.Sleep(ThrottleDelay);

        string body;
        try
        {
            body = _http.GetStringAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Leave this chunk's titles uncached and absent from the result: a transient
            // failure must be retried on the next run, not pinned to disk as a permanent miss.
            return;
        }

        if (body.Length == 0)
            return;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return;
        }

        using (doc)
        {
            foreach (var title in titles)
            {
                var lead = ResolveLeadForTitle(doc.RootElement, title);

                var perTitleBody = lead.Length > 0
                    ? JsonSerializer.Serialize(new
                        {
                            query = new
                            {
                                pages = new Dictionary<string, object>
                                {
                                    ["1"] = new { title = ResolveFinalTitle(title, doc.RootElement), extract = lead }
                                }
                            }
                        })
                    : """{"query":{"pages":{"-1":{"missing":""}}}}""";

                try { _cache.Put($"lead:{title}", perTitleBody); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* caching is best-effort */ }

                if (lead.Length > 0)
                    result[title] = lead;
            }
        }
    }

    /// <summary>Shared by <see cref="FetchLead"/> and a per-title cache hit inside
    /// <see cref="FetchLeads"/>: parses a lead response body and returns its extract, or
    /// empty for an unusable body — never throws.</summary>
    private static string ExtractLeadFromBody(string body, string requestedTitle)
    {
        if (body.Length == 0)
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(body);
            return ResolveLeadForTitle(doc.RootElement, requestedTitle);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Finds the extract for <paramref name="requestedTitle"/> inside a (possibly multi-page,
    /// possibly redirected/normalized) query response. The API keys pages by the FINAL title,
    /// not the one requested, so this first resolves that final title via
    /// <see cref="ResolveFinalTitle"/> and matches on it. A single-title response (the common
    /// case for an old cache entry, or a chunk of one) has exactly one page regardless of
    /// whether its "title" property lines up, so that lone page is used as a fallback —
    /// this is what let the original single-title <see cref="FetchLead"/> ignore redirects
    /// entirely and still work.
    /// </summary>
    private static string ResolveLeadForTitle(JsonElement root, string requestedTitle)
    {
        if (!root.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages))
            return string.Empty;

        var finalTitle = ResolveFinalTitle(requestedTitle, root);

        JsonElement? matchedByTitle = null;
        JsonElement? onlyPage = null;
        var pageCount = 0;

        foreach (var page in pages.EnumerateObject())
        {
            pageCount++;
            onlyPage = page.Value;

            if (matchedByTitle is null
                && page.Value.TryGetProperty("title", out var titleProp)
                && string.Equals(titleProp.GetString(), finalTitle, StringComparison.Ordinal))
            {
                matchedByTitle = page.Value;
            }
        }

        var chosen = matchedByTitle ?? (pageCount == 1 ? onlyPage : null);
        if (chosen is null)
            return string.Empty;

        return chosen.Value.TryGetProperty("extract", out var extract)
            ? extract.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Follows <c>query.normalized</c> (casing/spacing) and then <c>query.redirects</c>
    /// (editorial redirects, possibly chained) to map the title a caller asked for to the
    /// title its page is actually keyed under. Skipping this is the subtle failure mode a
    /// batch request introduces: with several pages in one response, "POS terminal" and
    /// "Payment terminal" can both be present, and only this chain says which one the caller
    /// meant.
    /// </summary>
    private static string ResolveFinalTitle(string requestedTitle, JsonElement root)
    {
        if (!root.TryGetProperty("query", out var query))
            return requestedTitle;

        var current = requestedTitle;

        if (query.TryGetProperty("normalized", out var normalized))
            current = FindMapping(normalized, current) ?? current;

        if (query.TryGetProperty("redirects", out var redirects))
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (visited.Add(current))
            {
                var next = FindMapping(redirects, current);
                if (next is null)
                    break;
                current = next;
            }
        }

        return current;
    }

    /// <summary>Looks up a single "from" -> "to" hop in a <c>normalized</c>/<c>redirects</c>
    /// array, or null when <paramref name="from"/> is not in it.</summary>
    private static string? FindMapping(JsonElement chain, string from)
    {
        foreach (var entry in chain.EnumerateArray())
        {
            if (entry.TryGetProperty("from", out var f)
                && string.Equals(f.GetString(), from, StringComparison.Ordinal))
            {
                return entry.TryGetProperty("to", out var t) ? t.GetString() : null;
            }
        }
        return null;
    }

    /// <summary>Splits a list into fixed-size slices, for the API's per-request title cap.</summary>
    private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.Skip(i).Take(size).ToArray();
    }

    private string GetCached(string key, string url)
    {
        if (_cache.TryGet(key, out var cached))
            return cached;

        Thread.Sleep(ThrottleDelay);

        try
        {
            var body = _http.GetStringAsync(url).GetAwaiter().GetResult();
            try { _cache.Put(key, body); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* caching is best-effort */ }
            return body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}

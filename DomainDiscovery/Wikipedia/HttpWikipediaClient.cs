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
    /// Between uncached requests. Shaping fetches one lead per candidate, and the API
    /// answered ~14 rapid requests with HTTP 429 during design measurement.
    /// </summary>
    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromMilliseconds(150);

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

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

    private readonly WikipediaCache _cache;
    private readonly HttpClient _http;

    public HttpWikipediaClient(string cacheDirectory, HttpClient? http = null)
    {
        _cache = new WikipediaCache(cacheDirectory);
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", "SmartCodeGenerator-DomainDiscovery/1.0");
    }

    public IReadOnlyList<SearchHit> Search(IEnumerable<string> terms)
    {
        var query = string.Join(' ', terms);
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

    private string GetCached(string key, string url)
    {
        if (_cache.TryGet(key, out var cached))
            return cached;

        try
        {
            var body = _http.GetStringAsync(url).GetAwaiter().GetResult();
            try { _cache.Put(key, body); }
            catch (IOException) { /* caching is best-effort */ }
            return body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}

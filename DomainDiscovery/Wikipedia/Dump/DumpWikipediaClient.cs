using DomainDiscovery.Model;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// <see cref="IWikipediaClient"/> over the imported dump. No network, so no throttle, no HTTP 429,
/// no timeout, and no cache directory.
/// </summary>
/// <remarks>
/// The never-throw contract is inherited unchanged from <see cref="IWikipediaClient"/>, but it now
/// protects against a corrupt store rather than a flaky network.
/// <para>
/// <see cref="FetchLeads"/> keeps its batch signature purely so callers need not change. The
/// rationale in the interface doc — batching to dodge the API's rate limit — no longer applies
/// here: offline, a batch is just a loop over local reads, and it is not faster than N calls.
/// </para>
/// </remarks>
public sealed class DumpWikipediaClient : IWikipediaClient
{
    /// <summary>How many prefix matches a search may return, mirroring the old opensearch limit=5.</summary>
    private const int MaxHits = 5;

    private readonly WikipediaStore _store;

    public DumpWikipediaClient(WikipediaStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public IReadOnlyList<SearchHit> Search(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Array.Empty<SearchHit>();

        try
        {
            var resolved = _store.ResolveTitle(term.Trim());
            if (resolved is null)
                return Array.Empty<SearchHit>();

            var titles = new List<string> { resolved };

            foreach (var title in _store.PrefixSearch(term.Trim(), MaxHits))
            {
                if (!titles.Contains(title, StringComparer.OrdinalIgnoreCase))
                    titles.Add(title);
            }

            return titles
                .Take(MaxHits)
                .Select((title, rank) => new SearchHit(title, UrlFor(title), rank))
                .ToList();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            return Array.Empty<SearchHit>();
        }
    }

    public string FetchExtract(string articleTitle) => Read(articleTitle)?.Body ?? string.Empty;

    public IReadOnlyList<string> FetchLinks(string articleTitle)
        => Read(articleTitle)?.Links ?? Array.Empty<string>();

    public string FetchLead(string articleTitle) => Read(articleTitle)?.Lead ?? string.Empty;

    public IReadOnlyDictionary<string, string> FetchLeads(IReadOnlyList<string> articleTitles)
    {
        var leads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var title in articleTitles)
        {
            if (string.IsNullOrWhiteSpace(title) || leads.ContainsKey(title))
                continue;

            // Keyed by the title the CALLER asked for, even when a redirect resolved it to another
            // article -- the interface promises exactly that, and EntityShaper looks its own
            // SourceTitle back up in this map.
            var lead = Read(title)?.Lead;
            if (!string.IsNullOrEmpty(lead))
                leads[title] = lead;
        }

        return leads;
    }

    private ArticleRow? Read(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        try
        {
            return _store.Read(title);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The article's public URL. The store holds no URLs — every one is derivable.</summary>
    private static string UrlFor(string title)
        => "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));
}

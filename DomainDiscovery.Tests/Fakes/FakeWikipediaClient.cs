using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;

namespace DomainDiscovery.Tests.Fakes;

/// <summary>In-memory <see cref="IWikipediaClient"/> for offline tests.</summary>
public sealed class FakeWikipediaClient : IWikipediaClient
{
    /// <summary>Hits returned for any term with no entry in <see cref="HitsByTerm"/>.</summary>
    public List<SearchHit> SearchHits { get; } = new();

    /// <summary>Per-term hits, for tests that care which term found what.</summary>
    public Dictionary<string, List<SearchHit>> HitsByTerm { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Extracts { get; } = new();

    /// <summary>Per-title link targets, for tests exercising the link-based candidate source.</summary>
    public Dictionary<string, List<string>> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-title lead sentences, for tests exercising the definition-based enrichment.</summary>
    public Dictionary<string, string> Leads { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every term passed to <see cref="Search"/>, in call order.</summary>
    public List<string> SearchedTerms { get; } = new();

    public IReadOnlyList<SearchHit> Search(string term)
    {
        SearchedTerms.Add(term);
        return HitsByTerm.TryGetValue(term, out var hits) ? hits : SearchHits;
    }

    public string FetchExtract(string articleTitle)
        => Extracts.TryGetValue(articleTitle, out var text) ? text : string.Empty;

    public IReadOnlyList<string> FetchLinks(string articleTitle)
        => Links.TryGetValue(articleTitle, out var links) ? links : Array.Empty<string>();

    public string FetchLead(string articleTitle)
        => Leads.TryGetValue(articleTitle, out var lead) ? lead : string.Empty;
}

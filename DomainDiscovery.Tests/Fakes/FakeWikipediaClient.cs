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

    /// <summary>Every title passed to <see cref="FetchLead"/>, in call order — tests use this
    /// to prove a caller stopped fetching leads one at a time.</summary>
    public List<string> FetchLeadCalls { get; } = new();

    /// <summary>Every batch passed to <see cref="FetchLeads"/>, in call order — tests use this
    /// to prove a caller batches its lead lookups into a single call.</summary>
    public List<IReadOnlyList<string>> FetchLeadsCalls { get; } = new();

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
    {
        FetchLeadCalls.Add(articleTitle);
        return Leads.TryGetValue(articleTitle, out var lead) ? lead : string.Empty;
    }

    /// <summary>In-memory stand-in for the batched lookup: resolves each requested title
    /// from <see cref="Leads"/>, omitting any title with no entry (or an empty one) rather
    /// than throwing — mirroring the real client's never-throw contract.</summary>
    public IReadOnlyDictionary<string, string> FetchLeads(IReadOnlyList<string> articleTitles)
    {
        FetchLeadsCalls.Add(articleTitles);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in articleTitles)
        {
            if (Leads.TryGetValue(title, out var lead) && lead.Length > 0)
                result[title] = lead;
        }
        return result;
    }
}

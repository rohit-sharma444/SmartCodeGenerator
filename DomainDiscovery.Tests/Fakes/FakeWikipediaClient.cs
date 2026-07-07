using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;

namespace DomainDiscovery.Tests.Fakes;

/// <summary>In-memory <see cref="IWikipediaClient"/> for offline tests.</summary>
public sealed class FakeWikipediaClient : IWikipediaClient
{
    public List<SearchHit> SearchHits { get; } = new();
    public Dictionary<string, string> Extracts { get; } = new();
    public List<string> LastSearchTerms { get; } = new();

    public IReadOnlyList<SearchHit> Search(IEnumerable<string> terms)
    {
        LastSearchTerms.Clear();
        LastSearchTerms.AddRange(terms);
        return SearchHits;
    }

    public string FetchExtract(string articleTitle)
        => Extracts.TryGetValue(articleTitle, out var text) ? text : string.Empty;
}

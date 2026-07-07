using DomainDiscovery.Model;

namespace DomainDiscovery.Wikipedia;

/// <summary>Read-only access to Wikipedia: search and plain-text article extract.</summary>
public interface IWikipediaClient
{
    /// <summary>Best-first search hits for the given terms (may be empty).</summary>
    IReadOnlyList<SearchHit> Search(IEnumerable<string> terms);

    /// <summary>Plain-text extract of an article, or empty string if unavailable.</summary>
    string FetchExtract(string articleTitle);
}

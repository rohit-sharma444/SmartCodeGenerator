using DomainDiscovery.Model;

namespace DomainDiscovery.Wikipedia;

/// <summary>Read-only access to Wikipedia: search and plain-text article extract.</summary>
public interface IWikipediaClient
{
    /// <summary>
    /// Best-first search hits for a single term (may be empty). Search matches article
    /// titles, so callers must pass one term at a time rather than a joined phrase.
    /// </summary>
    IReadOnlyList<SearchHit> Search(string term);

    /// <summary>Plain-text extract of an article, or empty string if unavailable.</summary>
    string FetchExtract(string articleTitle);
}

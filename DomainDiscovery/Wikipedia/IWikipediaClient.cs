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

    /// <summary>
    /// Mainspace article titles this article links to, or empty when unavailable. Link
    /// targets are disambiguated by editors, which is why they beat raw prose nouns.
    /// </summary>
    IReadOnlyList<string> FetchLinks(string articleTitle);

    /// <summary>
    /// The article's first sentence — its definition — or empty when unavailable.
    /// </summary>
    string FetchLead(string articleTitle);

    /// <summary>
    /// The first sentence of every article named in <paramref name="articleTitles"/>, fetched
    /// in as few requests as the API allows rather than one per title. Shaping a real domain
    /// can need a lead for dozens of uncurated candidates in one pass, and the MediaWiki API
    /// starts answering with HTTP 429 well before that many requests land back to back — the
    /// same information comes back in one request when titles are pipe-joined. A title with
    /// no usable lead (unknown article, redirect to nowhere, or an unparseable response) is
    /// simply absent from the returned map; this method never throws.
    /// </summary>
    /// <param name="articleTitles">
    /// Titles to fetch, as the caller knows them (e.g. an entity's own <c>SourceTitle</c>).
    /// The returned map is keyed by these exact titles, even when the API resolves one to a
    /// different final title via normalization or a redirect.
    /// </param>
    IReadOnlyDictionary<string, string> FetchLeads(IReadOnlyList<string> articleTitles);
}

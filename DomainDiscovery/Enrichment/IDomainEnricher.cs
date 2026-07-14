using DomainDiscovery.Model;

namespace DomainDiscovery.Enrichment;

public interface IDomainEnricher
{
    /// <summary>
    /// Merges what <paramref name="articleTitle"/> knows into <paramref name="seed"/>, and hands the
    /// same model back. Call it once per article the domain is made of: the merge is additive and
    /// idempotent on names it has already seen.
    /// </summary>
    /// <remarks>
    /// This takes an article TITLE rather than the <see cref="TopicResult"/> it used to, because a
    /// domain is no longer one article. The recognized topic is simply the first of them.
    /// </remarks>
    CandidateDomainModel Enrich(string articleTitle, CandidateDomainModel seed);
}

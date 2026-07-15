namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>An article's wikitext reduced to prose, with its outgoing links kept aside.</summary>
/// <param name="Body">
/// Plain prose. List items survive as a leading <c>"* "</c> on their own line — deliberately,
/// unlike the API's explaintext, because <see cref="Enrichment.PropertyMiner"/> reads them.
/// </param>
/// <param name="Links">Mainspace link targets, in order of first appearance.</param>
public sealed record StrippedArticle(string Body, IReadOnlyList<string> Links);

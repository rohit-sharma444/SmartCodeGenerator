using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;

namespace DomainDiscovery.Recognition;

/// <summary>
/// Recognizes the domain by searching Wikipedia for the seed entity names and
/// taking the best hit as the topic. Confidence falls off with rank.
/// </summary>
public sealed class WikipediaTopicRecognizer : ITopicRecognizer
{
    private readonly IWikipediaClient _client;

    public WikipediaTopicRecognizer(IWikipediaClient client)
        => _client = client;

    public TopicResult Recognize(IEnumerable<string> seedEntityNames)
    {
        var terms = seedEntityNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (terms.Count == 0)
            return TopicResult.None;

        var best = _client.Search(terms)
            .OrderBy(h => h.Rank)
            .FirstOrDefault();

        if (best is null)
            return TopicResult.None;

        var confidence = 1.0 / (1 + best.Rank);
        return new TopicResult(best.Title, best.Url, confidence);
    }
}

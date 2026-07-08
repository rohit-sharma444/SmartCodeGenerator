using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;

namespace DomainDiscovery.Recognition;

/// <summary>
/// Recognizes the domain by searching Wikipedia for each seed entity name and taking
/// the best-supported hit as the topic.
/// </summary>
/// <remarks>
/// Wikipedia's search matches article <em>titles</em>, so the seed terms cannot be
/// joined into one query: "Payroll Application" is not the title of any article and
/// matches nothing, even though "Payroll" and "Application" both do. Each term is
/// therefore searched on its own and the hits vote — a title that several seed terms
/// agree on, and that ranks highly for them, wins.
/// </remarks>
public sealed class WikipediaTopicRecognizer : ITopicRecognizer
{
    /// <summary>Caps the number of HTTP round-trips for a long seed set.</summary>
    private const int MaxTermsSearched = 6;

    private readonly IWikipediaClient _client;

    public WikipediaTopicRecognizer(IWikipediaClient client)
        => _client = client;

    public TopicResult Recognize(IEnumerable<string> seedEntityNames)
    {
        var terms = seedEntityNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTermsSearched)
            .ToList();

        if (terms.Count == 0)
            return TopicResult.None;

        var candidates = new List<Candidate>();

        foreach (var term in terms)
        {
            foreach (var hit in _client.Search(term))
            {
                var candidate = candidates.FirstOrDefault(
                    c => string.Equals(c.Title, hit.Title, StringComparison.OrdinalIgnoreCase));

                if (candidate is null)
                    candidates.Add(candidate = new Candidate(hit.Title, hit.Url));

                // A hit is worth more the higher it ranks, and scores accumulate across
                // the terms that found it, so agreement between seed terms outweighs rank.
                candidate.Score += 1.0 / (1 + hit.Rank);
            }
        }

        if (candidates.Count == 0)
            return TopicResult.None;

        // MaxBy keeps the first element on a tie, so an all-square vote falls back to the
        // hit found by the earliest seed term — usually the head noun of the description.
        var best = candidates.MaxBy(c => c.Score)!;

        // Each term contributes at most 1.0, so this lands in (0, 1].
        return new TopicResult(best.Title, best.Url, best.Score / terms.Count);
    }

    private sealed class Candidate(string title, string url)
    {
        public string Title { get; } = title;
        public string Url { get; } = url;
        public double Score { get; set; }
    }
}

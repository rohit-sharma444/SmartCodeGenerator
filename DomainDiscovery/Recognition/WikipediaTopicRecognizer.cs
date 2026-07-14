using System.Text.RegularExpressions;
using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;

namespace DomainDiscovery.Recognition;

/// <summary>
/// Recognizes the domain by searching Wikipedia for the seed entity names — and for the word
/// sequences of the user's own description — then taking the best-supported hit as the topic.
/// </summary>
/// <remarks>
/// Single words are not enough, and the full encyclopedia is what proved it. "a point of sale
/// system for a retail shop" seeds the words point, sale, retail, shop; "point" resolves exactly
/// to <em>Point</em>, a disambiguation page, which scored 1.0 while the article the user meant
/// scored 0.33 as a mere prefix hit. The domain has a name — "point of sale" — and nothing was
/// ever asking for it. So the description's word sequences are searched alongside its words.
/// <para>
/// A sequence is worth more than a word because it IS agreement between words, in order: a hit
/// found by an n-word phrase scores n times what a single word's hit scores, which generalizes the
/// rule this recognizer already followed — agreement outweighs rank.
/// </para>
/// <para>
/// The old remark here claimed seed terms "cannot be joined into one query" because no article is
/// titled "Payroll Application". That is true of the whole description and false of its parts, and
/// the cost of asking has collapsed: against the local store a miss is a sub-millisecond index
/// probe, not an HTTP round trip.
/// </para>
/// </remarks>
public sealed class WikipediaTopicRecognizer : ITopicRecognizer
{
    /// <summary>Caps the number of HTTP round-trips for a long seed set.</summary>
    private const int MaxTermsSearched = 6;

    /// <summary>
    /// Caps the word sequences searched, for the same reason. The longest are searched first:
    /// "point of sale" identifies a domain that "of sale" cannot.
    /// </summary>
    private const int MaxPhrasesSearched = 4;

    /// <summary>Longest word sequence worth searching. Beyond this, titles stop existing.</summary>
    private const int MaxPhraseWords = 4;

    /// <summary>
    /// Words that carry no domain on their own. A sequence opening or closing on one of them is a
    /// fragment ("of sale", "a point"), never the name of anything, so it is not searched.
    /// </summary>
    private static readonly HashSet<string> FunctionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "for", "in", "on", "at", "to", "with", "and", "or", "as", "by",
        "from", "into", "per", "via", "that", "this", "these", "those", "it", "its", "my", "our",
        "your", "their", "is", "are", "be", "was", "were", "i", "we", "you",
    };

    /// <summary>
    /// A disambiguation page's lead does not describe a thing; it announces a list, and always
    /// closes on the colon that introduces it ("Point or points may refer to:", "Sale often refers
    /// to:"). A real lead is a sentence and ends like one, so this does not catch articles that
    /// merely happen to say what they refer to.
    /// </summary>
    private static readonly Regex DisambiguationLead = new(
        @"\brefers?\s+to:\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IWikipediaClient _client;

    public WikipediaTopicRecognizer(IWikipediaClient client)
        => _client = client;

    /// <summary>One thing to search, and what a hit from it is worth.</summary>
    private readonly record struct Term(string Text, double Weight);

    public TopicResult Recognize(IEnumerable<string> seedEntityNames, string? description = null)
    {
        var seeds = seedEntityNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTermsSearched)
            .ToList();

        // Sequences first, so that on a tie the more specific evidence is the one MaxBy keeps.
        var terms = PhrasesIn(description)
            .Select(phrase => new Term(phrase, WordCount(phrase)))
            .Concat(seeds.Select(seed => new Term(seed, 1)))
            .ToList();

        if (terms.Count == 0)
            return TopicResult.None;

        var candidates = new List<Candidate>();

        foreach (var term in terms)
        {
            foreach (var hit in _client.Search(term.Text))
            {
                var candidate = candidates.FirstOrDefault(
                    c => string.Equals(c.Title, hit.Title, StringComparison.OrdinalIgnoreCase));

                if (candidate is null)
                    candidates.Add(candidate = new Candidate(hit.Title, hit.Url));

                // A hit is worth more the higher it ranks, and scores accumulate across the terms
                // that found it, so agreement between terms outweighs rank.
                candidate.Score += term.Weight / (1 + hit.Rank);
            }
        }

        if (candidates.Count == 0)
            return TopicResult.None;

        RemoveDisambiguationPages(candidates);

        if (candidates.Count == 0)
            return TopicResult.None;

        // MaxBy keeps the first element on a tie, so an all-square vote falls back to the hit found
        // by the earliest term — the longest word sequence, or failing that the head noun.
        var best = candidates.MaxBy(c => c.Score)!;

        // Still measured against the seed words, so the number keeps the meaning it had: how much
        // of what the user named agrees on this article. A phrase can carry the score past that on
        // its own, and a phrase matched outright is certainty, so the ceiling holds it at 1.
        var confidence = Math.Min(1.0, best.Score / Math.Max(1, seeds.Count));

        return new TopicResult(best.Title, best.Url, confidence);
    }

    /// <summary>
    /// Drops the pages that only point at other pages. They are the failure mode of a full
    /// encyclopedia: a one-word domain term is very often the title of a disambiguation page, which
    /// then wins the vote outright and enriches to nothing, because it defines nothing.
    /// </summary>
    /// <remarks>
    /// One batched call, not one per candidate — the HTTP fallback pays a round trip for each, and
    /// <see cref="IWikipediaClient.FetchLeads"/> exists to keep that at one.
    /// </remarks>
    private void RemoveDisambiguationPages(List<Candidate> candidates)
    {
        var leads = _client.FetchLeads(candidates.Select(c => c.Title).ToList());

        candidates.RemoveAll(candidate =>
            leads.TryGetValue(candidate.Title, out var lead) &&
            lead.Length > 0 &&
            DisambiguationLead.IsMatch(lead.TrimEnd()));
    }

    /// <summary>
    /// The contiguous word sequences of the description that could name an article, longest first.
    /// </summary>
    private static List<string> PhrasesIn(string? description)
    {
        var phrases = new List<string>();
        if (string.IsNullOrWhiteSpace(description))
            return phrases;

        var words = Regex.Matches(description, @"[\p{L}\p{N}]+")
            .Select(m => m.Value)
            .ToList();

        for (var length = MaxPhraseWords; length >= 2; length--)
        {
            for (var start = 0; start + length <= words.Count; start++)
            {
                if (FunctionWords.Contains(words[start]) ||
                    FunctionWords.Contains(words[start + length - 1]))
                    continue;

                var phrase = string.Join(' ', words.GetRange(start, length));
                if (!phrases.Contains(phrase, StringComparer.OrdinalIgnoreCase))
                    phrases.Add(phrase);
            }
        }

        return phrases.Take(MaxPhrasesSearched).ToList();
    }

    private static int WordCount(string phrase)
        => phrase.Count(char.IsWhiteSpace) + 1;

    private sealed class Candidate(string title, string url)
    {
        public string Title { get; } = title;
        public string Url { get; } = url;
        public double Score { get; set; }
    }
}

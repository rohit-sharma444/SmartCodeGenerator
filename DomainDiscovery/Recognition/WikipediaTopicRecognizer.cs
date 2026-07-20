using System.Text.RegularExpressions;
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
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

    /// <summary>Scores are sums of 1/(1+rank), so "the same score" is a tolerance, not equality.</summary>
    private const double ScoreTolerance = 1e-9;

    /// <summary>
    /// How many articles BESIDE the winner the domain may be read from. Each one is a full article
    /// read and a whole domain adopted, so this is a budget, not a limit to grow.
    /// </summary>
    private const int MaxSupportingArticles = 3;

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
    /// A disambiguation page's lead does not describe a thing; it announces a list and closes on the
    /// colon that introduces it. Real disambiguation leads take several forms — "Point or points may
    /// refer to:", "Point may refer to any of the following:", "Mercury may mean:", "PIN stands for:"
    /// — so this matches one of those disambiguation verbs followed, with no sentence break, by the
    /// trailing colon. Requiring a colon at the very end and forbidding an intervening period keeps it
    /// off ordinary prose that merely says what a term "refers to" ("An invoice refers to a commercial
    /// document ... .").
    /// </summary>
    private static readonly Regex DisambiguationLead = new(
        @"\b(?:refers?\s+to|means?|stands?\s+for)\b[^.]*:\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

                // Did the user NAME this article, or did a redirect take us here from a word that
                // means something else? See SupportingTitlesFrom.
                if (string.Equals(term.Text, hit.Title, StringComparison.OrdinalIgnoreCase))
                    candidate.WasNamedOutright = true;
            }
        }

        if (candidates.Count == 0)
            return TopicResult.None;

        RemoveUnusableTopics(candidates);

        if (candidates.Count == 0)
            return TopicResult.None;

        var best = SelectBest(candidates);

        // Still measured against the seed words, so the number keeps the meaning it had: how much
        // of what the user named agrees on this article. A phrase can carry the score past that on
        // its own, and a phrase matched outright is certainty, so the ceiling holds it at 1.
        var confidence = Math.Min(1.0, best.Score / Math.Max(1, seeds.Count));

        return new TopicResult(best.Title, best.Url, confidence)
        {
            SupportingTitles = SupportingTitlesFrom(candidates, best)
        };
    }

    /// <summary>
    /// The other articles worth reading the domain out of: the ones the user NAMED, beside the
    /// winner, best-supported first.
    /// </summary>
    /// <remarks>
    /// A description usually names several concepts and only one of them can win the vote. "track
    /// customers, payments and invoices" named three, and enriching from the winner alone threw two
    /// away — the model had three classes where the three articles together hold fifteen.
    /// <para>
    /// <b>Only what the user named outright, never what a redirect led to.</b> This is the whole
    /// safeguard, and it is not fussiness: "a lending system for a public library" resolves the word
    /// <em>lending</em> — through an entirely reasonable editor-made redirect — to <em>Loan</em>,
    /// the financial instrument. Enriching from it dressed a library up in Lender, Borrower,
    /// Interest, Money and CreditCard. A library loan is not a bank loan. A redirect is a synonym of
    /// the WORD, which is exactly what recognition wants and exactly what enrichment cannot afford:
    /// reading a whole article means adopting a whole domain, so it must be a domain the user
    /// actually asked for. The winner is exempt — it earned its place by the vote, however it was
    /// resolved.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> SupportingTitlesFrom(List<Candidate> candidates, Candidate best)
        => candidates
            .Where(c => c != best && c.WasNamedOutright)
            .OrderByDescending(c => c.Score)
            .Take(MaxSupportingArticles)
            .Select(c => c.Title)
            .ToList();

    /// <summary>
    /// The winning candidate: highest score, and where several score alike, the one whose article
    /// talks about the others.
    /// </summary>
    /// <remarks>
    /// A tie is the normal case, not an edge case, and it used to be settled by accident. Each seed
    /// word usually resolves EXACTLY to its own article, so each scores 1.0 and nothing agrees with
    /// anything; <c>MaxBy</c> keeps the first element, so the winner was whichever word the user
    /// happened to type first. "a system to manage appointment schedules for a clinic" produced a
    /// four-way tie between System, Appointment, Schedule and Clinic, and System won for being
    /// first.
    /// <para>
    /// What settles it honestly is the encyclopedia: of the things you named, the domain is the one
    /// its own article talks about the others in. Measured against the store, that picks Invoice out
    /// of {Invoice, Payment, Customer}, Library out of {Library, Book, Member} and Payroll out of
    /// {Payroll, Employee, Salary} — each of them naming both of its rivals, while the rivals name
    /// fewer. It is the same "agreement outweighs rank" rule the score already follows, asked of the
    /// articles instead of the search hits.
    /// </para>
    /// <para>
    /// The bodies are fetched only when there IS a tie, and only for the tied candidates. Against the
    /// dump that is a handful of local seeks.
    /// </para>
    /// </remarks>
    private Candidate SelectBest(List<Candidate> candidates)
    {
        var top = candidates.Max(c => c.Score);

        // Scores are sums of 1/(1+rank), so compare with a tolerance rather than for equality.
        var tied = candidates.Where(c => Math.Abs(c.Score - top) < ScoreTolerance).ToList();

        if (tied.Count == 1)
            return tied[0];

        // Cohesion cannot separate candidates that are all unrelated to each other — which is itself
        // the truth about a description naming several things no article covers together. Nothing is
        // there to choose between them, so the first still wins, and Confidence already says how
        // little that is worth.
        return tied
            .Select(candidate => (Candidate: candidate, Cohesion: CohesionOf(candidate, tied)))
            .MaxBy(scored => scored.Cohesion)
            .Candidate;
    }

    /// <summary>
    /// How many of the OTHER tied candidates this one's article names. The domain's article is the
    /// one that discusses the rest of what the user asked for.
    /// </summary>
    private int CohesionOf(Candidate candidate, List<Candidate> tied)
    {
        var article = _client.FetchExtract(candidate.Title);
        if (article.Length == 0)
            return 0;

        return tied.Count(other =>
            !string.Equals(other.Title, candidate.Title, StringComparison.OrdinalIgnoreCase) &&
            article.Contains(other.Title, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Drops the candidates that cannot be a domain at all, judged by their own opening sentence.
    /// </summary>
    /// <remarks>
    /// Two kinds, and both are failure modes of the FULL encyclopedia rather than of the ranking:
    /// <list type="bullet">
    /// <item><b>Pages that only point at other pages.</b> A one-word domain term is very often the
    /// title of a disambiguation page, which then wins the vote outright and enriches to nothing,
    /// because it defines nothing.</item>
    /// <item><b>Pages that define an abstraction.</b> "a system to manage appointment schedules for
    /// a clinic" seeds the word system, which resolves exactly to <em>System</em> — systems theory —
    /// and that tied with every other seed's exact hit and won on being first. The domain came out
    /// as one class called Structure.</item>
    /// </list>
    /// The abstraction test is not a new judgement: <see cref="EntityShaper"/> already refuses
    /// System as a CLASS, by this very lexicon, and a thing that cannot be a class certainly cannot
    /// be the whole domain. Asking the same question here costs nothing — the leads are already
    /// fetched for the disambiguation test.
    /// </remarks>
    /// <remarks>
    /// One batched call, not one per candidate — the HTTP fallback pays a round trip for each, and
    /// <see cref="IWikipediaClient.FetchLeads"/> exists to keep that at one.
    /// </remarks>
    private void RemoveUnusableTopics(List<Candidate> candidates)
    {
        var leads = _client.FetchLeads(candidates.Select(c => c.Title).ToList());

        candidates.RemoveAll(candidate =>
        {
            // No lead is no evidence, and no evidence is not a reason to drop anything.
            if (!leads.TryGetValue(candidate.Title, out var lead) || lead.Length == 0)
                return false;

            return DisambiguationLead.IsMatch(lead.TrimEnd()) || DefinesAnAbstraction(candidate.Title, lead);
        });
    }

    /// <summary>
    /// Whether the article's own definition makes its subject an abstraction rather than a thing.
    /// </summary>
    /// <remarks>
    /// The curated lexicon outranks the archetype, exactly as it does across
    /// <see cref="EntityShaper"/>'s passes — and that precedence is the whole reason Library
    /// survives. "A library is a collection of books" resolves through the head "collection" to
    /// Abstract, yet a library is precisely the sort of thing people build apps about, and
    /// <see cref="DomainFieldLexicon"/> says so outright. Consulting the archetype alone here would
    /// refuse every library domain.
    /// </remarks>
    private static bool DefinesAnAbstraction(string title, string lead)
    {
        if (DomainFieldLexicon.Fields(NameNormalizer.ToEntityName(title)).Count > 0)
            return false;

        return ArchetypeLexicon.Resolve(HypernymExtractor.Extract(lead)).Verdict == ShapeVerdict.Abstract;
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

        /// <summary>
        /// A searched term IS this article's title — the user named it, rather than a redirect
        /// leading here from a word that means something else somewhere else.
        /// </summary>
        public bool WasNamedOutright { get; set; }
    }
}

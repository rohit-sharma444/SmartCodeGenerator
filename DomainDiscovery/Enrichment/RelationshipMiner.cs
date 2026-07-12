using System.Text.RegularExpressions;
using DomainDiscovery.Model;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Fills <see cref="CandidateEntity.Relationships"/> — a structure that has existed on the
/// model since it was written and that no production code populated before this — by mining
/// same-sentence co-occurrence out of the topic article the classes were discovered from.
/// </summary>
/// <remarks>
/// <para>
/// Measured on the cached Point of sale article (299 sentences, 20 shaped classes): 72 class
/// pairs co-occur in a sentence, and the strongest are exactly what a point-of-sale model
/// needs (Customer~Order, Customer~Payment, Payment~PaymentTerminal...). The same measurement
/// also exposed two ways a naive reading of "mentioned together" goes wrong, which is why the
/// two rules below (overlap exclusion, the recurrence threshold) exist — they are not
/// simplifications waiting to happen, they are the fix for a specific wrong answer this class
/// used to produce.
/// </para>
/// <para>
/// This is additive and best-effort by construction: every failure mode (blank article, no
/// pair reaching the threshold, an exception from a malformed input) leaves
/// <paramref name="model"/> — see <see cref="Mine"/> — exactly as it was. A relationship miner
/// that could make the model worse than twenty standalone classes would be a regression, not a
/// feature.
/// </para>
/// </remarks>
public sealed class RelationshipMiner
{
    /// <summary>
    /// The minimum number of sentences a pair of classes must be named together in before a
    /// relationship is emitted. Measured necessity, not a tuning knob: one rambling sentence
    /// naming merchant, customer, cash, payment and register at once manufactures a pair for
    /// every combination of those names, and on the real article that single sentence was
    /// behind half the top-scoring pairs. Requiring the pair to recur is what tells a genuine
    /// association (Customer~Order, mentioned together ten times) apart from that accident.
    /// </summary>
    public const int MinCoOccurrences = 3;

    /// <summary>
    /// The maximum number of outgoing relationships kept per referencing class, highest
    /// co-occurrence count first. A class with eight foreign keys is not a clearer model than
    /// one with three — this caps the noisiest classes down to their strongest associations
    /// rather than emitting every pair that cleared <see cref="MinCoOccurrences"/>.
    /// </summary>
    public const int MaxLinksPerEntity = 3;

    /// <summary>
    /// Splits prose into sentences. Deliberately simple (no abbreviation handling): the miner's
    /// job is to find recurring co-occurrence, not to parse English perfectly, and an
    /// occasional mis-split sentence only shifts which sentence a mention lands in, not whether
    /// two names recur together often enough to matter.
    /// </summary>
    private static readonly Regex SentenceBoundary = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    /// <summary>
    /// Mines <paramref name="articleText"/> for same-sentence co-occurrence between the classes
    /// already in <paramref name="model"/> and mutates it in place: a surviving pair gets a
    /// <c>references</c> relationship and an <c>int</c> foreign-key field on whichever class's
    /// <see cref="EntityRole"/> outranks the other (see <see cref="EntityRoleRank"/>). Never
    /// throws, and never leaves the model in a different state than it started in unless a pair
    /// actually clears every rule — blank text, too few mentions, equal-ranked classes and
    /// <see cref="EntityRole.None"/> classes all fall out of the algorithm as a no-op rather
    /// than needing a special case.
    /// </summary>
    public void Mine(CandidateDomainModel model, string articleText)
    {
        try
        {
            if (model is null || string.IsNullOrWhiteSpace(articleText))
                return;

            // Longest phrase first: this order is what lets each sentence's matching consume
            // "cash register" before "cash" ever gets a turn, which is the whole fix for the
            // Cash~CashRegister phantom (see MineSentence).
            var candidates = model.Entities
                .Select(e => (Entity: e, Phrase: e.SourceTitle ?? e.Name))
                .Where(c => !string.IsNullOrWhiteSpace(c.Phrase))
                .OrderByDescending(c => c.Phrase.Length)
                .ToList();

            if (candidates.Count < 2)
                return;

            var pairCounts = new Dictionary<(CandidateEntity, CandidateEntity), int>();

            foreach (var sentence in SentenceBoundary.Split(articleText))
            {
                if (string.IsNullOrWhiteSpace(sentence))
                    continue;

                var mentioned = MentionsIn(sentence, candidates);

                for (var i = 0; i < mentioned.Count; i++)
                for (var j = i + 1; j < mentioned.Count; j++)
                {
                    var key = PairKey(mentioned[i], mentioned[j]);
                    pairCounts[key] = pairCounts.TryGetValue(key, out var count) ? count + 1 : 1;
                }
            }

            var proposals = DirectedProposals(pairCounts);

            foreach (var group in proposals.GroupBy(p => p.Referencing))
            {
                var kept = group
                    .OrderByDescending(p => p.Count)
                    .ThenBy(p => p.Target.Name, StringComparer.Ordinal)
                    .Take(MaxLinksPerEntity);

                foreach (var link in kept)
                    Apply(link.Referencing, link.Target);
            }
        }
        catch
        {
            // Best-effort by contract: any surprise here (a pathological regex input, for
            // instance) must leave the model exactly as EntityShaper left it, not crash the
            // pipeline that has already done useful work.
        }
    }

    /// <summary>
    /// The distinct classes mentioned in one sentence, applying the overlap-exclusion rule:
    /// candidates are tried longest-phrase-first, and a shorter phrase may not match inside a
    /// character span a longer phrase already consumed. Without this, "the cash register
    /// prints a receipt" reads as a mention of both CashRegister AND Cash, because "cash" is a
    /// literal substring of "cash register" — measured as the single highest-scoring (and
    /// entirely wrong) pair in the real article.
    /// </summary>
    private static List<CandidateEntity> MentionsIn(
        string sentence, List<(CandidateEntity Entity, string Phrase)> candidates)
    {
        var mentioned = new List<CandidateEntity>();
        var consumed = new List<(int Start, int End)>();

        foreach (var (entity, phrase) in candidates)
        {
            // Word boundary on both ends, with an optional trailing "s" so "receipt" also
            // matches "receipts" — prose refers to a class in the plural just as often.
            var pattern = $@"\b{Regex.Escape(phrase)}s?\b";
            var spans = new List<(int Start, int End)>();

            foreach (Match match in Regex.Matches(sentence, pattern, RegexOptions.IgnoreCase))
            {
                var span = (Start: match.Index, End: match.Index + match.Length);
                if (!consumed.Any(c => span.Start < c.End && c.Start < span.End))
                    spans.Add(span);
            }

            if (spans.Count == 0)
                continue;

            mentioned.Add(entity);
            consumed.AddRange(spans);
        }

        return mentioned;
    }

    /// <summary>An unordered pair's dictionary key, ordered by name so (A, B) and (B, A) always
    /// land on the same entry regardless of which order a sentence mentioned them in.</summary>
    private static (CandidateEntity, CandidateEntity) PairKey(CandidateEntity a, CandidateEntity b)
        => string.CompareOrdinal(a.Name, b.Name) <= 0 ? (a, b) : (b, a);

    /// <summary>
    /// Turns surviving co-occurrence counts into directed (referencing -> target) proposals,
    /// applying the threshold and the direction rule from <see cref="EntityRoleRank"/>: the
    /// higher rank references the lower, equal ranks link neither way (Customer~Merchant, both
    /// Party, is the honest "no relationship" rather than a guessed one), and
    /// <see cref="EntityRole.None"/> never links regardless of how the ranks compare (rank 0
    /// meeting rank 1 differs, but None is still never a target or a source).
    /// </summary>
    private static List<(CandidateEntity Referencing, CandidateEntity Target, int Count)> DirectedProposals(
        Dictionary<(CandidateEntity, CandidateEntity), int> pairCounts)
    {
        var proposals = new List<(CandidateEntity, CandidateEntity, int)>();

        foreach (var ((a, b), count) in pairCounts)
        {
            if (count < MinCoOccurrences)
                continue;

            if (a.Role == EntityRole.None || b.Role == EntityRole.None)
                continue;

            var rankA = EntityRoleRank.RankOf(a.Role);
            var rankB = EntityRoleRank.RankOf(b.Role);

            if (rankA == rankB)
                continue;

            proposals.Add(rankA > rankB ? (a, b, count) : (b, a, count));
        }

        return proposals;
    }

    /// <summary>
    /// Records one surviving relationship: the structural link, unconditionally, plus a
    /// best-effort <c>&lt;Target&gt;Id</c> field. The field is skipped — never the relationship
    /// — when it would collide with a field the entity already has, would be literally named
    /// "Id" (which is generated, not declared: see <see cref="Review.ModelEditor.AddField"/>),
    /// or would equal the referencing entity's own name (<c>class Order { int Order }</c> is
    /// CS0542). These are the exact same guards <see cref="Review.ModelEditor.AddField"/>
    /// applies to a user-typed field name; a mined field is not exempt from any of them.
    /// </summary>
    private static void Apply(CandidateEntity referencing, CandidateEntity target)
    {
        referencing.Relationships.Add(new CandidateRelationship { Target = target.Name, Role = "references" });

        var fieldName = $"{target.Name}Id";

        if (referencing.Fields.Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
            return;

        if (string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(fieldName, referencing.Name, StringComparison.OrdinalIgnoreCase))
            return;

        referencing.Fields.Add(new CandidateField
        {
            Name = fieldName,
            Type = "int",
            Provenance = referencing.Provenance
        });
    }
}

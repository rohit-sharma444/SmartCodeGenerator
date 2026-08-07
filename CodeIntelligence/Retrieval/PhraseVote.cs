namespace CodeIntelligence.Retrieval;

/// <summary>One labelled corpus phrase and how close it sat to the query.</summary>
public sealed record ScoredPhrase<TLabel>(long Id, string Text, TLabel Label, double Score);

/// <summary>A label the neighbours voted for, with the winner's share of the vote.</summary>
public sealed record PhraseDecision<TLabel>(
    TLabel Label, double Confidence, IReadOnlyList<ScoredPhrase<TLabel>> Evidence);

/// <summary>Brute-force k-NN over labelled phrases, behind TWO independent gates.
///
/// This is the one implementation of the gate semantics. <see cref="NeighbourVote"/>
/// wraps it for <c>IntentType</c>; the build-app review loop wraps it for its verbs.
/// A second copy would drift, and the thresholds are calibrated against this behaviour
/// specifically — see AiConfig's constants.
///
/// <paramref name="minTopScore"/> asks "is anything actually similar?" and
/// <paramref name="floor"/> asks "do the neighbours agree?". They must stay separate:
/// share-of-vote alone answers only the second, so a single weak positive match takes
/// 100% of the vote and clears any floor.
///
/// <paramref name="floor"/> is INCLUSIVE and must therefore be set ABOVE 0.5, or a
/// two-way tie — which computes to exactly 0.5 — is accepted instead of declined.</summary>
public static class PhraseVote
{
    public static PhraseDecision<TLabel>? Decide<TLabel>(
        IReadOnlyList<(long Id, string Text, TLabel Label, float[] Vector)> corpus,
        float[] query, int topK, double floor, double minTopScore)
        where TLabel : notnull
    {
        if (corpus.Count == 0 || query.Length == 0)
            return null;

        var neighbours = corpus
            .Select(v => new ScoredPhrase<TLabel>(
                v.Id, v.Text, v.Label, Similarity.Cosine(v.Vector, query)))
            .Where(e => e.Score > 0)
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.Text, StringComparer.Ordinal)
            .ThenBy(e => Key(e.Label), StringComparer.Ordinal)
            .Take(Math.Max(1, topK))
            .ToList();

        if (neighbours.Count == 0)
            return null;

        // Gate 1: is anything actually close? A lone weak match would otherwise take
        // 100% of the vote share and clear any floor.
        if (neighbours[0].Score < minTopScore)
            return null;

        var totals = neighbours
            .GroupBy(e => e.Label)
            .Select(g => (Label: g.Key, Score: g.Sum(e => e.Score)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Key(x.Label), StringComparer.Ordinal)
            .ToList();

        var overall = neighbours.Sum(e => e.Score);
        var winner = totals[0];
        var confidence = overall == 0 ? 0 : winner.Score / overall;

        // Gate 2: do the neighbours agree? Inclusive — see the remarks above on why
        // floor must be set above 0.5.
        if (confidence < floor)
            return null;

        return new PhraseDecision<TLabel>(winner.Label, confidence, neighbours);
    }

    private static string Key<TLabel>(TLabel label) => label?.ToString() ?? string.Empty;
}

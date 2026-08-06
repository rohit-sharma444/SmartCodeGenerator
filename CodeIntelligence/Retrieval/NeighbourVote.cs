using CodeIntelligence.Abstractions;

namespace CodeIntelligence.Retrieval;

/// <summary>Brute-force k-NN over the corpus. Confidence is the winning intent's share
/// of the total neighbour score, so a unanimous neighbourhood approaches 1 and an
/// evenly split one approaches 1/n. Below the floor we return null — declining is
/// always better than a confident wrong answer, because the user's next step after a
/// decline is the familiar "did you mean" line.</summary>
public static class NeighbourVote
{
    public static AiResolution? Decide(
        IReadOnlyList<StoredVector> corpus, float[] query, int topK, double floor)
    {
        if (corpus.Count == 0 || query.Length == 0)
            return null;

        var neighbours = corpus
            .Select(v => new RetrievedExample(v.DocumentId, v.Text, v.Intent, Similarity.Cosine(v.Vector, query)))
            .Where(e => e.Score > 0)
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.Text, StringComparer.Ordinal)
            .Take(Math.Max(1, topK))
            .ToList();

        if (neighbours.Count == 0)
            return null;

        var totals = neighbours
            .GroupBy(e => e.Intent)
            .Select(g => (Intent: g.Key, Score: g.Sum(e => e.Score)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Intent.ToString(), StringComparer.Ordinal)
            .ToList();

        var overall = neighbours.Sum(e => e.Score);
        var winner = totals[0];
        var confidence = overall == 0 ? 0 : winner.Score / overall;

        if (confidence < floor)
            return null;

        var rationale =
            $"{neighbours.Count} nearest examples, {winner.Intent} holding " +
            $"{confidence:P0} of the vote (closest: \"{neighbours[0].Text}\").";

        return new AiResolution(winner.Intent, confidence, neighbours, rationale);
    }
}

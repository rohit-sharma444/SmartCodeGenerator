using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;

namespace CodeIntelligence.Retrieval;

/// <summary>Brute-force k-NN over the corpus, behind TWO independent gates.
///
/// The gate logic itself lives in <see cref="PhraseVote"/>, which the build-app review
/// loop also uses; this type is the <c>IntentType</c> face of it, adding the evidence
/// shape and rationale string that <c>AiResolution</c> carries. Keeping one voting
/// implementation is what stops the two callers drifting apart, since the thresholds
/// are calibrated against that behaviour specifically.
///
/// `minTopScore` asks "is anything actually similar?" and `floor` asks "do the
/// neighbours agree?" — they must stay separate: if exactly one corpus entry scores
/// positive (say cosine 0.02, noise), it takes 100% of the vote and clears any floor.
///
/// `floor` is INCLUSIVE (confidence == floor is accepted) and therefore must be set
/// ABOVE 0.5, or a two-way ambiguous phrase — which computes to exactly 0.5 — would be
/// accepted instead of declined. Declining is always better than a confident wrong
/// answer: the user's next step after a decline is the familiar "did you mean" line.</summary>
public static class NeighbourVote
{
    public static AiResolution? Decide(
        IReadOnlyList<StoredVector> corpus, float[] query, int topK, double floor, double minTopScore)
    {
        var decision = PhraseVote.Decide(
            [.. corpus.Select(v => (v.DocumentId, v.Text, v.Intent, v.Vector))],
            query, topK, floor, minTopScore);

        if (decision is null)
            return null;

        var evidence = decision.Evidence
            .Select(e => new RetrievedExample(e.Id, e.Text, e.Label, e.Score))
            .ToList();

        var rationale =
            $"{evidence.Count} nearest examples, {decision.Label} holding " +
            $"{decision.Confidence:P0} of the vote (closest: \"{evidence[0].Text}\").";

        return new AiResolution(decision.Label, decision.Confidence, evidence, rationale);
    }
}

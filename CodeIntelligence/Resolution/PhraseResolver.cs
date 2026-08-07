using CodeIntelligence.Abstractions;
using CodeIntelligence.Retrieval;

namespace CodeIntelligence.Resolution;

/// <summary>What the phrase index made of some input: the label it voted for, the
/// winner's share of the vote, and the nearest example that supported it.
///
/// <paramref name="ClosestScore"/> is the raw cosine of that nearest example and is the
/// honest measure of "was anything actually close". Over a small catalog it is the ONLY
/// one that separates a real paraphrase from nonsense: with labels spread far apart,
/// the neighbours agree even on garbage, so <paramref name="Confidence"/> — a share of
/// the vote — reads high for input the index has never seen anything like.</summary>
public sealed record PhraseSuggestion(
    string Label, double Confidence, string ClosestText, double ClosestScore);

/// <summary>Retrieval over a small, fixed catalog of labelled phrasings — the same
/// embedder, similarity and two gates as the main intent path, but over a caller's own
/// vocabulary rather than <c>IntentType</c>.
///
/// Built for the build-app review loop, whose eight verbs are a closed set the main
/// corpus knows nothing about. The catalog is embedded once at construction; at ~40
/// short phrases that is a few milliseconds, and it keeps <see cref="Resolve"/> to one
/// embed plus a linear scan.
///
/// Like <c>IIntentResolver</c>, this MUST NOT throw: a failure is a null return, and
/// the caller then behaves exactly as it did before the AI layer existed.</summary>
public sealed class PhraseResolver
{
    private readonly IEmbeddingProvider _embedder;
    private readonly List<(long Id, string Text, string Label, float[] Vector)> _corpus = [];
    private readonly int _topK;
    private readonly double _floor;
    private readonly double _minTopScore;

    public PhraseResolver(
        IEmbeddingProvider embedder,
        IReadOnlyList<(string Text, string Label)> phrases,
        int topK, double floor, double minTopScore)
    {
        _embedder = embedder;
        _topK = topK;
        _floor = floor;
        _minTopScore = minTopScore;

        long id = 0;
        foreach (var (text, label) in phrases)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label))
                continue;

            _corpus.Add((id++, text, label, Similarity.Normalize(embedder.Embed(text))));
        }
    }

    /// <summary>The labels this resolver can return. Exposed so a caller can assert its
    /// catalog covers every command it dispatches — a label with no phrasings is a
    /// suggestion that can never be made.</summary>
    public IReadOnlyCollection<string> Labels => [.. _corpus.Select(c => c.Label).Distinct()];

    public PhraseSuggestion? Resolve(string input)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input) || _corpus.Count == 0)
                return null;

            var query = Similarity.Normalize(_embedder.Embed(input));
            var decision = PhraseVote.Decide(_corpus, query, _topK, _floor, _minTopScore);

            return decision is null
                ? null
                : new PhraseSuggestion(
                    decision.Label, decision.Confidence,
                    decision.Evidence[0].Text, decision.Evidence[0].Score);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

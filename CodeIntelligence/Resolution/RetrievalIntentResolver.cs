using System.Diagnostics;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Retrieval;

namespace CodeIntelligence.Resolution;

/// <summary>Embeds the input and votes over its nearest corpus neighbours.
///
/// Every failure path returns null, which callers render as today's "Command not
/// recognized" message. This class must not throw: it runs inside the REPL loop, and
/// an escaping exception would turn an unrecognized command into a crash.
///
/// The corpus is loaded once and cached — it is a few thousand short strings, and
/// re-reading SQLite per keystroke-command would dominate the latency budget.</summary>
public sealed class RetrievalIntentResolver : IIntentResolver
{
    private readonly IVectorStore _store;
    private readonly IEmbeddingProvider _embedder;
    private readonly int _topK;
    private readonly double _floor;
    private readonly double _minTopScore;
    private readonly TimeSpan _budget;

    private IReadOnlyList<StoredVector>? _corpus;

    public RetrievalIntentResolver(
        IVectorStore store, IEmbeddingProvider embedder,
        int topK, double floor, double minTopScore, TimeSpan budget)
    {
        _store = store;
        _embedder = embedder;
        _topK = topK;
        _floor = floor;
        _minTopScore = minTopScore;
        _budget = budget;
    }

    /// <summary>Non-null when retrieval is unavailable, e.g. "reindex required".
    /// Surfaced by `ai status`; never printed per command.</summary>
    public string? Unavailable { get; private set; }

    public AiResolution? Resolve(string input, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input) || ct.IsCancellationRequested)
                return null;

            var status = _store.GetStatus(_embedder.ModelId);
            if (status.ReindexRequired)
            {
                Unavailable = status.DocumentCount == 0
                    ? "index is empty - run 'ai reindex'"
                    : $"index was built with '{status.IndexedModelId}' but '{_embedder.ModelId}' is loaded - run 'ai reindex'";
                return null;
            }

            _corpus ??= _store.LoadAll(_embedder.ModelId);
            if (_corpus.Count == 0)
                return null;

            var watch = Stopwatch.StartNew();
            var query = Similarity.Normalize(_embedder.Embed(input));

            if (ct.IsCancellationRequested || watch.Elapsed > _budget)
                return null;

            Unavailable = null;
            return NeighbourVote.Decide(_corpus, query, _topK, _floor, _minTopScore);
        }
        catch (Exception ex)
        {
            Unavailable = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }
}

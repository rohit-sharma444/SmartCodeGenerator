using CodeGenerationEngine.Models;

namespace CodeIntelligence.Abstractions;

public sealed record StoredVector(long DocumentId, string Text, IntentType Intent, float[] Vector);

/// <summary>What `ai status` reports. <paramref name="ReindexRequired"/> is true when the
/// index is empty or was built by a different model than the one now loaded.</summary>
public sealed record IndexStatus(
    int DocumentCount,
    string? IndexedModelId,
    int Dimension,
    bool ReindexRequired,
    DateTime? BuiltUtc);

public interface IVectorStore
{
    IndexStatus GetStatus(string currentModelId);
    IReadOnlyList<StoredVector> LoadAll(string modelId);
    void ReplaceAll(string modelId, int dimension, IReadOnlyList<(CorpusDocument Doc, float[] Vector)> rows);
}

namespace CodeIntelligence.Abstractions;

public interface ITokenizer
{
    IReadOnlyList<int> Encode(string text);
}

/// <summary>Turns text into a unit-length vector. ModelId identifies the weights:
/// vectors from different ModelIds are NOT comparable, so it is stored per row.</summary>
public interface IEmbeddingProvider : IDisposable
{
    string ModelId { get; }
    int Dimension { get; }
    float[] Embed(string text);
}

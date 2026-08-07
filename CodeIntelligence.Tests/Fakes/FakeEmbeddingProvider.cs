using CodeIntelligence.Abstractions;

namespace CodeIntelligence.Tests.Fakes;

/// <summary>Deterministic stand-in for the ONNX embedder: a bag-of-words vector over a
/// fixed vocabulary. Lets resolver and seam tests run without model files.</summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    private static readonly string[] Vocabulary =
        ["list", "show", "create", "delete", "class", "classes", "project", "property", "method"];

    public string ModelId { get; init; } = "fake-v1";
    public int Dimension => Vocabulary.Length;
    public Func<string, float[]>? OnEmbed { get; init; }

    public float[] Embed(string text)
    {
        if (OnEmbed is not null)
            return OnEmbed(text);

        var words = (text ?? string.Empty)
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var vector = new float[Vocabulary.Length];
        for (var i = 0; i < Vocabulary.Length; i++)
            vector[i] = words.Count(w => w == Vocabulary[i]);

        return vector;
    }

    public void Dispose() { }
}

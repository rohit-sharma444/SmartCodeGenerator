using CodeIntelligence.Abstractions;
using Microsoft.ML.Tokenizers;

namespace CodeIntelligence.Onnx;

/// <summary>Wraps the library WordPiece tokenizer behind our own interface. The
/// boundary is ours and stays swappable; the fiddly parts — unknown-token fallback,
/// max-chars-per-word, accent stripping — are not ours to get subtly wrong, and a
/// subtle tokenizer bug degrades retrieval silently rather than loudly.</summary>
public sealed class BertTokenizerAdapter : ITokenizer
{
    private readonly BertTokenizer _tokenizer;

    public BertTokenizerAdapter(string vocabPath)
        => _tokenizer = BertTokenizer.Create(vocabPath);

    public IReadOnlyList<int> Encode(string text)
        => _tokenizer.EncodeToIds(text ?? string.Empty);
}

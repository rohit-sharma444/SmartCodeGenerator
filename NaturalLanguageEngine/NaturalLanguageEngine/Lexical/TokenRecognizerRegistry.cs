using NaturalLanguageEngine.Lexical.Tokenizers;

namespace NaturalLanguageEngine.Lexical;

/// <summary>
/// Represents a registry for token recognizers, allowing for the registration and retrieval of token recognizers in a prioritized order.
/// </summary>
public sealed class TokenRecognizerRegistry
{
    private readonly List<ITokenRecognizer> _recognizers = new();

    public TokenRecognizerRegistry Register(ITokenRecognizer recognizer)
    {
        _recognizers.Add(recognizer);
        return this;
    }

    public IReadOnlyList<ITokenRecognizer> Build()
    {
        return _recognizers
            .OrderByDescending(r => r.Priority)
            .ToList();
    }
}
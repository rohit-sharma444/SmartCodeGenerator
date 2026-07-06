using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Lexical;

/// <summary>
/// Factory class for creating Token instances.
/// </summary>
internal static class TokenFactory
{
    public static Token Create(
        string text,
        TokenType type,
        int start,
        int length)
    {
        return new Token
        {
            Text = text,
            NormalizedText = text,
            Type = type,
            Span = new CharacterSpan(start, length)
        };
    }
}
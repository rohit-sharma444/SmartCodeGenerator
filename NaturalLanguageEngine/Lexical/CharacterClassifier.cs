namespace NaturalLanguageEngine.Lexical;

/// <summary>
/// Provides methods for classifying characters based on their properties, such as whether they are letters, digits, whitespace, punctuation, or other specific character types.
/// </summary>
internal static class CharacterClassifier
{
    public static bool IsLetter(char c)
        => char.IsLetter(c);

    public static bool IsDigit(char c)
        => char.IsDigit(c);

    public static bool IsWhiteSpace(char c)
        => char.IsWhiteSpace(c);

    public static bool IsLetterOrDigit(char c)
        => char.IsLetterOrDigit(c);

    public static bool IsSentenceTerminator(char c)
        => c == '.'
        || c == '!'
        || c == '?';

    public static bool IsQuotation(char c)
        => c == '"'
        || c == '\'';

    public static bool IsWordConnector(char c)
        => c == '_'
        || c == '-'
        || c == '\'';

    public static bool IsPunctuation(char c)
        => char.IsPunctuation(c);
}
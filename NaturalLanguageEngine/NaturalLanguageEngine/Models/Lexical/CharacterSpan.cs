namespace NaturalLanguageEngine.Models.Lexical;

/// <summary>
/// Represents a location inside the original text.
/// </summary>
public readonly record struct CharacterSpan(
    int Start,
    int Length)
{
    public int End => Start + Length;
}
using NaturalLanguageEngine.Models.Enums;

namespace NaturalLanguageEngine.Models.Lexical;

public sealed class Token
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;

    public string NormalizedText { get; set; } = string.Empty;

    public TokenType Type { get; set; }

    public CharacterSpan Span { get; set; }

    public override string ToString() => Text;
}
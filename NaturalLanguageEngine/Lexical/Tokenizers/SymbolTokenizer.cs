using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class SymbolTokenizer : ITokenRecognizer
{
    public int Priority => 400;

    public bool CanRead(ICharacterReader reader)
    {
        char c = reader.Current;

        return !char.IsLetterOrDigit(c)
            && !char.IsWhiteSpace(c)
            && !char.IsPunctuation(c);
    }

    public Token Read(ICharacterReader reader)
    {
        var token = TokenFactory.Create(
            reader.Current.ToString(),
            TokenType.Symbol,
            reader.Position,
            1);

        reader.MoveNext();

        return token;
    }
}
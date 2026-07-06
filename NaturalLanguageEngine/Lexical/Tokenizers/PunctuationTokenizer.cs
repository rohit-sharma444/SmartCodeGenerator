using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class PunctuationTokenizer : ITokenRecognizer
{
    public int Priority => 600;

    public bool CanRead(ICharacterReader reader)
    {
        return CharacterClassifier.IsPunctuation(reader.Current);
    }

    public Token Read(ICharacterReader reader)
    {
        var token = TokenFactory.Create(
            reader.Current.ToString(),
            TokenType.Punctuation,
            reader.Position,
            1);

        reader.MoveNext();

        return token;
    }
}
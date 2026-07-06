using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using System.Text;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class WordTokenizer : ITokenRecognizer
{
    public int Priority => 500;

    public bool CanRead(ICharacterReader reader)
    {
        return CharacterClassifier.IsLetter(reader.Current);
    }

    public Token Read(ICharacterReader reader)
    {
        var builder = new StringBuilder();

        int start = reader.Position;

        while (!reader.EndOfInput)
        {
            char c = reader.Current;

            if (!CharacterClassifier.IsLetterOrDigit(c) &&
                !CharacterClassifier.IsWordConnector(c))
                break;

            builder.Append(c);

            if (!reader.MoveNext())
                break;
        }

        return TokenFactory.Create(
            builder.ToString(),
            TokenType.Word,
            start,
            builder.Length);
    }
}
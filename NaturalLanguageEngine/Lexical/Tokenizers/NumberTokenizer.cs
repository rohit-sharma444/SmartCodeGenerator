using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using System.Text;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class NumberTokenizer : ITokenRecognizer
{
    public int Priority => 700;

    public bool CanRead(ICharacterReader reader)
        => CharacterClassifier.IsDigit(reader.Current);

    public Token Read(ICharacterReader reader)
    {
        var builder = new StringBuilder();

        int start = reader.Position;

        bool decimalFound = false;

        while (!reader.EndOfInput)
        {
            char c = reader.Current;

            if (CharacterClassifier.IsDigit(c))
            {
                builder.Append(c);
            }
            else if (c == '.' && !decimalFound)
            {
                decimalFound = true;
                builder.Append(c);
            }
            else
            {
                break;
            }

            if (!reader.MoveNext())
                break;
        }

        return TokenFactory.Create(
            builder.ToString(),
            TokenType.Number,
            start,
            builder.Length);
    }
}
using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using System.Text;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class TimeTokenizer : ITokenRecognizer
{
    public int Priority => 850;

    /// <summary>
    /// Fires only on digit runs containing a ':' (e.g. 14:30, 9:05).
    /// </summary>
    public bool CanRead(ICharacterReader reader)
    {
        if (!char.IsDigit(reader.Current))
            return false;

        int offset = 1;

        while (offset <= 8)
        {
            char c = reader.Peek(offset);

            if (char.IsDigit(c))
            {
                offset++;
                continue;
            }

            return c == ':';
        }

        return false;
    }

    public Token Read(ICharacterReader reader)
    {
        int start = reader.Position;
        var sb = new StringBuilder();
        bool hasColon = false;

        while (!reader.EndOfInput)
        {
            char c = reader.Current;

            if (char.IsDigit(c))
                sb.Append(c);
            else if (c == ':' && !hasColon)
            {
                hasColon = true;
                sb.Append(c);
            }
            else
            {
                break;
            }

            if (!reader.MoveNext())
                break;
        }

        return TokenFactory.Create(
            sb.ToString(),
            TokenType.Time,
            start,
            sb.Length);
    }
}

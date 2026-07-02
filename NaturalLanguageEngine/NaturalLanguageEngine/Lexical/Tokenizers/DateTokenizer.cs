using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using System.Text;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class DateTokenizer : ITokenRecognizer
{
    public int Priority => 900;

    /// <summary>
    /// Fires only on digit runs that contain a '/' or '-' date separator,
    /// e.g. 2024-06-30 or 12/31/2025. Plain numbers fall through to the
    /// number recognizer.
    /// </summary>
    public bool CanRead(ICharacterReader reader)
    {
        if (!char.IsDigit(reader.Current))
            return false;

        int offset = 1;
        bool sawSeparator = false;

        while (offset <= 32)
        {
            char c = reader.Peek(offset);

            if (char.IsDigit(c))
            {
                offset++;
                continue;
            }

            if (c == '/' || c == '-')
            {
                sawSeparator = true;
                offset++;
                continue;
            }

            break;
        }

        return sawSeparator;
    }

    public Token Read(ICharacterReader reader)
    {
        int start = reader.Position;
        var sb = new StringBuilder();

        while (!reader.EndOfInput)
        {
            char c = reader.Current;

            if (char.IsDigit(c) || c == '/' || c == '-')
                sb.Append(c);
            else
                break;

            if (!reader.MoveNext())
                break;
        }

        var text = sb.ToString().TrimEnd('-');

        return TokenFactory.Create(
            text,
            TokenType.Date,
            start,
            text.Length);
    }
}

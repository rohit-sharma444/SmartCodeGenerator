using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using System.Text;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class EmailTokenizer : ITokenRecognizer
{
    public int Priority => 950;

    /// <summary>
    /// Only commits when the upcoming run (up to whitespace) actually
    /// looks like an email: contains an '@' followed later by a '.'.
    /// This prevents the recognizer from swallowing ordinary words.
    /// </summary>
    public bool CanRead(ICharacterReader reader)
    {
        if (!char.IsLetterOrDigit(reader.Current))
            return false;

        int offset = 0;
        int atOffset = -1;

        while (true)
        {
            char c = offset == 0 ? reader.Current : reader.Peek(offset);

            if (c == '\0' || char.IsWhiteSpace(c))
                break;

            if (c == '@')
            {
                if (atOffset >= 0)
                    return false; // two '@' -> not an email
                atOffset = offset;
            }
            else if (atOffset >= 0 && c == '.')
            {
                return true; // '@' ... '.' pattern found
            }

            offset++;

            if (offset > 256)
                break;
        }

        return false;
    }

    public Token Read(ICharacterReader reader)
    {
        int start = reader.Position;
        var sb = new StringBuilder();

        while (!reader.EndOfInput)
        {
            char c = reader.Current;

            if (char.IsWhiteSpace(c) || !IsEmailChar(c))
                break;

            sb.Append(c);

            if (!reader.MoveNext())
                break;
        }

        // Trim a trailing dot that belongs to sentence punctuation, not the address.
        var text = sb.ToString().TrimEnd('.');
        var type = text.Contains('@') ? TokenType.Email : TokenType.Word;

        return TokenFactory.Create(text, type, start, text.Length);
    }

    private static bool IsEmailChar(char c)
        => char.IsLetterOrDigit(c)
        || c is '@' or '.' or '_' or '-' or '+';
}

using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using System.Text;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class UrlTokenizer : ITokenRecognizer
{
    public int Priority => 1000;

    public bool CanRead(ICharacterReader reader)
    {
        // Match http:// or https:// (case-insensitive on the scheme).
        if (reader.Current is not ('h' or 'H'))
            return false;

        return (reader.Peek(1) is 't' or 'T')
            && (reader.Peek(2) is 't' or 'T')
            && (reader.Peek(3) is 'p' or 'P');
    }

    public Token Read(ICharacterReader reader)
    {
        int start = reader.Position;
        var sb = new StringBuilder();

        while (!reader.EndOfInput)
        {
            char c = reader.Current;

            if (char.IsWhiteSpace(c))
                break;

            sb.Append(c);

            if (!reader.MoveNext())
                break;
        }

        // Strip trailing sentence punctuation that isn't part of the URL.
        var text = sb.ToString().TrimEnd('.', ',', ';', ':', '!', '?', ')');

        return TokenFactory.Create(
            text,
            TokenType.Url,
            start,
            text.Length);
    }
}

using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using System.Text;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class QuotedStringTokenizer : ITokenRecognizer
{
    public int Priority => 800;

    public bool CanRead(ICharacterReader reader)
    {
        return reader.Current == '"' || reader.Current == '\'';
    }

    public Token Read(ICharacterReader reader)
    {
        int start = reader.Position;

        char quote = reader.Current;

        var sb = new StringBuilder();

        sb.Append(quote);

        while (reader.MoveNext())
        {
            char c = reader.Current;

            sb.Append(c);

            if (c == quote)
                break;
        }

        return TokenFactory.Create(
            sb.ToString(),
            TokenType.QuotedString,
            start,
            sb.Length);
    }
}
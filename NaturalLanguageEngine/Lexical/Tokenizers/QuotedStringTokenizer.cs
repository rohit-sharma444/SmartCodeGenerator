using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using System;
using System.Text;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

internal sealed class QuotedStringTokenizer : ITokenRecognizer
{
    public int Priority => 800;

    public bool CanRead(ICharacterReader reader)
    {
        return CharacterClassifier.IsQuotation(reader.Current)
            && TryFindClosingQuote(reader, out _);
    }

    public Token Read(ICharacterReader reader)
    {
        if (!TryFindClosingQuote(reader, out int closingOffset))
            throw new InvalidOperationException(
                "Read called on a quote that opens no quoted string; call CanRead first.");

        int start = reader.Position;

        var sb = new StringBuilder(closingOffset + 1);

        sb.Append(reader.Current);

        for (int i = 0; i < closingOffset; i++)
        {
            reader.MoveNext();

            sb.Append(reader.Current);
        }

        // Step past the closing quote. Leaving the reader on it would let the
        // same character open a second quoted string.
        reader.MoveNext();

        return TokenFactory.Create(
            sb.ToString(),
            TokenType.QuotedString,
            start,
            sb.Length);
    }

    /// <summary>
    /// Finds the matching quote that closes the one under the reader, as an offset
    /// from it. A quote only opens a quoted string when its partner follows on the
    /// same line and in the same sentence; otherwise the character is an apostrophe
    /// or a lone scare quote, and pairing it with a distant delimiter would swallow
    /// the prose in between.
    /// </summary>
    private static bool TryFindClosingQuote(ICharacterReader reader, out int closingOffset)
    {
        char quote = reader.Current;

        for (int offset = 1; ; offset++)
        {
            char c = reader.Peek(offset);

            if (c == '\0'
                || c == '\n'
                || c == '\r'
                || CharacterClassifier.IsSentenceTerminator(c))
                break;

            if (c == quote && ClosesQuote(quote, reader.Peek(offset + 1)))
            {
                closingOffset = offset;

                return true;
            }
        }

        closingOffset = 0;

        return false;
    }

    /// <summary>
    /// An apostrophe followed by a letter is a contraction or possessive
    /// ("customer's"), not a closing quote.
    /// </summary>
    private static bool ClosesQuote(char quote, char next)
        => quote != '\'' || !CharacterClassifier.IsLetter(next);
}

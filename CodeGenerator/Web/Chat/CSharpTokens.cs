using System.Text;

namespace CodeGenerator.Web.Chat;

/// <summary>What a run of characters is, for colouring.</summary>
public enum TokenKind
{
    Plain,

    Keyword,

    /// <summary>A string or character literal. Not called String: that collides with the type
    /// name in a switch inside a .razor file.</summary>
    Text,

    Comment,

    Number
}

/// <summary>One run of characters and what it is.</summary>
public sealed record Token(TokenKind Kind, string Text);

/// <summary>
/// Splits C# into coloured runs, losing nothing.
/// </summary>
/// <remarks>
/// Hand-rolled because the page is self-contained — no CDN, no external highlighter, the same
/// constraint the workflow page already works under.
/// <para>
/// The invariant that matters is not correct colouring, it is that the runs concatenate back to
/// the input exactly. A highlighter that drops a character shows a user source they do not have,
/// and that is worse than showing them no colour at all. Every unterminated literal, every
/// trailing comment and every stray quote below is handled by running to the end of the input
/// rather than by giving up.
/// </para>
/// </remarks>
public static class CSharpTokens
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum",
        "event", "explicit", "false", "finally", "fixed", "float", "for", "foreach", "get",
        "if", "implicit", "in", "init", "int", "interface", "internal", "is", "lock", "long",
        "namespace", "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "record", "ref", "return", "sealed", "set", "short",
        "sizeof", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "ushort", "using", "var", "virtual", "void", "while", "yield"
    };

    public static IReadOnlyList<Token> Scan(string code)
    {
        var tokens = new List<Token>();
        var plain = new StringBuilder();
        var i = 0;

        void FlushPlain()
        {
            if (plain.Length == 0)
                return;

            tokens.Add(new Token(TokenKind.Plain, plain.ToString()));
            plain.Clear();
        }

        while (i < code.Length)
        {
            var c = code[i];

            // Comments. A line comment runs to the newline or the end; a block comment to its
            // terminator or the end. Neither gives up on an unterminated one.
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                FlushPlain();

                var end = code.IndexOf('\n', i);
                end = end < 0 ? code.Length : end;

                tokens.Add(new Token(TokenKind.Comment, code[i..end]));
                i = end;
                continue;
            }

            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                FlushPlain();

                var close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? code.Length : close + 2;

                tokens.Add(new Token(TokenKind.Comment, code[i..end]));
                i = end;
                continue;
            }

            // String and character literals, including the escape that would otherwise end them
            // early.
            if (c is '"' or '\'')
            {
                FlushPlain();

                var j = i + 1;

                while (j < code.Length)
                {
                    if (code[j] == '\\')
                    {
                        j += 2;
                        continue;
                    }

                    if (code[j] == c)
                    {
                        j++;
                        break;
                    }

                    j++;
                }

                var end = Math.Min(j, code.Length);

                tokens.Add(new Token(TokenKind.Text, code[i..end]));
                i = end;
                continue;
            }

            // Numbers, including a decimal point and a type suffix, so "1.20m" is one token.
            if (char.IsAsciiDigit(c) && (i == 0 || !IsWordChar(code[i - 1])))
            {
                FlushPlain();

                var j = i;

                while (j < code.Length && (char.IsAsciiDigit(code[j]) || code[j] == '.'
                                           || code[j] is 'm' or 'M' or 'd' or 'D' or 'f' or 'F'
                                           or 'L' or 'l' or 'u' or 'U'))
                {
                    j++;
                }

                // A word that merely starts with digits is not a number: 123abc is one plain run.
                if (j < code.Length && IsWordChar(code[j]))
                {
                    while (j < code.Length && IsWordChar(code[j]))
                        j++;

                    plain.Append(code[i..j]);
                    i = j;
                    continue;
                }

                tokens.Add(new Token(TokenKind.Number, code[i..j]));
                i = j;
                continue;
            }

            // Words. Matched whole, so an identifier beginning with a keyword is not coloured as
            // one — the classic way a highlighter starts painting variable names.
            if (IsWordStart(c))
            {
                var j = i;

                while (j < code.Length && IsWordChar(code[j]))
                    j++;

                var word = code[i..j];

                // Emitted as a run of its own either way, keyword or not. An identifier is the
                // thing a reader looks for, so it stays visible as one token rather than being
                // buried in whatever punctuation happened to surround it.
                FlushPlain();

                tokens.Add(new Token(
                    Keywords.Contains(word) ? TokenKind.Keyword : TokenKind.Plain, word));

                i = j;
                continue;
            }

            plain.Append(c);
            i++;
        }

        FlushPlain();

        return tokens;
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}

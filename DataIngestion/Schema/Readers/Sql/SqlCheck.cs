using System.Globalization;

namespace DataIngestion.Schema.Readers.Sql;

/// <summary>
/// What one column may hold under a CHECK constraint: a lowest and highest number, or a list of
/// values. Null members say nothing about the column.
/// </summary>
/// <param name="Allowed">The only values the column may hold, when the check lists them.</param>
/// <param name="AllowedAreText">Whether <paramref name="Allowed"/> came from text literals
/// ('Active') rather than numbers.</param>
public sealed record CheckBounds(
    decimal? Min = null,
    bool MinInclusive = true,
    decimal? Max = null,
    bool MaxInclusive = true,
    IReadOnlyList<string>? Allowed = null,
    bool AllowedAreText = false)
{
    /// <summary>What a value must satisfy to meet both this and <paramref name="other"/>.</summary>
    public CheckBounds And(CheckBounds other)
    {
        var (min, minInclusive) = (Min, MinInclusive);
        if (other.Min is { } otherMin && (min is null || otherMin > min || (otherMin == min && !other.MinInclusive)))
            (min, minInclusive) = (otherMin, other.MinInclusive);

        var (max, maxInclusive) = (Max, MaxInclusive);
        if (other.Max is { } otherMax && (max is null || otherMax < max || (otherMax == max && !other.MaxInclusive)))
            (max, maxInclusive) = (otherMax, other.MaxInclusive);

        var allowed = Allowed is null ? other.Allowed
            : other.Allowed is null ? Allowed
            : [.. Allowed.Intersect(other.Allowed, StringComparer.Ordinal)];

        return new CheckBounds(min, minInclusive, max, maxInclusive, allowed,
            Allowed is null ? other.AllowedAreText : AllowedAreText);
    }
}

/// <summary>
/// A CHECK constraint's definition, as SQL Server stores it, read as the bounds it puts on each
/// column — or not read at all.
/// </summary>
/// <remarks>
/// SQL Server rewrites what was typed before it stores it: <c>Grade BETWEEN 1 AND 10</c> comes
/// back as <c>([Grade]&gt;=(1) AND [Grade]&lt;=(10))</c>, and <c>Status IN ('A','B')</c> as
/// <c>([Status]='B' OR [Status]='A')</c>. Both spellings are read. Only comparisons of a column
/// with a literal, BETWEEN, IN, IS [NOT] NULL, and AND/OR of those are understood; anything else
/// (a function, two columns compared, NOT) makes the whole check unread, never half read.
/// </remarks>
public static class SqlCheck
{
    /// <summary>The bounds <paramref name="definition"/> puts on each column it names, or null
    /// when any part of it is not understood.</summary>
    public static IReadOnlyDictionary<string, CheckBounds>? Parse(string definition)
    {
        try
        {
            var tokens = Tokenize(definition);
            if (tokens is null)
                return null;

            var parser = new Parser(tokens);
            var condition = parser.Expression();

            return condition is null || !parser.AtEnd ? null : Bounds(condition);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    // --- What a check says ---

    private abstract record Condition;
    private sealed record Compare(string Column, string Op, Literal Value) : Condition;
    private sealed record Between(string Column, Literal Low, Literal High) : Condition;
    private sealed record InList(string Column, IReadOnlyList<Literal> Values) : Condition;
    private sealed record IsNull(string Column, bool Not) : Condition;
    private sealed record All(IReadOnlyList<Condition> Parts) : Condition;
    private sealed record Any(IReadOnlyList<Condition> Parts) : Condition;

    private sealed record Literal(decimal? Number, string? Text);

    private static Dictionary<string, CheckBounds>? Bounds(Condition condition)
    {
        switch (condition)
        {
            case Compare { Value.Number: { } n } c:
                return c.Op switch
                {
                    ">=" or "!<" => One(c.Column, new CheckBounds(Min: n)),
                    ">" => One(c.Column, new CheckBounds(Min: n, MinInclusive: false)),
                    "<=" or "!>" => One(c.Column, new CheckBounds(Max: n)),
                    "<" => One(c.Column, new CheckBounds(Max: n, MaxInclusive: false)),
                    "=" => One(c.Column, new CheckBounds(Min: n, Max: n)),
                    _ => null
                };

            case Compare { Value.Text: { } text, Op: "=" } c:
                return One(c.Column, new CheckBounds(Allowed: [text], AllowedAreText: true));

            case Between { Low.Number: { } low, High.Number: { } high } b:
                return One(b.Column, new CheckBounds(Min: low, Max: high));

            case InList list when list.Values.All(v => v.Text is not null):
                return One(list.Column, new CheckBounds(Allowed: [.. list.Values.Select(v => v.Text!)], AllowedAreText: true));

            case InList list when list.Values.All(v => v.Number is not null):
                return One(list.Column, new CheckBounds(
                    Allowed: [.. list.Values.Select(v => v.Number!.Value.ToString(CultureInfo.InvariantCulture))]));

            case IsNull { Not: true }:
                return new Dictionary<string, CheckBounds>(StringComparer.OrdinalIgnoreCase);

            case All all:
            {
                var merged = new Dictionary<string, CheckBounds>(StringComparer.OrdinalIgnoreCase);

                foreach (var part in all.Parts)
                {
                    if (Bounds(part) is not { } bounds)
                        return null;

                    foreach (var (column, bound) in bounds)
                        merged[column] = merged.TryGetValue(column, out var existing) ? existing.And(bound) : bound;
                }

                return merged;
            }

            case Any any:
                return Either(any);

            default:
                return null;
        }
    }

    /// <summary>
    /// An OR, read only where it lists the values one column may hold — what SQL Server makes of
    /// an IN — or where it says "or it is NULL" beside one condition, which the app's nullable
    /// property already allows.
    /// </summary>
    private static Dictionary<string, CheckBounds>? Either(Any any)
    {
        var nullable = any.Parts.OfType<IsNull>().Where(p => !p.Not).Select(p => p.Column).ToList();
        var rest = any.Parts.Where(p => p is not IsNull { Not: false }).ToList();

        if (rest.Count == 0)
            return null;

        if (rest.Count == 1)
        {
            var single = Bounds(rest[0]);
            return single is { Count: 1 } && nullable.All(single.ContainsKey) ? single : null;
        }

        string? column = null;
        bool? text = null;
        var values = new List<string>();

        foreach (var part in rest)
        {
            if (Bounds(part) is not { Count: 1 } bounds)
                return null;

            var (name, bound) = bounds.First();

            if (bound.Allowed is null && bound.Min is { } min && bound.Min == bound.Max)
                bound = new CheckBounds(Allowed: [min.ToString(CultureInfo.InvariantCulture)]);

            if (bound.Allowed is null
                || (column is not null && !string.Equals(column, name, StringComparison.OrdinalIgnoreCase))
                || (text is not null && text != bound.AllowedAreText))
                return null;

            column = name;
            text = bound.AllowedAreText;
            values.AddRange(bound.Allowed);
        }

        if (nullable.Any(c => !string.Equals(c, column, StringComparison.OrdinalIgnoreCase)))
            return null;

        return One(column!, new CheckBounds(Allowed: [.. values.Distinct(StringComparer.Ordinal)], AllowedAreText: text!.Value));
    }

    private static Dictionary<string, CheckBounds> One(string column, CheckBounds bounds)
        => new(StringComparer.OrdinalIgnoreCase) { [column] = bounds };

    // --- Reading the text ---

    private enum Kind { Column, Number, Text, Word, Op, Open, Close, Comma, Minus }

    private sealed record Token(Kind Kind, string Value);

    private static List<Token>? Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '[')
            {
                if (Quoted(text, ref i, ']', out var name) is false)
                    return null;

                tokens.Add(new Token(Kind.Column, name));
            }
            else if (c == '\'' || ((c == 'N' || c == 'n') && i + 1 < text.Length && text[i + 1] == '\''))
            {
                if (c != '\'')
                    i++;

                if (Quoted(text, ref i, '\'', out var value) is false)
                    return null;

                tokens.Add(new Token(Kind.Text, value));
            }
            else if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                var start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                    i++;

                tokens.Add(new Token(Kind.Number, text[start..i]));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    i++;

                tokens.Add(new Token(Kind.Word, text[start..i].ToUpperInvariant()));
            }
            else if (c is '>' or '<' or '=' or '!')
            {
                var two = i + 1 < text.Length ? text.Substring(i, 2) : string.Empty;

                if (two is ">=" or "<=" or "<>" or "!=" or "!<" or "!>")
                {
                    tokens.Add(new Token(Kind.Op, two));
                    i += 2;
                }
                else if (c != '!')
                {
                    tokens.Add(new Token(Kind.Op, c.ToString()));
                    i++;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                Kind? kind = c switch { '(' => Kind.Open, ')' => Kind.Close, ',' => Kind.Comma, '-' => Kind.Minus, _ => null };
                if (kind is null)
                    return null;

                tokens.Add(new Token(kind.Value, c.ToString()));
                i++;
            }
        }

        return tokens;
    }

    /// <summary>
    /// The text from the opening character at <paramref name="i"/> to the matching
    /// <paramref name="close"/>, a doubled one standing for itself; false when it never closes.
    /// </summary>
    private static bool Quoted(string text, ref int i, char close, out string value)
    {
        var builder = new System.Text.StringBuilder();
        i++;

        while (i < text.Length)
        {
            if (text[i] == close)
            {
                if (i + 1 < text.Length && text[i + 1] == close)
                {
                    builder.Append(close);
                    i += 2;
                    continue;
                }

                i++;
                value = builder.ToString();
                return true;
            }

            builder.Append(text[i++]);
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Recursive descent with backtracking: an opening parenthesis may start a group of
    /// conditions or only wrap a number, and only trying both tells which.</summary>
    private sealed class Parser(List<Token> tokens)
    {
        private int _at;

        public bool AtEnd => _at == tokens.Count;

        private Token? Peek => _at < tokens.Count ? tokens[_at] : null;

        private bool Take(Kind kind, string? value = null)
        {
            if (Peek is { } token && token.Kind == kind && (value is null || token.Value == value))
            {
                _at++;
                return true;
            }

            return false;
        }

        public Condition? Expression()
        {
            var parts = new List<Condition>();

            do
            {
                if (Conjunction() is not { } part)
                    return null;

                parts.Add(part);
            }
            while (Take(Kind.Word, "OR"));

            return parts.Count == 1 ? parts[0] : new Any(parts);
        }

        private Condition? Conjunction()
        {
            var parts = new List<Condition>();

            do
            {
                if (Unit() is not { } part)
                    return null;

                parts.Add(part);
            }
            while (Take(Kind.Word, "AND"));

            return parts.Count == 1 ? parts[0] : new All(parts);
        }

        private Condition? Unit()
        {
            var start = _at;

            if (Take(Kind.Open))
            {
                if (Expression() is { } inner && Take(Kind.Close))
                    return inner;

                _at = start;
            }

            return Predicate();
        }

        private Condition? Predicate()
        {
            var left = Operand();

            if (left is null)
                return null;

            if (Take(Kind.Word, "IS"))
            {
                var not = Take(Kind.Word, "NOT");
                return left is Value.Col col && Take(Kind.Word, "NULL") ? new IsNull(col.Name, not) : null;
            }

            if (Take(Kind.Word, "BETWEEN"))
            {
                var low = Operand();
                if (!Take(Kind.Word, "AND"))
                    return null;

                var high = Operand();
                return left is Value.Col c && low is Value.Lit l && high is Value.Lit h
                    ? new Between(c.Name, l.Literal, h.Literal)
                    : null;
            }

            if (Take(Kind.Word, "IN"))
            {
                if (left is not Value.Col c || !Take(Kind.Open))
                    return null;

                var values = new List<Literal>();

                do
                {
                    if (Operand() is not Value.Lit item)
                        return null;

                    values.Add(item.Literal);
                }
                while (Take(Kind.Comma));

                return Take(Kind.Close) ? new InList(c.Name, values) : null;
            }

            if (Peek is not { Kind: Kind.Op } op)
                return null;

            _at++;
            var right = Operand();

            return (left, right) switch
            {
                (Value.Col c, Value.Lit l) => new Compare(c.Name, op.Value, l.Literal),
                (Value.Lit l, Value.Col c) => new Compare(c.Name, Flipped(op.Value), l.Literal),
                _ => null
            };
        }

        private static string Flipped(string op) => op switch
        {
            ">" => "<", "<" => ">", ">=" => "<=", "<=" => ">=", "!<" => "!>", "!>" => "!<", _ => op
        };

        private abstract record Value
        {
            public sealed record Col(string Name) : Value;
            public sealed record Lit(Literal Literal) : Value;
        }

        private Value? Operand()
        {
            var start = _at;

            if (Take(Kind.Open))
            {
                if (Operand() is { } inner && Take(Kind.Close))
                    return inner;

                _at = start;
                return null;
            }

            if (Take(Kind.Minus))
            {
                return Operand() is Value.Lit { Literal.Number: { } n }
                    ? new Value.Lit(new Literal(-n, null))
                    : null;
            }

            if (Peek is not { } token)
                return null;

            switch (token.Kind)
            {
                case Kind.Column:
                    _at++;
                    return new Value.Col(token.Value);

                case Kind.Number:
                    _at++;
                    return new Value.Lit(new Literal(
                        decimal.Parse(token.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), null));

                case Kind.Text:
                    _at++;
                    return new Value.Lit(new Literal(null, token.Value));

                // A bare name is a column, unless it is a keyword or calls something.
                case Kind.Word when token.Value is not ("AND" or "OR" or "NOT" or "IS" or "NULL" or "IN" or "BETWEEN")
                                    && !(_at + 1 < tokens.Count && tokens[_at + 1].Kind == Kind.Open):
                    _at++;
                    return new Value.Col(token.Value);

                default:
                    return null;
            }
        }
    }
}

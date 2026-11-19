using System.Globalization;

namespace DataIngestion.Schema.Readers.Sql;

/// <summary>
/// A column's DEFAULT definition, as SQL Server stores it, turned into what a new record starts
/// with: a literal value, or one of the few functions a generated app can repeat.
/// </summary>
/// <remarks>
/// SQL Server stores a default wrapped in parentheses, often twice: <c>((1))</c>, <c>(N'Active')</c>,
/// <c>(getdate())</c>. Only a literal and the clock and new-GUID functions are carried; anything
/// else (an expression, a user function) is left to the caller to say it was not.
/// </remarks>
public static class SqlDefault
{
    /// <summary>The functions carried, by the name <see cref="CodeGenerationEngine.Models.TypeFacts.DefaultFunction"/> records.</summary>
    public const string Now = "now", UtcNow = "utcnow", NewGuid = "newguid";

    /// <summary>
    /// The literal value (invariant text) or function a default definition names; null when it is
    /// neither, or when it is NULL, which is no default at all.
    /// </summary>
    public static (string? Value, string? Function)? Parse(string? definition)
    {
        if (definition is null)
            return null;

        var text = Unwrapped(definition.Trim());

        switch (text.ToLowerInvariant())
        {
            case "getdate()" or "sysdatetime()" or "current_timestamp" or "sysdatetimeoffset()":
                return (null, Now);
            case "getutcdate()" or "sysutcdatetime()":
                return (null, UtcNow);
            case "newid()" or "newsequentialid()":
                return (null, NewGuid);
            case "null":
                return null;
        }

        if (StringLiteral(text) is { } literal)
            return (literal, null);

        if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number))
            return (number.ToString(CultureInfo.InvariantCulture), null);

        return null;
    }

    /// <summary>
    /// A literal default for a date column of SQL type <paramref name="sqlType"/>, as the
    /// invariant date text the generated app parses: SQL Server reads <c>'20200101'</c> as
    /// 2020-01-01 and, for <c>datetime</c> and <c>smalldatetime</c>, a number as days since
    /// 1900-01-01 (<c>((0))</c> is 1900-01-01). Any other value is returned as it is.
    /// </summary>
    public static string AsDate(string value, string sqlType)
    {
        var invariant = CultureInfo.InvariantCulture;
        var text = value.Trim();

        if (DateTime.TryParseExact(text, ["yyyyMMdd", "yyyyMMdd HH:mm", "yyyyMMdd HH:mm:ss", "yyyyMMdd HH:mm:ss.FFFFFFF"],
                invariant, DateTimeStyles.None, out var compact))
            return compact.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", invariant);

        if (sqlType.ToLowerInvariant() is "datetime" or "smalldatetime"
            && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, invariant, out var days)
            && days is >= -53690m and <= 2958463m)
            return new DateTime(1900, 1, 1).AddDays((double)days).ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", invariant);

        return value;
    }

    /// <summary><paramref name="text"/> without every pair of parentheses wrapping the whole of it.</summary>
    private static string Unwrapped(string text)
    {
        while (text.Length >= 2 && text[0] == '(' && text[^1] == ')' && ClosesAtEnd(text))
            text = text[1..^1].Trim();

        return text;
    }

    /// <summary>Whether the parenthesis opening <paramref name="text"/> is the one closing it, so
    /// <c>(a)+(b)</c> is not unwrapped into <c>a)+(b</c>. Quotes are skipped.</summary>
    private static bool ClosesAtEnd(string text)
    {
        var depth = 0;
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'')
                quoted = !quoted;
            else if (quoted)
                continue;
            else if (text[i] == '(')
                depth++;
            else if (text[i] == ')' && --depth == 0)
                return i == text.Length - 1;
        }

        return false;
    }

    /// <summary>The value of a whole-text string literal ('a''b' or N'a'), or null.</summary>
    private static string? StringLiteral(string text)
    {
        if (text.StartsWith("N'", StringComparison.OrdinalIgnoreCase))
            text = text[1..];

        if (text.Length < 2 || text[0] != '\'' || text[^1] != '\'')
            return null;

        var inner = text[1..^1];

        // A lone quote inside means two literals joined by something, not one literal.
        return inner.Replace("''", string.Empty).Contains('\'') ? null : inner.Replace("''", "'");
    }
}

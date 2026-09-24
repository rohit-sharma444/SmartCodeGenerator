using System.Data.Common;

namespace DataIngestion;

/// <summary>
/// Removes the password from a connection string that is about to be shown to someone.
/// </summary>
/// <remarks>
/// <see cref="DataSourceSpec.Location"/> is a file path for every other kind and a connection
/// string for SQL, and the location is echoed — on the review page, in the console sign-off, and
/// in the replay line a user is invited to copy. Without this, an ordinary SQL-auth connection
/// string puts its password in all three.
/// <para>
/// The value is spliced out of the ORIGINAL text rather than rebuilt from a
/// <see cref="DbConnectionStringBuilder"/>. Rebuilding works, but the builder lower-cases every
/// key on the way out, so <c>Database=Northwind</c> comes back as <c>database=Northwind</c> and
/// the echoed line stops matching what the user typed — which invites exactly the question of
/// what else was changed. The builder is still used to DECIDE whether there is a password,
/// because it understands quoting and this does not have to.
/// </para>
/// </remarks>
public static class ConnectionSecret
{
    public const string Mask = "***";

    /// <summary>
    /// Every spelling of the password key.
    /// </summary>
    /// <remarks>
    /// All three are listed because <see cref="DbConnectionStringBuilder"/> is the generic one and
    /// does not fold SqlClient's synonyms together: <c>pwd</c> is stored under <c>pwd</c>, not
    /// under <c>password</c>. <c>pass word</c> is a real synonym, not a typo.
    /// </remarks>
    private static readonly string[] Keys = ["password", "pwd", "pass word"];

    /// <summary>
    /// A whole typed command line with any password in it masked — for anything that keeps what
    /// was typed, such as the <c>.codegen_history</c> file.
    /// </summary>
    /// <remarks>
    /// <see cref="Redact"/> takes a connection string on its own and parses it, which a command
    /// line is not: <c>build app: shop from sql "Server=.;Password=x" tables A</c>. So this finds
    /// each key followed by <c>=</c> and masks up to the next <c>;</c> or <c>"</c> — the end of
    /// the value inside a quoted connection string. Unquoted, the mask runs on to the end of the
    /// line: masking too much of a history line costs a retype; masking too little leaks the
    /// password. Before this the history file held the password in plain text, although the manual
    /// promises it is masked wherever the run is echoed back.
    /// </remarks>
    public static string RedactLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return line ?? string.Empty;

        return System.Text.RegularExpressions.Regex.Replace(
            line,
            @"(?i)(?<![A-Za-z])(password|pwd|pass word)(\s*=\s*)('[^']*'|[^;""]*)",
            m => m.Groups[3].Value.Length == 0
                ? m.Value
                : m.Groups[1].Value + m.Groups[2].Value + Mask);
    }

    /// <summary>
    /// <paramref name="connectionString"/> with any password masked, and everything else — keys,
    /// casing, spacing, order — exactly as it was. The input is returned unchanged when it holds
    /// no password, or is not a connection string at all.
    /// </summary>
    public static string Redact(string connectionString)
    {
        var text = connectionString ?? string.Empty;

        if (text.Length == 0)
            return text;

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = text };

            foreach (var key in Keys)
            {
                if (builder.ContainsKey(key))
                    text = MaskValue(text, key);
            }
        }
        catch (ArgumentException)
        {
            // Not a connection string. A file path is the normal case here, and returning it
            // unchanged is the only correct answer.
            return text;
        }

        return text;
    }

    /// <summary>Replaces the value of <paramref name="key"/> in <paramref name="text"/>, leaving
    /// every other character where it was.</summary>
    private static string MaskValue(string text, string key)
    {
        var at = IndexOfKey(text, key);

        if (at < 0)
            return text;

        var equals = text.IndexOf('=', at);

        if (equals < 0)
            return text;

        var start = equals + 1;

        while (start < text.Length && char.IsWhiteSpace(text[start]))
            start++;

        var end = EndOfValue(text, start);

        return text[..start] + Mask + text[end..];
    }

    /// <summary>
    /// Where <paramref name="key"/> starts as an actual key — the next non-space character after
    /// it must be <c>=</c>.
    /// </summary>
    /// <remarks>
    /// That test is the whole guard, and it is load-bearing — but not for the reason it first
    /// looks. A decoy inside a value is usually harmless, because
    /// <see cref="MaskValue"/> then scans FORWARD for <c>=</c> and lands on the real key anyway.
    /// It stops being harmless when another key sits between the decoy and the real one:
    /// <c>Server=xpwdy;Database=A;Pwd=secret</c> finds <c>pwd</c> inside <c>xpwdy</c>, takes
    /// Database's <c>=</c> as its own, and masks <c>A</c> while leaving the password in place —
    /// the wrong value destroyed and the secret still printed. There is a test named for exactly
    /// that string.
    /// <para>
    /// A <c>;</c>-boundary check on the left was written here first and removed: with this test in
    /// place it could not change an answer, because a second <c>=</c> inside one value is not a
    /// connection string.
    /// </para>
    /// </remarks>
    private static int IndexOfKey(string text, string key)
    {
        for (var i = 0; i + key.Length <= text.Length; i++)
        {
            if (string.Compare(text, i, key, 0, key.Length, StringComparison.OrdinalIgnoreCase) != 0)
                continue;

            var before = text[..i].TrimEnd();


            var after = text[(i + key.Length)..].TrimStart();

            if (after.StartsWith('='))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// One past the last character of the value starting at <paramref name="start"/>.
    /// </summary>
    /// <remarks>
    /// A quoted value runs to its closing quote, which is what lets a password contain a
    /// semicolon; an unquoted one runs to the next <c>;</c>. Getting this wrong is how the tail of
    /// a password survives redaction.
    /// </remarks>
    private static int EndOfValue(string text, int start)
    {
        if (start >= text.Length)
            return text.Length;

        var quote = text[start];

        if (quote is '"' or '\'')
        {
            for (var i = start + 1; i < text.Length; i++)
                if (text[i] == quote)
                    return i + 1;

            return text.Length;
        }

        var semicolon = text.IndexOf(';', start);

        return semicolon < 0 ? text.Length : semicolon;
    }
}

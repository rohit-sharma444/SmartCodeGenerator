using DataIngestion.Schema.Readers;

namespace DataIngestion;

/// <summary>What a `build app` line asked for: a description, and optionally a data source.</summary>
public sealed record InlineBuildRequest(string Description, DataSourceSpec? Source, bool Normalize);

/// <summary>
/// Parses the scriptable form, <c>&lt;description&gt; from &lt;format&gt; &lt;location&gt;
/// [normalize]</c>.
/// </summary>
/// <remarks>
/// This form exists because redirected stdin never prompts — the precedent
/// <c>BuildAppCommand.ResolveTarget</c> set, and what every existing piped script and E2E test
/// depends on. Without an inline form there would be no way to drive the data path from a script
/// at all, so this is what the end-to-end test uses.
/// </remarks>
public static class InlineDataSource
{
    /// <summary>Opts a non-interactive run into applying every normalization finding. Absent, a
    /// scripted run reports the findings and changes nothing, so scripts stay deterministic.</summary>
    public const string NormalizeKeyword = "normalize";

    /// <summary>Names the SQL tables to read, comma-joined. Only meaningful for the sql
    /// kind: a path may legitimately contain the word.</summary>
    public const string TablesKeyword = "tables";

    public static InlineBuildRequest Parse(string tail)
    {
        var text = (tail ?? string.Empty).Trim();
        if (text.Length == 0)
            return new InlineBuildRequest(string.Empty, null, false);

        // Scan RIGHT to LEFT for `from <format>`. A description may legitimately contain the word
        // "from" ("track orders from suppliers"), and the leftmost match would swallow the rest of
        // it as a location.
        var tokens = Tokenize(text);

        for (var i = tokens.Count - 2; i >= 0; i--)
        {
            if (!string.Equals(tokens[i].Text, "from", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!DataSourceReaders.TryParseKind(tokens[i + 1].Text, out var kind))
                continue;

            var description = text[..tokens[i].Start].Trim();

            // The location is the REST OF THE LINE, not one token: Windows paths contain spaces.
            var locationStart = tokens[i + 1].Start + tokens[i + 1].Text.Length;
            var location = text[locationStart..].Trim();

            var normalize = false;
            if (location.EndsWith(NormalizeKeyword, StringComparison.OrdinalIgnoreCase)
                && location.Length > NormalizeKeyword.Length
                && char.IsWhiteSpace(location[^(NormalizeKeyword.Length + 1)]))
            {
                normalize = true;
                location = location[..^NormalizeKeyword.Length].Trim();
            }

            // AFTER normalize and before the location is settled. Both are trailing
            // keywords on a location that is the REST OF THE LINE, and normalize is the
            // outermost - taking tables first swallows it, turning `tables Orders normalize`
            // into a table called "Orders normalize" and losing the flag in silence.
            // Only for sql: "tables" is an ordinary English word and a path may contain it.
            IReadOnlyList<string>? sqlTables = null;

            if (kind == DataSourceKind.Sql)
                (location, sqlTables) = SplitTables(location);

            location = Unquote(location);

            // `from csv` with nothing after it names no file. Returning a spec with an empty
            // location would surface as a baffling "no file at ''" instead of being read as prose.
            if (location.Length == 0)
                continue;

            return new InlineBuildRequest(
                description,
                new DataSourceSpec(
                    kind,
                    location,
                    kind == DataSourceKind.Sql ? SqlConnectionInfo.DatabaseOf(location) : null,
                    sqlTables),
                normalize);
        }

        return new InlineBuildRequest(text, null, false);
    }

    /// <summary>
    /// Splits a trailing <c>tables A,B,C</c> off a location.
    /// </summary>
    /// <remarks>
    /// Searched from the RIGHT, so a connection string that happens to contain the word - in
    /// a password, or an Application Name - cannot be mistaken for the keyword. Returns the
    /// location unchanged when there is no such tail, which is what a run naming no tables
    /// looks like.
    /// </remarks>
    private static (string Location, IReadOnlyList<string>? Tables) SplitTables(string location)
    {
        var marker = location.LastIndexOf(
            " " + TablesKeyword + " ", StringComparison.OrdinalIgnoreCase);

        if (marker < 0)
            return (location, null);

        var names = Unquote(location[(marker + TablesKeyword.Length + 2)..].Trim());

        var split = names.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return split.Length == 0
            ? (location, null)
            : (location[..marker].Trim(), split);
    }

    private static string Unquote(string text)
        => text.Length >= 2 && text[0] == '"' && text[^1] == '"'
            ? text[1..^1]
            : text;

    /// <summary>Whitespace-separated tokens with the offset each starts at, so a description can be
    /// cut from the ORIGINAL text and keep its spacing.</summary>
    private static List<(string Text, int Start)> Tokenize(string text)
    {
        var tokens = new List<(string, int)>();
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            var isSpace = i == text.Length || char.IsWhiteSpace(text[i]);

            if (!isSpace && start < 0)
                start = i;
            else if (isSpace && start >= 0)
            {
                tokens.Add((text[start..i], start));
                start = -1;
            }
        }

        return tokens;
    }
}

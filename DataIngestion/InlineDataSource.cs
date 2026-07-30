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

            location = Unquote(location);

            // `from csv` with nothing after it names no file. Returning a spec with an empty
            // location would surface as a baffling "no file at ''" instead of being read as prose.
            if (location.Length == 0)
                continue;

            return new InlineBuildRequest(
                description, new DataSourceSpec(kind, location), normalize);
        }

        return new InlineBuildRequest(text, null, false);
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

using CodeGenerationEngine.Core;

namespace CodeGenerator.App;

/// <summary>
/// Ranks the known command names by edit distance to what the user typed, so an
/// unrecognized command can offer a "did you mean" hint. Case-insensitive.
/// </summary>
internal static class CommandSuggester
{
    /// <summary>Levenshtein edit distance (case-insensitive), two-row DP.</summary>
    internal static int Distance(string a, string b)
    {
        a ??= string.Empty;
        b ??= string.Empty;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    /// <summary>
    /// The closest command <em>prefixes</em> to <paramref name="input"/> within
    /// <paramref name="maxDistance"/> edits, best first, at most <paramref name="max"/>.
    /// Each command is reduced to its literal prefix — the words before the first
    /// "&lt;placeholder&gt;" or "[option]" — and the input's leading words are compared
    /// against it. This lets templated commands (e.g. "add business validation
    /// &lt;rule&gt; in &lt;class name&gt;") surface a "did you mean" for a keyword typo,
    /// while the suggested text stays placeholder-free.
    /// </summary>
    internal static IReadOnlyList<string> Closest(string input, int max = 3, int maxDistance = 3)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return [];

        var inputWords = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return CommandDictionary.Commands
            .Select(c => LiteralPrefix(c.Command))
            .Where(prefix => prefix.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(prefix => (Prefix: prefix, Distance: Distance(LeadingWords(inputWords, prefix), prefix)))
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Prefix)
            .ToList();
    }

    /// <summary>The literal words of a command name, up to the first placeholder
    /// ("&lt;...&gt;") or bracketed option ("[...]").</summary>
    private static string LiteralPrefix(string command)
    {
        var cut = command.IndexOfAny(['<', '[']);
        return (cut < 0 ? command : command[..cut]).Trim();
    }

    /// <summary>The first N words of the input joined with spaces, where N is the
    /// word count of <paramref name="prefix"/> — so a full sentence is compared
    /// against a command prefix on equal footing.</summary>
    private static string LeadingWords(string[] inputWords, string prefix)
    {
        var count = Math.Min(prefix.Split(' ').Length, inputWords.Length);
        return string.Join(' ', inputWords.Take(count));
    }
}

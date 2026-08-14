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
            .Where(x => x.Distance <= Budget(
                LeadingWords(inputWords, x.Prefix), x.Prefix, maxDistance))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Prefix)
            .ToList();
    }

    /// <summary>
    /// How many edits may separate <paramref name="compared"/> from <paramref name="prefix"/> and
    /// still count as a typo rather than a different phrase.
    /// </summary>
    /// <remarks>
    /// This used to be a flat <paramref name="ceiling"/>, which made the suggester confidently
    /// silly on short input: every three-letter command is within 3 edits of any two-character
    /// string, so <c>-h</c> was answered with "why, add, cls" and <c>/?</c> with "add, cls, why" —
    /// nonsense offered at the moment the user is already lost.
    /// <para>
    /// The budget is now bounded by the shorter of the two strings as well as by the ceiling: a
    /// correction may not cost as much as the word it is correcting. That keeps every case the
    /// feature exists for ("hepl" → help, "craete project" → create project) while refusing to
    /// guess when there is too little to go on.
    /// </para>
    /// </remarks>
    private static int Budget(string compared, string prefix, int ceiling)
    {
        var shorter = Math.Min(compared.Length, prefix.Length);

        // A one-character input has no room for any edit at all; two characters allow one.
        return Math.Min(ceiling, Math.Max(0, shorter - 1));
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

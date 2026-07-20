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
    /// The closest command names to <paramref name="input"/> within
    /// <paramref name="maxDistance"/> edits, best first, at most <paramref name="max"/>.
    /// Templated forms (those with a "&lt;placeholder&gt;") are skipped — they are not
    /// something a user types verbatim.
    /// </summary>
    internal static IReadOnlyList<string> Closest(string input, int max = 3, int maxDistance = 3)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return [];

        return CommandDictionary.Commands
            .Select(c => c.Command)
            .Where(c => !c.Contains('<'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(command => (Command: command, Distance: Distance(trimmed, command)))
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Command, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Command)
            .ToList();
    }
}

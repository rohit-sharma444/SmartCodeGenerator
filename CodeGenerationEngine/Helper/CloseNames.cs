using System.Linq;

namespace CodeGenerationEngine.Helper;

/// <summary>The names close to one that was not found, for a "did you mean" hint.</summary>
public static class CloseNames
{
    /// <summary>
    /// Up to <paramref name="max"/> of <paramref name="candidates"/> within a small edit distance of
    /// <paramref name="name"/>, or starting with it, nearest first.
    /// </summary>
    public static IReadOnlyList<string> To(string name, IEnumerable<string> candidates, int max = 3)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [];

        var allowed = Math.Max(1, name.Length / 3);

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(c => (Name: c, Distance: Distance(name.ToLowerInvariant(), c.ToLowerInvariant())))
            .Where(x => x.Distance <= allowed
                || x.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Name)
            .ToList();
    }

    /// <summary>" Did you mean A or B?" — or empty when nothing is close.</summary>
    public static string Hint(string name, IEnumerable<string> candidates)
    {
        var close = To(name, candidates);
        return close.Count == 0 ? string.Empty : $" Did you mean {string.Join(" or ", close)}?";
    }

    // Edits to turn a into b, a swap of two neighbouring letters counting as one: "Nmae" is one
    // slip from "Name", not two.
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }

        return d[a.Length, b.Length];
    }
}

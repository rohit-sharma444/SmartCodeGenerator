namespace NaturalLanguageBridge;

/// <summary>
/// Corrects near-miss spellings of the distinctive validation-family keywords
/// (<c>business</c>, <c>validation</c>, <c>validator</c>) so a typo like
/// "add busniess validation ..." still routes to the right intent. Only these
/// long, distinctive words are corrected — short common keywords (add, create,
/// class, ...) are left to the "did you mean" suggester, where a wrong guess is
/// surfaced rather than acted on. Token replacement fires only when a token is
/// uniquely within two edits of one keyword and close in length, so argument
/// words (entity names, rule text) are never rewritten. Stateless.
/// </summary>
public static class KeywordCorrector
{
    private static readonly string[] Keywords = ["business", "validation", "validator"];

    private const int MaxDistance = 2;
    private const int MaxLengthDelta = 2;

    /// <summary>Rewrites near-miss keyword tokens in <paramref name="input"/> to
    /// their canonical spelling, preserving all other tokens and the original
    /// whitespace between them.</summary>
    public static string Correct(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input ?? string.Empty;

        // Split on spaces but keep the separators so we reproduce the original
        // spacing exactly; only whole space-delimited tokens are candidates.
        var parts = input.Split(' ');
        for (var i = 0; i < parts.Length; i++)
        {
            var corrected = CorrectToken(parts[i]);
            if (corrected is not null)
                parts[i] = corrected;
        }

        return string.Join(' ', parts);
    }

    /// <summary>The canonical keyword for a token that is a unique near-miss, or
    /// <c>null</c> to leave the token unchanged. Case- and length-guarded, and it
    /// never "corrects" a token that is already a keyword.</summary>
    private static string? CorrectToken(string token)
    {
        if (token.Length == 0)
            return null;

        var best = -1;
        var bestDistance = MaxDistance + 1;
        var tied = false;

        for (var k = 0; k < Keywords.Length; k++)
        {
            var keyword = Keywords[k];
            if (Math.Abs(token.Length - keyword.Length) > MaxLengthDelta)
                continue;

            var distance = Distance(token, keyword);
            if (distance == 0)
                return null; // already correct — leave it exactly as typed

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = k;
                tied = false;
            }
            else if (distance == bestDistance)
            {
                tied = true;
            }
        }

        if (best < 0 || bestDistance > MaxDistance || tied)
            return null;

        return Keywords[best];
    }

    /// <summary>Levenshtein edit distance (case-insensitive), two-row DP.</summary>
    private static int Distance(string a, string b)
    {
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
}

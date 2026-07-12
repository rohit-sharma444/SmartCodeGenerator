namespace DomainDiscovery.Enrichment;

/// <summary>
/// Pulls the head noun of the predicate nominal out of a Wikipedia lead sentence:
/// "A receipt is a document acknowledging..." -> "document".
/// </summary>
/// <remarks>
/// The *topic* article cannot supply this — it defines its own title and then uses the rest
/// of its vocabulary without defining it (measured: zero definitions for any of the twenty
/// Point of sale candidates across its 299 sentences). Each candidate's own article does
/// define it, in its first sentence, which is what this reads.
/// </remarks>
public static class HypernymExtractor
{
    private static readonly string[] Copulas = { "is", "are", "was", "were" };

    private static readonly HashSet<string> Determiners =
        new(StringComparer.OrdinalIgnoreCase) { "a", "an", "the" };

    /// <summary>
    /// Ends the noun phrase. Everything after one of these modifies the head rather than
    /// being it, so "an organized collection of facts" stops at "of" and yields "collection".
    /// </summary>
    private static readonly HashSet<string> Terminators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "of", "that", "which", "who", "whose", "where", "when", "used",
            "in", "on", "for", "with", "to", "from", "by", "at"
        };

    /// <summary>
    /// Extracts the head noun (hypernym) from a Wikipedia lead sentence.
    /// Returns a lowercase head noun, or an empty string when the sentence defines nothing.
    /// </summary>
    public static string Extract(string leadSentence)
    {
        if (string.IsNullOrWhiteSpace(leadSentence))
            return string.Empty;

        // "Sale may refer to:" - a disambiguation page defines nothing, and pairing its
        // words with an archetype would invent a shape out of nothing.
        if (leadSentence.Contains("may refer to", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var words = leadSentence
            .Split(leadSentence.Where(c => !char.IsLetterOrDigit(c) && c != '\'' && c != '-')
                               .Distinct().ToArray(),
                   StringSplitOptions.RemoveEmptyEntries);

        var copula = Array.FindIndex(
            words, w => Copulas.Contains(w, StringComparer.OrdinalIgnoreCase));

        if (copula < 0)
            return string.Empty;

        string head = string.Empty;

        for (var i = copula + 1; i < words.Length; i++)
        {
            var word = words[i];

            if (Determiners.Contains(word))
                continue;

            if (Terminators.Contains(word))
                break;

            // A participle after the head noun modifies it rather than being it - "a document
            // acknowledging..." stops at "acknowledging" and yields "document". But before any
            // head has been captured, an "-ing" word is the head itself - "a gathering of
            // people" must yield "gathering", not "".
            if (head.Length > 0 && word.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
                break;

            head = word;
        }

        return head.ToLowerInvariant();
    }
}

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Function words that must never become domain entities.
/// </summary>
/// <remarks>
/// The NLE's part-of-speech tagger defaults unrecognized words to <c>Noun</c>, which is what
/// lets arbitrary business nouns be picked up from article prose. The cost is that determiners,
/// prepositions, pronouns and auxiliary verbs are tagged as nouns too — and because they are
/// among the most frequent words in any article, frequency ranking alone promotes them straight
/// to the top of the model. They are excluded by name instead.
///
/// Content-word noise (adjectives such as "actual", adverbs such as "accurately") is left to the
/// harvester's frequency cap: such words are rare in prose, so they never reach the top ranks.
/// </remarks>
internal static class StopWords
{
    public static bool Contains(string name) => Words.Contains(name);

    /// <summary>Compared after normalization, so entries are singular and case-insensitive.</summary>
    private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        // Determiners and quantifiers
        "a", "an", "the", "all", "any", "both", "each", "either", "every", "few", "many", "more",
        "most", "much", "neither", "no", "none", "other", "same", "several", "some", "such",

        // Pronouns
        "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "us", "them",
        "my", "your", "his", "its", "our", "their", "this", "that", "these", "those",
        "who", "whom", "whose", "which", "what",

        // Prepositions and conjunctions
        "about", "above", "after", "again", "against", "among", "and", "around", "as", "at",
        "because", "before", "below", "between", "but", "by", "during", "for", "from", "if",
        "in", "into", "nor", "of", "off", "on", "once", "only", "onto", "or", "out", "over",
        "per", "since", "so", "than", "then", "through", "to", "under", "until", "up", "upon",
        "via", "when", "where", "while", "with", "within", "without",

        // Auxiliary and copular verbs
        "am", "is", "are", "was", "were", "be", "been", "being", "have", "has", "had", "having",
        "do", "does", "did", "doing", "can", "could", "may", "might", "must", "shall", "should",
        "will", "would",

        // Discourse glue and prose verbs that carry no domain meaning
        "also", "although", "example", "however", "include", "included", "including", "instead",
        "just", "not", "now", "often", "rather", "still", "take", "taken", "therefore", "though",
        "thus", "too", "use", "used", "using", "very", "well", "yet",
    };
}

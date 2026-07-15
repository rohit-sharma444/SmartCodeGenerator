namespace DomainDiscovery.Enrichment;

/// <summary>
/// Picks domain concepts out of a topic article's wikilinks.
/// </summary>
/// <remarks>
/// Wikilink targets are disambiguated by human editors, and they name the multi-word
/// concepts prose-noun frequency structurally cannot see: "cash register" tokenizes into
/// two common nouns that normalize to junk, so the harvester can never surface it, yet the
/// article links it. A link alone is not enough — an article links hundreds of things it
/// merely mentions — so a candidate must also be something the prose keeps talking about.
///
/// A title also must not name one particular thing rather than a kind of thing: a proper
/// noun (e.g. "Microsoft") can never be a domain class, only a common noun (e.g. "Merchant")
/// can. Wiki title-casing capitalizes every title's first word regardless of which kind it
/// is, so the title's own spelling cannot distinguish them — but the article's prose can: a
/// common noun is written lowercase somewhere ("the merchant bank"), while a proper noun
/// never is. See <see cref="HasLowercaseOccurrence"/>.
/// </remarks>
public static class LinkedConceptSource
{
    /// <summary>Mentions in the prose before a linked title counts as a domain concept.</summary>
    public const int MinMentions = 2;

    /// <summary>Longer titles are descriptions, not concepts ("Automated sales suppression device").</summary>
    public const int MaxTitleWords = 3;

    /// <summary>
    /// Returns the raw wikilink titles that survive filtering as domain concept candidates.
    /// </summary>
    /// <remarks>
    /// The titles are returned un-normalized (e.g., "Cash register") because a later stage in the
    /// pipeline transforms them into type names; normalization at this stage would destroy the
    /// multi-word concepts that tokenization and case-folding cannot surface.
    ///
    /// A link title is only retained if the article's prose mentions it repeatedly (at least
    /// <see cref="MinMentions"/> times), because articles link hundreds of things they merely
    /// mention once in passing. Repeated mention in the prose indicates the concept is central
    /// to the article's domain, not merely incidental.
    /// </remarks>
    /// <param name="linkTitles">The wikilink target titles extracted from the article.</param>
    /// <param name="articleText">The full text of the article to check for repeated mentions.</param>
    /// <returns>A list of link titles that meet all filtering criteria: occurrence threshold, title length, disambiguation, and proper-noun rules.</returns>
    public static IReadOnlyList<string> Candidates(
        IReadOnlyList<string> linkTitles, string articleText)
    {
        if (linkTitles is null || linkTitles.Count == 0 || string.IsNullOrWhiteSpace(articleText))
            return Array.Empty<string>();

        var kept = new List<string>();

        foreach (var title in linkTitles)
        {
            if (string.IsNullOrWhiteSpace(title))
                continue;

            // "Gondola (retail)": the qualifier exists because the bare word is ambiguous.
            if (title.Contains('('))
                continue;

            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length > MaxTitleWords)
                continue;

            // "National Retail Federation" - a name, not a kind of thing.
            if (words.Count(w => w.Length > 0 && char.IsUpper(w[0])) >= 2)
                continue;

            // Wiki title-casing capitalizes every title's first word regardless of whether it
            // names a kind of thing ("Cash register") or one particular thing ("Microsoft"), so
            // the title's own casing cannot tell the two apart. The article's prose can: a common
            // noun gets written lowercase somewhere ("the cash register"), while a proper noun
            // never does ("IBM", "Microsoft" always capitalized). A title names one particular
            // thing - never a kind of thing - if the prose only ever spells it capitalized, so
            // such a title cannot be a domain class and must be dropped.
            if (!HasLowercaseOccurrence(articleText, title))
                continue;

            if (CountOccurrences(articleText, title) >= MinMentions)
                kept.Add(title);
        }

        return kept;
    }

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        var at = 0;

        while ((at = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            at += term.Length;
        }

        return count;
    }

    /// <summary>
    /// True if the article text spells the title with a lowercase first letter anywhere
    /// ("cash register"), which only happens for a common noun; a proper noun ("Microsoft")
    /// is always capitalized, so it never produces such an occurrence.
    /// </summary>
    private static bool HasLowercaseOccurrence(string text, string title)
    {
        if (title.Length == 0)
            return false;

        var lowerFirst = char.ToLowerInvariant(title[0]) + title[1..];
        return text.Contains(lowerFirst, StringComparison.Ordinal);
    }
}

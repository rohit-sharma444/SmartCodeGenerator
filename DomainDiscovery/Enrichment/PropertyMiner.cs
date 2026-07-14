using System.Text.RegularExpressions;
using DomainDiscovery.SeedExtraction;
using FieldShape = DomainDiscovery.Enrichment.DomainFieldLexicon.FieldShape;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Mines a class's properties from the article the class was discovered from.
/// </summary>
/// <remarks>
/// <para>
/// This is what the offline dump buys. Reading each candidate's own article costs one article
/// fetch per candidate, and twenty candidates is twenty fetches — exactly what the MediaWiki API's
/// rate limit forbade, and the reason <see cref="EntityShaper"/> settled for guessing a shape from
/// a single lead sentence. Against a local store that read is a seek.
/// </para>
/// <para>
/// Two sources, in confidence order. A <b>bulleted list introduced by a containment phrase</b>
/// ("An invoice typically contains:") is the strong one: an encyclopedia states a concept's parts
/// exactly there, and it is the reason <see cref="Wikipedia.Dump.WikitextStripper"/> goes out of
/// its way to keep list markers the API's explaintext destroyed. A <b>prose enumeration</b> after
/// a containment verb is the weaker one, and is only consulted for what the lists did not give.
/// </para>
/// <para>
/// The lead-in requirement is not a nicety. Without it, every "See also" and "References" list in
/// Wikipedia becomes a property list, and the class fills with navigation furniture.
/// </para>
/// <para>
/// <b>No evidence is a no-op.</b> An article with nothing to say returns empty, and the entity
/// stays exactly as <see cref="EntityShaper"/> shaped it. A miner that could make a model worse
/// than the one it replaces would be a regression, not a feature.
/// </para>
/// </remarks>
public static class PropertyMiner
{
    /// <summary>
    /// The most properties one article may contribute. A class with thirty mined fields is not a
    /// better model than one with eight — it is an unreadable grid. The strongest evidence
    /// (the earliest, list-borne items) survives the cap.
    /// </summary>
    public const int MaxMinedFields = 8;

    /// <summary>The most words a phrase may hold and still be a property name. "date of issue"
    /// is a property; "a detailed explanation of every term that may apply" is a sentence.</summary>
    private const int MaxPhraseWords = 4;

    /// <summary>
    /// The verbs an encyclopedia uses when it is about to state what something is made of. A list
    /// or enumeration NOT introduced by one of these is not evidence of anything.
    /// </summary>
    private static readonly Regex ContainmentLeadIn = new(
        @"\b(contains?|contain|includes?|include|comprises?|comprise|consists? of|records?|shows?|lists?|carries|specif(?:y|ies)|states?)\b[^.]*:\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The same verbs, mid-sentence, followed by the enumeration itself.</summary>
    private static readonly Regex ProseEnumeration = new(
        @"\b(?:contains?|includes?|comprises?|consists? of|records?|shows?|lists?|carries)\b\s+(?<items>[^.]+)\.",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Leading articles and determiners: "the date of sale" is the DateOfSale property.</summary>
    private static readonly Regex LeadingDeterminer = new(
        @"^\s*(?:a|an|the|its|their|each|every|any)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Anything parenthetical, or after a dash, is a gloss rather than part of the name.
    /// </summary>
    /// <remarks>
    /// A gloss dash is SET OFF by whitespace ("date of issue - the day it was raised"). A plain
    /// hyphen with no space around it is inside a word and belongs to the name: the Invoice
    /// article's lead says "agreed-upon prices", and cutting at that hyphen left the class with a
    /// field called "Agreed".
    /// </remarks>
    private static readonly Regex Gloss = new(
        @"(?:\s*[(–—:,;]|\s+-\s+).*$", RegexOptions.Compiled);

    /// <summary>
    /// Words that cannot open a property name, because a property is a NOUN PHRASE and these open
    /// a clause.
    /// </summary>
    /// <remarks>
    /// Measured against the real articles: the prose enumeration splits a sentence on commas and
    /// "and", which slices clauses out of the middle of it. The Invoice article yielded
    /// "in ancient Mesopotamia" and "functioned as contracts"; Receipt yielded "for their
    /// agreement"; Credit note yielded "did not receive". None is a property, and all of them
    /// begin with a preposition, a conjunction, or an auxiliary verb — which is the cheapest
    /// reliable signal that what follows is a fragment rather than a thing.
    /// <para>
    /// A participle is deliberately NOT in this list: "agreed prices for products" is a real
    /// property of an order, and rejecting every -ed opener would take it with the noise.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> FragmentOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        // Prepositions.
        "in", "of", "for", "with", "to", "from", "by", "at", "on", "as", "into", "onto",
        "over", "under", "between", "through", "during", "after", "before", "about",

        // Conjunctions and relatives.
        "and", "or", "but", "that", "which", "who", "whom", "whose", "when", "where", "while",

        // Auxiliaries and modals — a clause, never a thing.
        "is", "are", "was", "were", "be", "been", "being", "do", "does", "did",
        "has", "have", "had", "can", "could", "may", "might", "must", "shall",
        "should", "will", "would", "not",
    };

    /// <summary>
    /// The properties <paramref name="articleBody"/> states that <paramref name="entityName"/> has.
    /// Empty when the article says nothing usable — which is the common case, and is fine.
    /// Never throws.
    /// </summary>
    public static IReadOnlyList<FieldShape> Mine(string articleBody, string entityName)
    {
        if (string.IsNullOrWhiteSpace(articleBody))
            return Array.Empty<FieldShape>();

        try
        {
            var phrases = new List<string>();
            phrases.AddRange(FromIntroducedLists(articleBody));
            phrases.AddRange(FromProseEnumerations(articleBody));

            var fields = new List<FieldShape>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var phrase in phrases)
            {
                if (fields.Count >= MaxMinedFields)
                    break;

                if (ToFieldShape(phrase, entityName) is not { } shape)
                    continue;

                if (!taken.Add(shape.Name))
                    continue;

                fields.Add(shape);
            }

            return fields;
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
        {
            // The no-op contract. A surprise here leaves the entity as EntityShaper shaped it.
            return Array.Empty<FieldShape>();
        }
    }

    /// <summary>
    /// Every item of every bulleted list whose preceding non-list line ends in a containment
    /// phrase. The list markers this reads are the ones WikitextStripper preserved on purpose.
    /// </summary>
    private static IEnumerable<string> FromIntroducedLists(string body)
    {
        var lines = body.Split('\n');
        var introduced = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("* ", StringComparison.Ordinal))
            {
                if (introduced)
                    yield return line[2..];
                continue;
            }

            // A non-list line either introduces the list that follows it, or ends the one above.
            introduced = ContainmentLeadIn.IsMatch(line);
        }
    }

    /// <summary>
    /// Items enumerated in prose after a containment verb: "A receipt includes the date of sale,
    /// the amount paid and the payment method."
    /// </summary>
    private static IEnumerable<string> FromProseEnumerations(string body)
    {
        foreach (Match match in ProseEnumeration.Matches(body))
        {
            var items = match.Groups["items"].Value;

            foreach (var item in Regex.Split(items, @",|\band\b|\bor\b"))
            {
                if (!string.IsNullOrWhiteSpace(item))
                    yield return item;
            }
        }
    }

    /// <summary>
    /// One phrase as a field, or null when it cannot be one: too long, a stop word, not a valid
    /// identifier, named "Id" (which DomainModelMapper generates), or named after its own class
    /// (CS0542 — <c>class Invoice { string Invoice; }</c> does not compile).
    /// </summary>
    private static FieldShape? ToFieldShape(string phrase, string entityName)
    {
        var cleaned = Gloss.Replace(LeadingDeterminer.Replace(phrase.Trim(), string.Empty), string.Empty).Trim();

        if (cleaned.Length == 0)
            return null;

        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > MaxPhraseWords)
            return null;

        // A property is a noun phrase. Anything opening a clause is a fragment the enumeration
        // split out of the middle of a sentence, and naming a field after it makes the model worse.
        if (FragmentOpeners.Contains(words[0]))
            return null;

        var name = NameNormalizer.ToFieldName(cleaned);

        if (!NameNormalizer.IsValidIdentifier(name))
            return null;

        if (StopWords.Contains(name))
            return null;

        if (string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(name, entityName, StringComparison.OrdinalIgnoreCase))
            return null;

        return new FieldShape(name, TypeInference.Infer(name));
    }
}

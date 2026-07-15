using DomainDiscovery.SeedExtraction;

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
    /// Words that name a container for the real head rather than the head itself: "a type of
    /// display" is not about "type", it is about "display". Only "of" earns a second scan past
    /// one of these — "a type that detects touch" has no second noun phrase to chase.
    /// </summary>
    private static readonly HashSet<string> ContainerWords =
        new(StringComparer.OrdinalIgnoreCase)
        { "type", "kind", "form", "sort", "variety", "class", "category" };

    /// <summary>
    /// Extracts the head noun (hypernym) from a Wikipedia lead sentence.
    /// Returns a lowercase, singular head noun, or an empty string when the sentence defines
    /// nothing.
    /// </summary>
    public static string Extract(string leadSentence)
    {
        if (string.IsNullOrWhiteSpace(leadSentence))
            return string.Empty;

        // "Sale may refer to:" - a disambiguation page defines nothing, and pairing its
        // words with an archetype would invent a shape out of nothing.
        if (leadSentence.Contains("may refer to", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        // The plain word list (below) splits on every non-alphanumeric character, which erases
        // commas and semicolons entirely. But those marks genuinely end the noun phrase - "a
        // payment card, usually issued by a bank" must stop at "card", not wander into the
        // modifier clause the comma introduces. So words are tokenized here together with their
        // position in the original sentence, which lets the punctuation right after each word
        // be inspected separately from the word itself.
        var tokens = Tokenize(leadSentence);

        var copula = tokens.FindIndex(
            t => Copulas.Contains(t.Word, StringComparer.OrdinalIgnoreCase));

        if (copula < 0)
            return string.Empty;

        var (head, terminator, nextIndex) = ScanHead(tokens, leadSentence, copula + 1);

        // "a type of display" scans only as far as "type" ("of" terminates it), but "type" is
        // a container, not a concept - the real head is whatever "of" introduces. Re-run the
        // same scan from just past "of" and take its head instead, falling back to the
        // container word when nothing usable follows (e.g. "a type of thing." with no phrase).
        if (head.Length > 0
            && ContainerWords.Contains(head)
            && string.Equals(terminator, "of", StringComparison.OrdinalIgnoreCase))
        {
            var (continuedHead, _, _) = ScanHead(tokens, leadSentence, nextIndex);
            if (continuedHead.Length > 0)
                head = continuedHead;
        }

        // A hypernym is a concept, not a word form: "Self-checkouts are machines..." and "A
        // computer is a machine..." define the same thing, so both must reach ArchetypeLexicon
        // as "machine". NameNormalizer.ToEntityName already carries this singularization
        // (deliberately conservative - "business" stays "business") and reusing it here means
        // there is exactly one plural-stripper in the codebase, not two.
        return Singularize(head);
    }

    /// <summary>
    /// Scans forward from <paramref name="startIndex"/> for the head noun of one noun phrase,
    /// applying the same determiner/terminator/participle rules <see cref="Extract"/> always
    /// has. Returns the head captured (possibly empty), the word that stopped the scan (empty
    /// when the scan ran out of tokens or stopped at punctuation rather than a terminator), and
    /// the index to resume scanning from - which lets a caller re-run this for a second noun
    /// phrase (see the "type of X" handling in <see cref="Extract"/>) without duplicating the
    /// rules themselves.
    /// </summary>
    private static (string Head, string Terminator, int NextIndex) ScanHead(
        List<(string Word, int End)> tokens, string sentence, int startIndex)
    {
        var head = string.Empty;

        for (var i = startIndex; i < tokens.Count; i++)
        {
            var (word, end) = tokens[i];

            if (Determiners.Contains(word))
            {
                if (EndsPhrase(sentence, end))
                    return (head, string.Empty, i + 1);
                continue;
            }

            if (Terminators.Contains(word))
                return (head, word, i + 1);

            // A participle after the head noun modifies it rather than being it - "a document
            // acknowledging..." stops at "acknowledging" and yields "document"; "engaged in
            // trade" after "company" works the same way for the past-tense form. But before any
            // head has been captured, a "-ing"/"-ed" word is the head itself, not a modifier of
            // it - "a gathering of people" must yield "gathering", and "an organized collection
            // of data" must let "organized" through so it can be overwritten by "collection".
            if (head.Length > 0 &&
                (word.EndsWith("ing", StringComparison.OrdinalIgnoreCase) ||
                 word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)))
                return (head, string.Empty, i);

            head = word;

            // A comma or semicolon right after the head ends the noun phrase - "a payment
            // card, usually issued by a bank" is done at "card"; whatever follows the mark
            // only modifies it rather than continuing it.
            if (EndsPhrase(sentence, end))
                return (head, string.Empty, i + 1);
        }

        return (head, string.Empty, tokens.Count);
    }

    /// <summary>
    /// Folds a plural head onto the singular form ArchetypeLexicon's table is keyed by, via the
    /// one singularizer the codebase already has. Blank input passes through unchanged so
    /// callers keep seeing an empty string, not a surprising non-empty one, when nothing was
    /// captured.
    /// </summary>
    private static string Singularize(string head)
    {
        if (head.Length == 0)
            return head;

        var entityName = NameNormalizer.ToEntityName(head);
        return entityName.Length == 0 ? head.ToLowerInvariant() : entityName.ToLowerInvariant();
    }

    /// <summary>
    /// Splits the sentence into words the same way the original word list did (breaking on any
    /// character that isn't a letter, digit, apostrophe or hyphen), but keeps each word's end
    /// position in the source string so trailing punctuation can still be examined.
    /// </summary>
    private static List<(string Word, int End)> Tokenize(string sentence)
    {
        var tokens = new List<(string, int)>();
        var start = -1;

        for (var i = 0; i < sentence.Length; i++)
        {
            var isWordChar = char.IsLetterOrDigit(sentence[i]) || sentence[i] is '\'' or '-';

            if (isWordChar)
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                tokens.Add((sentence.Substring(start, i - start), i));
                start = -1;
            }
        }

        if (start >= 0)
            tokens.Add((sentence.Substring(start), sentence.Length));

        return tokens;
    }

    /// <summary>
    /// True when a comma or semicolon sits right after the given position - the punctuation
    /// that genuinely closes a noun phrase, as opposed to marks like parentheses that don't.
    /// </summary>
    private static bool EndsPhrase(string sentence, int position) =>
        position < sentence.Length && sentence[position] is ',' or ';';
}

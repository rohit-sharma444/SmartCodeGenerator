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
    /// Words that modify the head rather than being it, and which sit BEFORE it: "is often a
    /// ranking", "is primarily a hospital", "is not a device". The scan steps over them and
    /// keeps looking, instead of capturing the first token after the copula and stopping.
    /// </summary>
    /// <remarks>
    /// Measured over twelve topic articles, these accounted for some thirty-five candidates
    /// whose definition resolved to an adverb — "often", "primarily", "generally", "humanly",
    /// "not" — and which therefore could never reach an archetype. Adverbs ending in "-ly" are
    /// caught by rule (see <see cref="IsModifier"/>); this list is the ones that do not.
    /// </remarks>
    private static readonly HashSet<string> Modifiers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "not", "often", "also", "still", "just", "very", "quite", "rather", "more",
            "most", "much", "any", "either", "neither", "sometimes", "always", "never",
            "perhaps", "maybe", "so", "such", "well", "then", "now", "here", "there",
            "large", "small", "big", "little", "new", "old", "long", "short", "high", "low"
        };

    /// <summary>
    /// Words that join phrases and so cannot be a head: "is and was a drawer". Reaching one
    /// BEFORE any head has been captured means the phrase has not started yet, so the scan
    /// steps over it exactly as it does a modifier. Reaching one AFTER a head has been
    /// captured ends the phrase instead of being skipped: "a room or area" must stop at
    /// "room" — otherwise the second disjunct falls through to the plain "head = word"
    /// assignment and overwrites it, which is exactly how "area" (an abstraction) used to
    /// delete a "room" candidate outright.
    /// </summary>
    private static readonly HashSet<string> Conjunctions =
        new(StringComparer.OrdinalIgnoreCase) { "and", "or", "but", "nor", "yet" };

    /// <summary>
    /// Words that name a container for the real head rather than the head itself: "a type of
    /// display" is not about "type", it is about "display". Only "of" earns a second scan past
    /// one of these — "a type that detects touch" has no second noun phrase to chase.
    /// </summary>
    private static readonly HashSet<string> ContainerWords =
        new(StringComparer.OrdinalIgnoreCase)
        { "type", "kind", "form", "sort", "variety", "class", "category" };

    /// <summary>
    /// Irregular past participles that can follow a head noun and modify it, the same way an
    /// "-ing"/"-ed" word does: "travel undertaken for work purposes" must stop at "travel", not
    /// wander into "undertaken". A suffix rule cannot cover these the way it covers "-ing"/"-ed"
    /// — ordinary nouns such as "chicken", "kitchen", "garden", "citizen" and "children" also end
    /// in "-en", and treating that ending as a participle marker would strip them as modifiers of
    /// whatever word precedes them. So the irregular forms are enumerated here instead.
    /// </summary>
    private static readonly HashSet<string> IrregularParticiples =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "undertaken", "taken", "given", "written", "known", "driven", "spoken", "chosen",
            "proven", "seen", "shown", "held", "kept", "spent", "made", "sold", "told", "built",
            "sent", "found", "left", "brought", "bought", "taught", "thought"
        };

    /// <summary>
    /// Ordinary nouns that happen to end in "-ly" and so would otherwise be caught by the
    /// blanket adverb-suffix rule in <see cref="IsModifier"/>, stripping the head right where
    /// it sits: "a clan is a family of related people" must yield "family", not skip over it
    /// looking for a noun that never comes. No <see cref="ArchetypeLexicon"/> key currently
    /// ends in "-ly", so this list has not yet changed a verdict — but <see cref="SubtypeChaining"/>
    /// needs the exact word, and a candidate defined as "an assembly of..." could otherwise
    /// never chain to an `Assembly` class in the same model. Kept small and closed rather than
    /// inverted (e.g. "not ending in a vowel + ly") because the false-positive rate of a looser
    /// rule is unmeasured and a closed list is exactly as cheap to extend later.
    /// </summary>
    private static readonly HashSet<string> LyNouns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "family", "assembly", "supply", "monopoly", "anomaly", "ally", "rally", "alley",
            "folly"
        };

    /// <summary>
    /// True for a word that qualifies the head noun rather than being it: an adverb (by the
    /// "-ly" suffix or by the closed <see cref="Modifiers"/> list) or a conjunction. The "-ly"
    /// suffix rule defers to <see cref="LyNouns"/> first, since that suffix alone cannot tell
    /// "family" (a noun) from "primarily" (an adverb). Deliberately does NOT treat "-ing"/"-ed"
    /// words as modifiers here — <see cref="ScanHead"/> already has a position-sensitive rule
    /// for those, and it must keep it: "a gathering of people" needs "gathering" as the head,
    /// while "a document acknowledging receipt" needs "document".
    /// </summary>
    private static bool IsModifier(string word)
        => Conjunctions.Contains(word)
           || Modifiers.Contains(word)
           || (!LyNouns.Contains(word)
               && word.Length > 3
               && word.EndsWith("ly", StringComparison.OrdinalIgnoreCase));

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

            // "is often a ranking", "is primarily a hospital", "is and was a drawer": step over
            // the qualifier and keep looking for the noun. Only BEFORE a head is captured —
            // after one, a following word is either a terminator, a participle modifying the
            // head (handled below), or a continuation that legitimately overwrites it.
            if (head.Length == 0 && IsModifier(word))
                continue;

            // Once a head is captured, a conjunction ends the phrase rather than being
            // skipped over — "a room or area" must stop at "room". Without this, the word
            // falls through to the plain "head = word" assignment below and the second
            // disjunct silently wins.
            if (head.Length > 0 && Conjunctions.Contains(word))
                return (head, word, i + 1);

            if (Terminators.Contains(word))
                return (head, word, i + 1);

            // A participle after the head noun modifies it rather than being it - "a document
            // acknowledging..." stops at "acknowledging" and yields "document"; "engaged in
            // trade" after "company" works the same way for the past-tense form. But before any
            // head has been captured, a "-ing"/"-ed" word is the head itself, not a modifier of
            // it - "a gathering of people" must yield "gathering", and "an organized collection
            // of data" must let "organized" through so it can be overwritten by "collection".
            // Irregular past participles ("undertaken", "given", ...) are checked separately
            // since no suffix rule can catch them without also catching ordinary nouns like
            // "chicken" and "kitchen" (see IrregularParticiples).
            if (head.Length > 0 &&
                (word.EndsWith("ing", StringComparison.OrdinalIgnoreCase) ||
                 word.EndsWith("ed", StringComparison.OrdinalIgnoreCase) ||
                 IrregularParticiples.Contains(word)))
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

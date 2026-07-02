using NaturalLanguageEngine.Abstractions.Syntax;
using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Syntax;

/// <summary>
/// Builds dependency edges from the phrase sequence produced by
/// <see cref="Abstractions.Syntax.IPhraseDetector"/>.
///
/// <para>
/// This is a shallow, rule-based dependency parser, not a statistical one -
/// there is no training data for it to learn from. It leans on two
/// structural signals that are already reliable at this point in the
/// pipeline: phrase order and phrase-internal head/modifier structure, to
/// build a real dependency graph in place of the purely positional
/// "closest entity before/after the verb" guesswork that
/// <see cref="Semantics.RelationshipExtractor"/> previously relied on
/// exclusively.
/// </para>
///
/// <para>Two kinds of edges are produced:</para>
/// <list type="bullet">
/// <item><description>
/// <b>Intra-phrase</b>: within a phrase, every non-head token (a
/// determiner/adjective in a noun phrase, an auxiliary/modal in a verb
/// phrase) is attached to that phrase's head, as
/// <see cref="DependencyType.Modifier"/> or <see cref="DependencyType.Predicate"/>
/// respectively. E.g. in "the big red car", <c>car</c> governs <c>the</c>,
/// <c>big</c>, and <c>red</c> as modifiers.
/// </description></item>
/// <item><description>
/// <b>Inter-phrase</b>: each verb phrase becomes the root of its own
/// clause (<see cref="DependencyType.Root"/>). The noun phrase immediately
/// preceding a verb phrase becomes its subject. Noun phrases that follow
/// become, in order: the direct object, then indirect object(s) - unless
/// the phrase is headed by a date/time token, in which case it attaches
/// as a time <see cref="DependencyType.Modifier"/> instead. A second verb
/// phrase later in the sentence starts a new clause with its own
/// subject/object attachments, so simple run-on sentences don't get
/// collapsed into a single clause.
/// </description></item>
/// </list>
///
/// <para>
/// <b>Known limitations</b> (documented rather than hidden): this still
/// assumes active voice and simple word order. Passive constructions
/// ("The report was sent by John"), questions ("Did Sarah book a
/// flight?"), and coordinated subjects/objects ("Sarah and Tom booked
/// flights") are not modeled correctly - the phrase detector doesn't
/// surface the grammatical signals (auxiliary-before-subject inversion,
/// coordinating conjunctions between phrases) that would be needed to
/// handle them. It is nonetheless a real improvement over the previous
/// approach: relationships are now derived from grammatical roles
/// (subject/object/modifier of a specific verb) rather than only "nearest
/// entity," which is what let the old heuristic get confused by anything
/// with more than one action or more than a handful of entities.
/// </para>
/// </summary>
public sealed class DependencyParser : IDependencyParser
{
    public IReadOnlyList<Dependency> Parse(IReadOnlyList<Phrase> phrases)
    {
        var dependencies = new List<Dependency>();

        foreach (var phrase in phrases)
            AddIntraPhraseDependencies(phrase, dependencies);

        AddInterPhraseDependencies(phrases, dependencies);

        return dependencies;
    }

    /// <summary>
    /// Attaches every non-head token in <paramref name="phrase"/> to that
    /// phrase's head (determiners/adjectives to a noun, auxiliaries/modals
    /// to a verb).
    /// </summary>
    private static void AddIntraPhraseDependencies(Phrase phrase, List<Dependency> dependencies)
    {
        if (phrase.Head is null)
            return;

        var type = phrase.Type == PhraseType.VerbPhrase
            ? DependencyType.Predicate
            : DependencyType.Modifier;

        foreach (var token in phrase.Tokens)
        {
            if (ReferenceEquals(token, phrase.Head))
                continue;

            dependencies.Add(new Dependency
            {
                Governor = phrase.Head,
                Dependent = token,
                Type = type
            });
        }
    }

    /// <summary>
    /// Walks the phrase sequence left to right, assigning each verb phrase
    /// as a clause root with a subject (the noun phrase before it) and
    /// object(s)/time-modifiers (the noun phrases after it, up to the next
    /// verb phrase).
    /// </summary>
    private static void AddInterPhraseDependencies(IReadOnlyList<Phrase> phrases, List<Dependency> dependencies)
    {
        TokenAnalysis? currentRoot = null;
        bool sawDirectObjectForCurrentRoot = false;

        // Noun-phrase heads already attached as some clause's object. A
        // second verb phrase immediately following one of these (e.g.
        // "Sarah booked a flight and sent it" - two coordinated verbs
        // sharing an elided subject, with no conjunction surfaced as its
        // own phrase) must not treat that object as its own subject just
        // because it happens to be the preceding phrase.
        var consumedAsObject = new HashSet<TokenAnalysis>();

        for (int i = 0; i < phrases.Count; i++)
        {
            var phrase = phrases[i];
            var head = phrase.Head;
            if (head is null)
                continue;

            if (phrase.Type == PhraseType.VerbPhrase)
            {
                dependencies.Add(new Dependency { Governor = head, Dependent = head, Type = DependencyType.Root });

                if (i > 0
                    && phrases[i - 1].Type == PhraseType.NounPhrase
                    && phrases[i - 1].Head is { } subjectHead
                    && !consumedAsObject.Contains(subjectHead))
                {
                    dependencies.Add(new Dependency { Governor = head, Dependent = subjectHead, Type = DependencyType.Subject });
                }

                currentRoot = head;
                sawDirectObjectForCurrentRoot = false;
                continue;
            }

            // Noun phrase with no verb governing it yet - e.g. a sentence
            // fragment with no verb at all. Nothing to attach it to.
            if (currentRoot is null)
                continue;

            bool isDateOrTime = head.Token.Type is TokenType.Date or TokenType.Time;

            DependencyType depType;
            if (isDateOrTime)
            {
                depType = DependencyType.Modifier;
            }
            else if (!sawDirectObjectForCurrentRoot)
            {
                depType = DependencyType.DirectObject;
                sawDirectObjectForCurrentRoot = true;
            }
            else
            {
                depType = DependencyType.IndirectObject;
            }

            dependencies.Add(new Dependency { Governor = currentRoot, Dependent = head, Type = depType });
            consumedAsObject.Add(head);
        }
    }
}

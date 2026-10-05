using DomainDiscovery.Model;
using NaturalLanguageEngine.Models.Semantics;

namespace DomainDiscovery.SeedExtraction;

/// <summary>
/// Turns an NLE <see cref="SemanticResult"/> into the seed entity model: the
/// nouns the user actually mentioned. Verbs (Action) and Attributes are ignored
/// here — they inform fields/relationships during enrichment, not entities — except a
/// transaction verb, which names the record it makes (see <see cref="TransactionVerbs"/>).
/// </summary>
public sealed class SeedEntityExtractor
{
    /// <summary>The words that make "that books" a verb phrase rather than a noun phrase.</summary>
    private static readonly string[] RelativePronouns = ["that", "which", "who"];

    private static readonly EntityType[] EntityTypes =
    {
        EntityType.Object,
        EntityType.Person,
        EntityType.Organization,
        EntityType.Location
    };

    public CandidateDomainModel Extract(SemanticResult result) => Extract(result, description: null);

    /// <summary>
    /// As <see cref="Extract(SemanticResult)"/>, with the description the result was analysed from,
    /// so a transaction verb the engine typed as a thing can be told from a noun by where it stands.
    /// </summary>
    public CandidateDomainModel Extract(SemanticResult result, string? description)
    {
        var model = new CandidateDomainModel();

        // Every entity is walked, not just the ones whose type qualifies: a word the engine
        // typed as something other than a thing is exactly the case a user most wants
        // explained, and filtering it out here would leave nothing to explain it with.
        var words = WordsOf(description);

        // Whether the entity before this one was read as a verb. Its next noun is then the verb's
        // object - a thing the user named - and never a verb itself: in "that records orders"
        // and "that ships orders", "orders" is what is recorded or shipped.
        var previousWasAVerb = false;

        for (var i = 0; i < result.Entities.Count; i++)
        {
            var entity = result.Entities[i];
            var previous = i > 0 ? result.Entities[i - 1] : null;
            var next = i + 1 < result.Entities.Count ? result.Entities[i + 1] : null;
            var afterAVerb = previousWasAVerb;
            previousWasAVerb = false;

            var record = RecordMadeBy(entity) ?? (afterAVerb ? null : BareVerbRecord(entity, previous, words));

            if (record is null && (VerbPhrase(entity) || (!afterAVerb && BareVerb(entity, previous, next, words))))
            {
                previousWasAVerb = true;
                model.Record(NameOrText(entity), DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.ItIsAVerbHere,
                    $"\"{entity.Text}\" says what is done, not a thing that is kept");
                continue;
            }

            if (record is not null)
            {
                previousWasAVerb = true;
                var verbWord = entity.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];

                if (TransactionVerbs.MakesNoRecordForAPerson(verbWord) && IsAPerson(next))
                {
                    model.Record(entity.Text, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                        DecisionReasons.NotRecognizedAsAThing,
                        $"to {verbWord.ToLowerInvariant()} {next!.Text} is to employ them, not a rental");
                    continue;
                }

                AddRecord(model, record, entity);
                continue;
            }

            if (!EntityTypes.Contains(entity.Type))
            {
                model.Record(entity.Text, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.NotRecognizedAsAThing,
                    $"the language engine read it as {entity.Type}");
                continue;
            }

            var name = NameNormalizer.ToEntityName(entity.Text);
            if (name.Length == 0)
            {
                model.Record(entity.Text, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.NameNormalizedToNothing);
                continue;
            }

            // Compared by spelling key: "enrolls" made Enrolment, and "enrollments" is the same
            // class spelled the American way.
            if (model.Entities.FirstOrDefault(e => SameName(e.Name, name)) is { } taken)
            {
                model.Record(entity.Text, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.AlreadyNamed,
                    $"already taken by {taken.Name}");
                continue;
            }

            // Fields are attached later, by EntityShaper, once the topic is known.
            model.Entities.Add(new CandidateEntity
            {
                Name = name,
                Provenance = Provenance.Seed,
                Confidence = entity.Confidence
            });

            model.Record(name, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                DecisionReasons.YouNamedIt, $"confidence {(double)entity.Confidence:0.00}, from \"{entity.Text}\"");
        }

        return model;
    }

    /// <summary>
    /// The record <paramref name="entity"/> makes when it is a transaction verb, or null.
    /// </summary>
    /// <remarks>
    /// A verb reaches here in one of two shapes: typed as an Action ("lends"), or - when the
    /// lexicon knows the word as a noun, or not at all - typed as a thing, inside the phrase the
    /// relative pronoun opened ("that books", "that enrols"). Until this existed the second shape
    /// became a class named after the verb: Book in a clinic, Enrol in a school. A pronoun before
    /// a word the table does not know is left alone, since "that" is as often a determiner
    /// ("that data") and only a known verb can say which it is.
    /// </remarks>
    private static string? RecordMadeBy(Entity entity)
    {
        var words = entity.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (entity.Type == EntityType.Action)
            return words.Length == 1 ? TransactionVerbs.RecordFor(words[0]) : null;

        if (!EntityTypes.Contains(entity.Type) || words.Length != 2
            || !RelativePronouns.Contains(words[0], StringComparer.OrdinalIgnoreCase))
            return null;

        return TransactionVerbs.RecordFor(words[1]);
    }

    /// <summary>
    /// The record a transaction verb makes when the engine typed it as a thing and no relative
    /// pronoun marks it: "a school ENROLS students", "a hotel BOOKS rooms".
    /// </summary>
    /// <remarks>
    /// The entities alone cannot tell "a hotel books rooms" from "a library of books and dvds" -
    /// both are a noun phrase, "books", another noun - so the description decides. The word must
    /// stand straight after the noun before it, be an inflected form (books, enrolled; never the
    /// bare "book", which is as often a noun), and be followed by its object rather than by "and"
    /// or nothing. "of books" and "with orders" fail the first test, which is what keeps Book in
    /// a library.
    /// </remarks>
    private static string? BareVerbRecord(Entity entity, Entity? previous, IReadOnlyList<string> words)
    {
        if (previous is null || words.Count == 0
            || !EntityTypes.Contains(entity.Type) || !EntityTypes.Contains(previous.Type))
            return null;

        var verb = entity.Text.Trim().ToLowerInvariant();
        if (verb.Contains(' ') || TransactionVerbs.InflectedRecordFor(verb) is not { } record)
            return null;

        var before = previous.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant();

        // "hires staff AND pays salaries": a second verb joined to the first verb's object.
        for (var j = 0; j + 2 < words.Count; j++)
        {
            var at = words[j + 1] == "and" && j + 3 < words.Count ? j + 2 : j + 1;
            if (words[j] == before && words[at] == verb && at + 1 < words.Count
                && !Conjunctions.Contains(words[at + 1]))
                return record;
        }

        return null;
    }

    private static readonly string[] Conjunctions = ["and", "or", "nor"];

    /// <summary>
    /// Whether <paramref name="entity"/> is a relative pronoun and a verb the engine typed as a
    /// thing: "that records", "that treats", "that signs".
    /// </summary>
    /// <remarks>
    /// "that" before a word can be a determiner ("that data"), but a determiner "that" takes a
    /// singular noun - "those records", never "that records" - so a word ending in -s after it is
    /// a verb. The words that end in -s and are still singular (status, process, analysis, news)
    /// are left alone.
    /// </remarks>
    private static bool VerbPhrase(Entity entity)
    {
        var parts = entity.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return EntityTypes.Contains(entity.Type) && parts.Length == 2
               && RelativePronouns.Contains(parts[0], StringComparer.OrdinalIgnoreCase)
               && LooksLikeAVerbForm(parts[1]);
    }

    /// <summary>
    /// Whether <paramref name="entity"/> is a bare verb standing between its subject and its
    /// object: "a warehouse RECORDS orders", "a library of books KEEPS loans".
    /// </summary>
    /// <remarks>
    /// Its words must stand straight after the noun before it and straight before the noun after
    /// it - "a store with orders and payments" and "orders and payments" fail - and the noun after
    /// it must not be the software ("a school records system", where "records" is a noun).
    /// </remarks>
    private static bool BareVerb(Entity entity, Entity? previous, Entity? next, IReadOnlyList<string> words)
    {
        if (previous is null || next is null || words.Count == 0
            || !EntityTypes.Contains(entity.Type) || !EntityTypes.Contains(previous.Type)
            || !EntityTypes.Contains(next.Type))
            return false;

        var verb = entity.Text.Trim().ToLowerInvariant();
        if (verb.Contains(' ') || !LooksLikeAVerbForm(verb))
            return false;

        var before = previous.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].ToLowerInvariant();
        var after = next.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        var head = next.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].ToLowerInvariant();

        if (SoftwareWords.Contains(head))
            return false;

        // "... for classes AND employs staff": a second verb joined to the first verb's object.
        for (var j = 0; j + 2 < words.Count; j++)
        {
            var at = words[j + 1] == "and" ? j + 2 : j + 1;
            if (words[j] == before && at + 1 < words.Count && words[at] == verb && words[at + 1] == after)
                return true;
        }

        return false;
    }

    private static readonly string[] SoftwareWords =
        ["system", "systems", "app", "application", "software", "database", "platform", "tool", "portal"];

    private static readonly string[] SingularWordsEndingInS =
        ["news", "series", "species", "means", "data", "lens", "gas", "bus", "chaos"];

    // A third-person verb: records, logs, treats, schedules. Not a word whose singular ends in -s.
    private static bool LooksLikeAVerbForm(string word)
    {
        var w = word.ToLowerInvariant();
        return w.Length > 3 && w.EndsWith('s') && w.All(char.IsLetter)
               && !w.EndsWith("ss", StringComparison.Ordinal)
               && !w.EndsWith("us", StringComparison.Ordinal)
               && !w.EndsWith("is", StringComparison.Ordinal)
               && !SingularWordsEndingInS.Contains(w);
    }

    // The name `why` is asked with: "that records" is asked about as Record.
    private static string NameOrText(Entity entity)
    {
        var name = NameNormalizer.ToEntityName(entity.Text);
        return name.Length == 0 ? entity.Text : name;
    }

    // The archetype table already knows which nouns are people (staff, nurse, driver...).
    private static bool IsAPerson(Entity? entity)
    {
        if (entity is null)
            return false;
        if (entity.Type == EntityType.Person)
            return true;

        var last = entity.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        return Enrichment.ArchetypeLexicon.Resolve(last).Role == Enrichment.EntityRole.Party;
    }

    private static IReadOnlyList<string> WordsOf(string? description) =>
        System.Text.RegularExpressions.Regex.Matches(description ?? string.Empty, "[A-Za-z']+")
            .Select(m => m.Value.ToLowerInvariant())
            .ToList();

    private static bool SameName(string a, string b)
        => NameNormalizer.SpellingKey(a) == NameNormalizer.SpellingKey(b);

    private static void AddRecord(CandidateDomainModel model, string record, Entity verb)
    {
        // "a car rental company that rents vehicles" names the Rental twice, once each way.
        if (model.Entities.Any(e => SameName(e.Name, record)))
        {
            model.Record(verb.Text, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                DecisionReasons.AlreadyNamed, $"already taken by {record}");
            return;
        }

        model.Entities.Add(new CandidateEntity
        {
            Name = record,
            Provenance = Provenance.Seed,
            Confidence = verb.Confidence
        });

        model.Record(record, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouSaidItAsAVerb,
            $"confidence {(double)verb.Confidence:0.00}, from \"{verb.Text}\"");
    }
}

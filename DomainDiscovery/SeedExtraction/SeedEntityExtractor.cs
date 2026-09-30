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

    public CandidateDomainModel Extract(SemanticResult result)
    {
        var model = new CandidateDomainModel();

        // Every entity is walked, not just the ones whose type qualifies: a word the engine
        // typed as something other than a thing is exactly the case a user most wants
        // explained, and filtering it out here would leave nothing to explain it with.
        foreach (var entity in result.Entities)
        {
            if (RecordMadeBy(entity) is { } record)
            {
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

            if (model.Entities.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                model.Record(entity.Text, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.AlreadyNamed,
                    $"already taken by {name}");
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

    private static void AddRecord(CandidateDomainModel model, string record, Entity verb)
    {
        // "a car rental company that rents vehicles" names the Rental twice, once each way.
        if (model.Entities.Any(e => string.Equals(e.Name, record, StringComparison.OrdinalIgnoreCase)))
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

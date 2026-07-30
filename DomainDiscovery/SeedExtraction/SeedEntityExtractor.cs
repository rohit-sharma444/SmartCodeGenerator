using DomainDiscovery.Model;
using NaturalLanguageEngine.Models.Semantics;

namespace DomainDiscovery.SeedExtraction;

/// <summary>
/// Turns an NLE <see cref="SemanticResult"/> into the seed entity model: the
/// nouns the user actually mentioned. Verbs (Action) and Attributes are ignored
/// here — they inform fields/relationships during enrichment, not entities.
/// </summary>
public sealed class SeedEntityExtractor
{
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
}

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

        foreach (var entity in result.Entities.Where(e => EntityTypes.Contains(e.Type)))
        {
            var name = NameNormalizer.ToEntityName(entity.Text);
            if (name.Length == 0)
                continue;

            if (model.Entities.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            model.Entities.Add(new CandidateEntity
            {
                Name = name,
                Provenance = Provenance.Seed,
                Confidence = entity.Confidence
            });
        }

        return model;
    }
}

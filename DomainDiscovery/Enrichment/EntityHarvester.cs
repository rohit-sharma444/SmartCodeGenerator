using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Models.Semantics;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Mines a domain model out of an NLE analysis of article prose: noun-type
/// entities become candidate entities (frequency-ranked); Attribute targets of
/// relationships become typed fields on their source entity.
/// </summary>
public sealed class EntityHarvester : IEntityHarvester
{
    private static readonly EntityType[] EntityTypes =
    {
        EntityType.Object,
        EntityType.Person,
        EntityType.Organization,
        EntityType.Location
    };

    public CandidateDomainModel Harvest(SemanticResult analysis)
    {
        var model = new CandidateDomainModel();

        // 1. Count noun mentions by normalized name.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in analysis.Entities.Where(e => EntityTypes.Contains(e.Type)))
        {
            var name = NameNormalizer.ToEntityName(entity.Text);
            if (name.Length == 0)
                continue;

            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        if (counts.Count == 0)
            return model;

        var max = counts.Values.Max();

        // 2. Frequency-ranked entities with normalized confidence.
        foreach (var (name, count) in counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
        {
            model.Entities.Add(new CandidateEntity
            {
                Name = name,
                Provenance = Provenance.Wiki,
                Confidence = (double)count / max
            });
        }

        // 3. Attribute targets of relationships become typed fields.
        foreach (var rel in analysis.Relationships)
        {
            if (rel.Source is null || rel.Target is null || rel.Target.Type != EntityType.Attribute)
                continue;

            var ownerName = NameNormalizer.ToEntityName(rel.Source.Text);
            var owner = model.Entities.FirstOrDefault(
                e => string.Equals(e.Name, ownerName, StringComparison.OrdinalIgnoreCase));
            if (owner is null)
                continue;

            var fieldName = NameNormalizer.ToEntityName(rel.Target.Text);
            if (fieldName.Length == 0
                || owner.Fields.Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
                continue;

            owner.Fields.Add(new CandidateField
            {
                Name = fieldName,
                Type = TypeInference.Infer(fieldName),
                Provenance = Provenance.Wiki
            });
        }

        return model;
    }
}

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
    /// <summary>
    /// A whole article's vocabulary is far too much to review or generate classes from, so only
    /// the most-mentioned nouns are kept. This also strands content-word noise (adjectives,
    /// adverbs), which prose mentions once or twice at most.
    /// </summary>
    public const int DefaultMaxEntities = 20;

    /// <summary>
    /// Shortest name that can be a domain entity. Tokenizing prose leaves fragments behind
    /// ("th" from "18th century", "d" from "D.C."), and the tagger's fallback calls them nouns.
    /// Three letters keeps real entities such as Tax and Pay.
    /// </summary>
    private const int MinNameLength = 3;

    private static readonly EntityType[] EntityTypes =
    {
        EntityType.Object,
        EntityType.Person,
        EntityType.Organization,
        EntityType.Location
    };

    private readonly int _maxEntities;

    public EntityHarvester(int maxEntities = DefaultMaxEntities)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntities, 1);
        _maxEntities = maxEntities;
    }

    public CandidateDomainModel Harvest(SemanticResult analysis)
    {
        var model = new CandidateDomainModel();

        // 1. Count noun mentions by normalized name.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in analysis.Entities.Where(e => EntityTypes.Contains(e.Type)))
        {
            // Screen the raw token as well as the normalized name: singularizing turns the
            // stop word "does" into "Doe", which no longer matches the stop list.
            if (StopWords.Contains(entity.Text))
                continue;

            var name = NameNormalizer.ToEntityName(entity.Text);
            if (name.Length < MinNameLength || StopWords.Contains(name))
                continue;

            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        if (counts.Count == 0)
            return model;

        var max = counts.Values.Max();

        // 2. Frequency-ranked entities with normalized confidence, capped.
        foreach (var (name, count) in counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(_maxEntities))
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

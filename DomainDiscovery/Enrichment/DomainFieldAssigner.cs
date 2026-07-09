using DomainDiscovery.Model;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Shapes a discovered model: every entity gets the fields <see cref="DomainFieldLexicon"/>
/// knows for it, and every entity the lexicon does not know is dropped.
/// </summary>
/// <remarks>
/// Runs once, on the finished model, rather than inside the seed extractor or the harvester:
/// topic recognition searches on the seed entity names, so they must survive until the article
/// has been chosen.
/// </remarks>
public static class DomainFieldAssigner
{
    /// <summary>
    /// Attaches known fields and removes entities that have none. Fields already on an entity
    /// — added by hand in the review loop — are kept and never duplicated.
    /// </summary>
    public static void Apply(CandidateDomainModel model)
    {
        foreach (var entity in model.Entities.ToList())
        {
            foreach (var shape in DomainFieldLexicon.Fields(entity.Name))
            {
                if (entity.Fields.Any(f => string.Equals(f.Name, shape.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                entity.Fields.Add(new CandidateField
                {
                    Name = shape.Name,
                    Type = shape.Type,
                    Provenance = entity.Provenance
                });
            }

            // A noun whose shape we cannot name is prose, not a domain class. Generating
            // <Name>.cs for it produces a tab with nothing but a read-only Id.
            if (entity.Fields.Count == 0)
                model.Entities.Remove(entity);
        }
    }
}

using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Shapes a discovered model, and decides what is a domain class at all.
/// </summary>
/// <remarks>
/// <para>
/// Replaces DomainFieldAssigner, which answered both questions with one lookup in
/// <see cref="DomainFieldLexicon"/> — so an entity whose fields were merely uncurated was
/// deleted as though it were prose. Sixteen of the twenty candidates harvested from the
/// Point of sale article died that way.
/// </para>
/// <para>
/// Shape resolves in precedence order: the curated lexicon, then the archetype implied by
/// the candidate's own Wikipedia definition, then nothing. An abstract definition ("a
/// system is a group of interacting elements") rejects the candidate — that is the drop
/// signal, and unlike an allow-list it needs no curation per domain. An absent or unreadable
/// definition means we do not know, and the candidate is offered to the user instead.
/// </para>
/// </remarks>
public sealed class EntityShaper
{
    private readonly IWikipediaClient _client;

    /// <summary>
    /// Wires the client used to fetch a candidate's own lead sentence when the curated
    /// lexicon has nothing to say about it.
    /// </summary>
    public EntityShaper(IWikipediaClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>
    /// Shapes every entity in <paramref name="model"/> in place: attaches known fields, and
    /// for anything still fieldless resolves the three-way verdict — shaped, abstract
    /// (rejected), or unknown (suggested) — except for a seed entity, which the user asserted
    /// exists and so is never dropped or demoted, whatever the lexicon or article says.
    /// </summary>
    public void Shape(CandidateDomainModel model)
    {
        foreach (var entity in model.Entities.ToList())
        {
            foreach (var shape in DomainFieldLexicon.Fields(entity.Name))
                AddField(entity, shape.Name, shape.Type);

            if (entity.Fields.Count > 0)
                continue;

            // The user named it: they have asserted it exists. Never dropped, never demoted.
            if (entity.Provenance == Provenance.Seed)
            {
                DefaultFields.ApplyTo(entity, Provenance.Seed);
                continue;
            }

            var lead = _client.FetchLead(entity.SourceTitle ?? entity.Name);
            var result = ArchetypeLexicon.Resolve(HypernymExtractor.Extract(lead));

            switch (result.Verdict)
            {
                case ShapeVerdict.Shaped:
                    foreach (var shape in result.Fields)
                        AddField(entity, shape.Name, shape.Type);
                    break;

                case ShapeVerdict.Abstract:
                    model.Entities.Remove(entity);
                    model.Rejected.Add(entity.Name);
                    break;

                default:
                    model.Entities.Remove(entity);
                    model.Suggestions.Add(entity);
                    break;
            }
        }
    }

    /// <summary>Adds a field unless the entity already declares one of that name.</summary>
    private static void AddField(CandidateEntity entity, string name, string type)
    {
        if (entity.Fields.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        entity.Fields.Add(new CandidateField
        {
            Name = name,
            Type = type,
            Provenance = entity.Provenance
        });
    }
}

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
/// <para>
/// <see cref="Provenance.Seed"/> earns no exemption from that gate. SeedEntityExtractor
/// turns every noun in the user's typed phrase into a fieldless Seed entity — an extracted
/// seed is a guess like any other, not an assertion, so it is shaped, rejected, or suggested
/// exactly like a Wikipedia candidate. The only fieldless entity this method lets through
/// unshaped is one that already carries a field: that is what an entity the user adds by
/// hand in the review loop looks like, because <see cref="Review.ModelEditor.AddEntity"/>
/// runs after discovery and gives it a field before Shape ever sees it.
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
    /// (rejected), or unknown (suggested). A seed entity gets no exemption here: it is a noun
    /// SeedEntityExtractor lifted straight out of the user's typed phrase, a guess like any
    /// Wikipedia candidate, so it takes the same gate. The only fieldless-and-kept case is
    /// impossible to reach through discovery: <see cref="Review.ModelEditor.AddEntity"/> already
    /// gives an entity the user adds by hand a field (its own lexicon shape, or the
    /// <see cref="DefaultFields"/> fallback) before this method ever sees it, which is what
    /// the fields-count check below actually protects.
    /// </summary>
    public void Shape(CandidateDomainModel model)
    {
        var entities = model.Entities.ToList();

        // Pass 1: the curated lexicon. This must run for every entity before any lead is
        // fetched, since an entity it fully covers needs no lead at all.
        foreach (var entity in entities)
        {
            foreach (var shape in DomainFieldLexicon.Fields(entity.Name))
                AddField(entity, shape.Name, shape.Type);
        }

        // Prefetch every remaining entity's lead in ONE call instead of one per entity — a
        // real domain can leave dozens of candidates uncurated, and fetching those leads one
        // at a time is what drove the Wikipedia API to 429 well before they were all in.
        var titlesNeedingLeads = entities
            .Where(e => e.Fields.Count == 0)
            .Select(e => e.SourceTitle ?? e.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var leads = _client.FetchLeads(titlesNeedingLeads);

        // Pass 2: resolve the three-way verdict for anything still fieldless.
        foreach (var entity in entities)
        {
            if (entity.Fields.Count > 0)
                continue;

            var title = entity.SourceTitle ?? entity.Name;
            var lead = leads.TryGetValue(title, out var fetchedLead) ? fetchedLead : string.Empty;
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

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
            var curatedFields = DomainFieldLexicon.Fields(entity.Name);

            // Role precedence mirrors field precedence exactly: the curated lexicon wins over
            // the archetype (assigned below, in pass 2) over EntityRole.None (the default).
            if (curatedFields.Count > 0)
                entity.Role = DomainFieldLexicon.Role(entity.Name);

            foreach (var shape in curatedFields)
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
                    entity.Role = result.Role;
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

        // Pass 3: widen every surviving class. The role template fills in the baseline its kind
        // implies, and PropertyMiner reads the class's own article for what makes it specific.
        //
        // This pass runs LAST, over model.Entities rather than the local list, so a class rejected
        // or demoted to a suggestion above is never mined: a class we decided is not a class must
        // not acquire properties on its way out.
        Widen(model);
    }

    /// <summary>
    /// Adds the role baseline and the mined properties to every surviving class.
    /// </summary>
    /// <remarks>
    /// Reading each class's own article is the thing the MediaWiki API's rate limit forbade — it is
    /// one fetch per class, and a real domain has twenty. Against the dump store it is a local
    /// seek, which is what makes this pass affordable at all.
    /// <para>
    /// Both sources are additive and neither can overwrite a field that is already there: precedence
    /// runs curated, then archetype, then template, then mined, and AddField silently declines a
    /// name already taken.
    /// </para>
    /// </remarks>
    private void Widen(CandidateDomainModel model)
    {
        foreach (var entity in model.Entities)
        {
            foreach (var shape in RoleTemplate.For(entity.Role))
                AddField(entity, shape.Name, shape.Type);

            var article = _client.FetchExtract(entity.SourceTitle ?? entity.Name);
            if (string.IsNullOrWhiteSpace(article))
                continue;

            // The FULL article now, not just the lead. PropertyMiner scopes each of its sources
            // itself: the containment-list and prose-enumeration sources still read only the
            // lead (below it they mined junk — Merchant's CalmlyConsidered, Inventory's
            // FIFOAndLIFO, Receipt's stray SalesTax neighbours), while the composition-list
            // source reads the whole body but fires only under a "Format"/"Header"/"Contents"
            // heading, which is where an article actually states a document's fields.
            foreach (var shape in PropertyMiner.Mine(article, entity.Name))
                AddField(entity, shape.Name, shape.Type);
        }
    }

    /// <summary>Adds a field unless the entity already declares one of that name, or the name
    /// collides with the entity's own name. Mirrors <see cref="Review.ModelEditor.AddField"/>:
    /// `class Model { public string Model { get; set; } }` is CS0542 and will not compile. That
    /// guard is name-agnostic there because a user can type any field name; here the field comes
    /// from an archetype shape that knows nothing about the entity's own name, so an entity such
    /// as "Model" or "Total" resolving to a device/document archetype would otherwise emit an
    /// uncompilable class.</summary>
    private static void AddField(CandidateEntity entity, string name, string type)
    {
        if (entity.Fields.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        if (string.Equals(name, entity.Name, StringComparison.OrdinalIgnoreCase))
            return;

        entity.Fields.Add(new CandidateField
        {
            Name = name,
            Type = type,
            Provenance = entity.Provenance
        });
    }
}

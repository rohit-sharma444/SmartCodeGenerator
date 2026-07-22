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
/// the candidate's own Wikipedia definition (the head noun), then the archetype implied by
/// the candidate's own NAME when the head has nothing to say (most commonly a disambiguation
/// page, where no head can be extracted at all), then chaining (see <see cref="SubtypeChaining"/>),
/// then nothing. An abstract definition ("a system is a group of interacting elements") rejects
/// the candidate — that is the drop signal, and unlike an allow-list it needs no curation per
/// domain. An absent or unreadable definition means we do not know, and the candidate is
/// offered to the user instead.
/// </para>
/// <para>
/// <see cref="Provenance.Seed"/> earns an exemption from ONE of the three verdicts. A seed is a
/// noun SeedEntityExtractor lifted out of the user's typed phrase, so it is shaped and rejected
/// exactly like a Wikipedia candidate — but where the verdict is <see cref="ShapeVerdict.Unknown"/>,
/// meaning we have no opinion, the user having named the thing is the only evidence available and
/// the candidate is kept with <see cref="DefaultFields"/> rather than offered back to them.
/// <see cref="ShapeVerdict.Abstract"/> still rejects it, so "system" and "application" cannot
/// become classes this way. Without this, `build app: a hotel booking application` produced no
/// Booking class at all.
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
    /// (rejected), or unknown (suggested). A seed entity takes the same gate as everything else,
    /// except that an Unknown verdict keeps it (with <see cref="DefaultFields"/>) rather than
    /// suggesting it — the user named it, and Unknown means we have nothing to say against that.
    /// The only other fieldless-and-kept case is impossible to reach through discovery:
    /// <see cref="Review.ModelEditor.AddEntity"/> already
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

        // Pass 2: resolve a verdict for everything still fieldless, WITHOUT acting on any of
        // them yet. Acting as we walked meant a candidate's fate depended on where it sat in
        // the list, and it left chaining (below) nothing to chain to: the parent it needed may
        // already have been removed.
        var pending = new List<CandidateEntity>();
        var candidates = new List<ShapeCandidate>();

        foreach (var entity in entities)
        {
            if (entity.Fields.Count > 0)
                continue;

            var title = entity.SourceTitle ?? entity.Name;
            var lead = leads.TryGetValue(title, out var fetchedLead) ? fetchedLead : string.Empty;
            var head = HypernymExtractor.Extract(lead);
            var result = ArchetypeLexicon.Resolve(head);

            // The head wins when it resolves, because the article's own definition is better
            // evidence than the candidate's name alone — "a merchant is a person" should not
            // be overridden by whatever "merchant" might resolve to on its own. But when the
            // head says nothing (Unknown) — most commonly because no head could be extracted
            // at all, as on a disambiguation page ("Booking may refer to:") — the candidate's
            // own name is still a signal ArchetypeLexicon has never been asked about: Resolve
            // ("booking") is Event, Resolve("sale") is Transaction, Resolve("administration")
            // is Abstract. Falling back to it here recovers exactly the candidates whose
            // article defines nothing, without ever overriding one that does.
            if (result.Verdict == ShapeVerdict.Unknown)
            {
                var ownNameResult = ArchetypeLexicon.Resolve(entity.Name);
                if (ownNameResult.Verdict != ShapeVerdict.Unknown)
                    result = ownNameResult;
            }

            pending.Add(entity);
            candidates.Add(new ShapeCandidate(entity.Name, head, result));
        }

        // Pass 2b: a candidate the table has no opinion on may still be defined as one it does
        // — "a motel is a hotel". This needs every candidate at once, which is why it happens
        // here and not per-entity above.
        var resolved = SubtypeChaining.Resolve(candidates);

        // Pass 2c: now act, once, on a verdict nothing further can change.
        foreach (var entity in pending)
        {
            var result = resolved[entity.Name];

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
                    // Unknown means "we have no opinion". Where we have none, the user having
                    // typed the word IS the evidence: SeedEntityExtractor only ever produces a
                    // Seed candidate from the description the user wrote. Abstract still
                    // rejects above, so "system" and "application" cannot get in this way.
                    // Note: ShapeVerdict.Unknown conflates two distinct cases: an article that
                    // defines nothing we recognize, and a lead that could not be fetched at all
                    // (a disambiguation page, a missing article, or a failed lookup). Before this
                    // change the ambiguity only cost a suggestion; now a seed whose lookup merely
                    // failed is minted as a class carrying only a Name field, indistinguishable
                    // from a genuinely unrecognized noun. This is an accepted trade: the user
                    // typed the word, and a visible spurious class costs one remove in the review
                    // loop, whereas a silently missing class costs the user a class they asked for.
                    if (entity.Provenance == Provenance.Seed)
                    {
                        DefaultFields.ApplyTo(entity, Provenance.Seed);
                        break;
                    }

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

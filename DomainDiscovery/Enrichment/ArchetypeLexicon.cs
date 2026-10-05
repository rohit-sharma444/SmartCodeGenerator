using DomainDiscovery.SeedExtraction;
using FieldShape = DomainDiscovery.Enrichment.DomainFieldLexicon.FieldShape;

namespace DomainDiscovery.Enrichment;

/// <summary>What a hypernym tells us about a candidate.</summary>
public enum ShapeVerdict
{
    /// <summary>The head names a kind of thing we can shape.</summary>
    Shaped,

    /// <summary>The head names an abstraction ("a group of interacting elements"), so the
    /// candidate is prose, not a domain class.</summary>
    Abstract,

    /// <summary>We have no opinion. The candidate is offered to the user, not deleted.</summary>
    Unknown
}

/// <summary>
/// A hypernym's verdict on a candidate, with its inferred fields. <c>Fields</c> is empty except when
/// <c>Verdict</c> is <see cref="ShapeVerdict.Shaped"/>: an <see cref="ShapeVerdict.Abstract"/> verdict
/// rejects the candidate outright, and <see cref="ShapeVerdict.Unknown"/> has no opinion, so neither
/// case carries a shape. <c>Role</c> is <see cref="EntityRole.None"/> for the same two cases — a
/// rejected or unresolved candidate has no kind for a relationship to point at.
/// </summary>
public readonly record struct ArchetypeResult(
    ShapeVerdict Verdict, IReadOnlyList<FieldShape> Fields, EntityRole Role);

/// <summary>
/// Maps the head noun of a definition to a field shape. This is the only curation in the
/// discovery path, and unlike <see cref="DomainFieldLexicon"/> it is domain-independent:
/// "device" shapes a cash register, a payment terminal and an MRI scanner alike, so a new
/// topic needs no new entries.
/// </summary>
public static class ArchetypeLexicon
{
    private static FieldShape Text(string name) => new(name, "string");
    private static FieldShape Money(string name) => new(name, "decimal");
    private static FieldShape When(string name) => new(name, "DateTime");

    // One field set per role, named once so the table below reads as a list of heads rather
    // than a wall of repeated literals. These are the values the table already used; the
    // fuller baseline each class ends up with comes from RoleTemplate, applied afterwards by
    // EntityShaper.Widen. Keeping them separate is deliberate — see RoleTemplate's remarks.
    private static readonly FieldShape[] PlaceFields = [Text("Name"), Text("Address")];
    private static readonly FieldShape[] PartyFields = [Text("FullName"), Text("Email"), Text("Phone")];
    private static readonly FieldShape[] DeviceFields = [Text("Name"), Text("Model"), Text("SerialNumber")];
    private static readonly FieldShape[] ItemFields = [Text("Name"), Text("Sku"), Money("Price")];
    private static readonly FieldShape[] DocumentFields = [Text("Number"), When("IssuedOn"), Money("Total")];
    private static readonly FieldShape[] TransactionFields = [Money("Amount"), When("OccurredOn"), Text("Method")];
    private static readonly FieldShape[] EventFields = [Text("Reference"), When("StartsOn"), When("EndsOn")];

    private static (FieldShape[] Fields, EntityRole Role) Place => (PlaceFields, EntityRole.Place);
    private static (FieldShape[] Fields, EntityRole Role) Party => (PartyFields, EntityRole.Party);
    private static (FieldShape[] Fields, EntityRole Role) Device => (DeviceFields, EntityRole.Device);
    private static (FieldShape[] Fields, EntityRole Role) Item => (ItemFields, EntityRole.Item);
    private static (FieldShape[] Fields, EntityRole Role) Document => (DocumentFields, EntityRole.Document);
    private static (FieldShape[] Fields, EntityRole Role) Transaction => (TransactionFields, EntityRole.Transaction);
    private static (FieldShape[] Fields, EntityRole Role) Event => (EventFields, EntityRole.Event);

    private static readonly Dictionary<string, (FieldShape[] Fields, EntityRole Role)> Archetypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // --- Place: somewhere a domain keeps things or does business ---
            ["institution"] = Place, ["facility"] = Place, ["building"] = Place,
            ["establishment"] = Place, ["venue"] = Place,
            ["site"] = Place, ["room"] = Place, ["space"] = Place, ["department"] = Place,
            ["centre"] = Place, ["center"] = Place, ["complex"] = Place,
            ["warehouse"] = Place, ["school"] = Place, ["hospital"] = Place,
            ["hotel"] = Place, ["restaurant"] = Place, ["clinic"] = Place,
            ["office"] = Place, ["factory"] = Place, ["station"] = Place,
            ["laboratory"] = Place, ["library"] = Place, ["museum"] = Place,
            ["structure"] = Place, ["house"] = Place, ["home"] = Place,
            ["business"] = Place, ["organization"] = Place, ["organisation"] = Place,
            ["company"] = Place, ["shop"] = Place, ["store"] = Place,
            ["city"] = Place, ["town"] = Place, ["location"] = Place, ["place"] = Place,

            // --- Party: somebody the domain deals with ---
            ["person"] = Party, ["individual"] = Party, ["professional"] = Party,
            ["practitioner"] = Party, ["employee"] = Party, ["worker"] = Party,
            ["staff"] = Party, ["physician"] = Party, ["doctor"] = Party,
            ["nurse"] = Party, ["surgeon"] = Party, ["teacher"] = Party,
            ["instructor"] = Party, ["student"] = Party, ["pupil"] = Party,
            ["member"] = Party, ["user"] = Party, ["client"] = Party,
            ["customer"] = Party, ["recipient"] = Party, ["buyer"] = Party,
            ["seller"] = Party, ["merchant"] = Party, ["guest"] = Party,
            ["patient"] = Party, ["passenger"] = Party, ["traveller"] = Party,
            ["traveler"] = Party, ["owner"] = Party, ["supplier"] = Party,
            ["vendor"] = Party, ["manager"] = Party, ["driver"] = Party,
            ["operator"] = Party, ["agent"] = Party, ["contractor"] = Party,
            ["subscriber"] = Party, ["applicant"] = Party, ["candidate"] = Party,
            ["author"] = Party, ["resident"] = Party, ["tenant"] = Party,
            ["landlord"] = Party,

            // --- Device: a machine somebody operates ---
            ["device"] = Device, ["machine"] = Device, ["terminal"] = Device,
            ["equipment"] = Device, ["display"] = Device, ["computer"] = Device,
            ["appliance"] = Device, ["scanner"] = Device, ["printer"] = Device,
            ["sensor"] = Device, ["engine"] = Device, ["apparatus"] = Device,

            // --- Item: a thing bought, held, stocked or consumed ---
            ["item"] = Item, ["product"] = Item, ["good"] = Item,
            ["merchandise"] = Item, ["commodity"] = Item, ["vehicle"] = Item,
            ["aircraft"] = Item, ["car"] = Item, ["truck"] = Item, ["bus"] = Item,
            ["material"] = Item, ["substance"] = Item, ["food"] = Item,
            ["beverage"] = Item, ["drink"] = Item, ["meal"] = Item, ["dish"] = Item,
            ["drug"] = Item, ["medicine"] = Item, ["medication"] = Item,
            ["tool"] = Item, ["instrument"] = Item, ["animal"] = Item,
            ["book"] = Item, ["publication"] = Item, ["part"] = Item,
            ["piece"] = Item, ["component"] = Item, ["ingredient"] = Item,
            ["container"] = Item, ["package"] = Item, ["garment"] = Item,
            ["fuel"] = Item,

            // A payment card is an Item you hold, and its own three fields beat the generic
            // Item ones, so it keeps its bespoke entry.
            ["card"] = ([Text("Number"), Text("Holder"), When("ExpiresOn")], EntityRole.Item),

            // --- Document: a record issued about something ---
            ["document"] = Document, ["record"] = Document, ["statement"] = Document,
            ["account"] = Document, ["licence"] = Document, ["license"] = Document,
            ["certificate"] = Document, ["contract"] = Document, ["agreement"] = Document,
            ["report"] = Document, ["ticket"] = Document, ["prescription"] = Document,
            ["invoice"] = Document, ["receipt"] = Document, ["bill"] = Document,
            ["permit"] = Document, ["policy"] = Document, ["catalogue"] = Document,
            ["catalog"] = Document, ["list"] = Document, ["menu"] = Document,

            // --- Transaction: value changing hands ---
            ["payment"] = Transaction, ["transaction"] = Transaction, ["tender"] = Transaction,
            ["loan"] = Transaction, ["sale"] = Transaction, ["purchase"] = Transaction,
            ["deposit"] = Transaction, ["withdrawal"] = Transaction, ["transfer"] = Transaction,
            ["refund"] = Transaction, ["order"] = Transaction, ["shipment"] = Transaction,
            ["delivery"] = Transaction, ["subscription"] = Transaction,
            ["rental"] = Transaction,

            // --- Event: something scheduled, between two times ---
            ["booking"] = Event, ["reservation"] = Event, ["appointment"] = Event,
            ["flight"] = Event, ["trip"] = Event, ["journey"] = Event, ["tour"] = Event,
            ["visit"] = Event, ["admission"] = Event, ["stay"] = Event,
            ["treatment"] = Event, ["procedure"] = Event, ["operation"] = Event,
            ["surgery"] = Event, ["session"] = Event, ["course"] = Event,
            ["lesson"] = Event, ["meeting"] = Event, ["conference"] = Event,
            ["event"] = Event, ["ceremony"] = Event, ["examination"] = Event,
            ["shift"] = Event,

            ["money"] = ([Money("Amount"), Text("Currency")], EntityRole.None),
            ["currency"] = ([Money("Amount"), Text("Currency")], EntityRole.None),
        };

    // Deliberately omitted from both Archetypes and AbstractHeads: unit (no standard shape
    // across domains), service (intangible, lacks inventory meaning), trade (equally likely
    // to mean occupation/craft as act of exchange), and asset (usually financial/intangible,
    // not a physical good). Omission means "we have no opinion, offer it to the user".

    /// <summary>
    /// Heads that name an abstraction rather than a thing. These are the drop signal that
    /// replaces the curated allow-list: System resolves to "group", Data to "collection",
    /// Business-the-activity to "practice".
    /// </summary>
    private static readonly HashSet<string> AbstractHeads =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // The original set: what System, Data, Management and Business-as-practice resolve to.
            "group", "collection", "set", "practice", "concept", "idea", "field",
            "discipline", "study", "science", "process", "method", "technique",
            "term", "activity", "administration", "period", "state", "condition",
            "branch", "area", "form", "way",

            // Measured additions. "specialty" is the important one: Radiology, Geriatric,
            // Psychiatric and Intensive care are all defined as a specialty, and a specialty
            // is not something a hospital keeps rows about.
            "specialty", "speciality", "profession", "education", "programme", "program",
            "provision", "totality", "risk", "possibility", "chance", "phase", "stage",
            "experience", "competition", "performance", "approach", "style", "quality",
            "amount", "value", "measure", "degree", "extent", "aspect", "characteristic",
            "feature", "principle", "theory", "notion", "phenomenon", "ability",
            "capacity", "effort", "attempt", "occurrence", "situation", "relationship",
            "maintenance", "movement", "workflow", "arrangement", "phrase", "word"
        };

    /// <summary>
    /// Words people use for the software they are asking for — "a library management SYSTEM", "a
    /// kanban board APP" — rather than for anything it keeps rows about.
    /// </summary>
    /// <remarks>
    /// Consulted for a candidate's NAME only, never for a definition's head: "an integrated library
    /// system is an enterprise resource planning system" defines a perfectly good topic, and
    /// treating the head "system" as an abstraction would refuse it. On the Wikipedia path these
    /// names are already refused by their own articles (System is "a group", Management "the
    /// administration"); the built-in domains have no articles, so the name is all there is.
    /// </remarks>
    private static readonly HashSet<string> SoftwareNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "app", "application", "platform", "software", "website", "portal",
        "management", "solution",
    };

    /// <summary>
    /// Whether a candidate is an abstraction by its name alone: the verdict
    /// <see cref="EntityShaper"/> falls back to when a candidate has no definition to read
    /// (<see cref="Resolve"/> on the name), or a word for the software itself.
    /// </summary>
    public static bool IsAbstractName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return SoftwareNames.Contains(Singularize(name)) || Resolve(name).Verdict == ShapeVerdict.Abstract;
    }

    /// <summary>
    /// Resolves a head noun to its verdict and shape, without throwing or guessing. A head the table
    /// has not seen returns <see cref="ShapeVerdict.Unknown"/>, not <see cref="ShapeVerdict.Abstract"/>—the
    /// distinction matters because <see cref="ShapeVerdict.Abstract"/> deletes the candidate while
    /// <see cref="ShapeVerdict.Unknown"/> offers it to the user for review. Blank input is
    /// <see cref="ShapeVerdict.Unknown"/> for the same reason.
    /// </summary>
    /// <remarks>
    /// The table is keyed by the singular concept ("machine"), but a caller must not have to
    /// know that: <see cref="HypernymExtractor.Extract"/> already hands this a singularized
    /// head, and this folds a plural to the same form again regardless, so the contract holds
    /// for any caller — not only one that happens to normalize first.
    /// </remarks>
    public static ArchetypeResult Resolve(string hypernymHead)
    {
        if (string.IsNullOrWhiteSpace(hypernymHead))
            return new ArchetypeResult(ShapeVerdict.Unknown, Array.Empty<FieldShape>(), EntityRole.None);

        var head = Singularize(hypernymHead);

        if (Archetypes.TryGetValue(head, out var archetype))
            return new ArchetypeResult(ShapeVerdict.Shaped, archetype.Fields, archetype.Role);

        if (AbstractHeads.Contains(head))
            return new ArchetypeResult(ShapeVerdict.Abstract, Array.Empty<FieldShape>(), EntityRole.None);

        return new ArchetypeResult(ShapeVerdict.Unknown, Array.Empty<FieldShape>(), EntityRole.None);
    }

    /// <summary>
    /// Folds a plural head onto its singular form before the table lookup, reusing
    /// <see cref="NameNormalizer.ToEntityName"/> rather than a second plural-stripper. It is
    /// deliberately conservative — "business", "status" and "analysis" all end in a pattern
    /// that looks pluralizable and are left alone — so a corrupted key never silently loses an
    /// archetype the table does have an opinion on.
    /// </summary>
    private static string Singularize(string head)
    {
        var entityName = NameNormalizer.ToEntityName(head);
        return entityName.Length == 0 ? head : entityName.ToLowerInvariant();
    }
}

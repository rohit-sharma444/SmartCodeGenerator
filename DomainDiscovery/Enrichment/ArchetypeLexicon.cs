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

    private static readonly Dictionary<string, (FieldShape[] Fields, EntityRole Role)> Archetypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["device"] = ([Text("Name"), Text("Model"), Text("SerialNumber")], EntityRole.Device),
            ["machine"] = ([Text("Name"), Text("Model"), Text("SerialNumber")], EntityRole.Device),
            ["terminal"] = ([Text("Name"), Text("Model"), Text("SerialNumber")], EntityRole.Device),
            ["equipment"] = ([Text("Name"), Text("Model"), Text("SerialNumber")], EntityRole.Device),
            ["display"] = ([Text("Name"), Text("Model"), Text("SerialNumber")], EntityRole.Device),

            ["document"] = ([Text("Number"), When("IssuedOn"), Money("Total")], EntityRole.Document),
            ["record"] = ([Text("Number"), When("IssuedOn"), Money("Total")], EntityRole.Document),
            ["statement"] = ([Text("Number"), When("IssuedOn"), Money("Total")], EntityRole.Document),

            ["person"] = ([Text("FullName"), Text("Email"), Text("Phone")], EntityRole.Party),
            ["recipient"] = ([Text("FullName"), Text("Email"), Text("Phone")], EntityRole.Party),
            ["buyer"] = ([Text("FullName"), Text("Email"), Text("Phone")], EntityRole.Party),
            ["customer"] = ([Text("FullName"), Text("Email"), Text("Phone")], EntityRole.Party),

            ["business"] = ([Text("Name"), Text("Address")], EntityRole.Place),
            ["organization"] = ([Text("Name"), Text("Address")], EntityRole.Place),
            ["organisation"] = ([Text("Name"), Text("Address")], EntityRole.Place),
            ["establishment"] = ([Text("Name"), Text("Address")], EntityRole.Place),
            ["company"] = ([Text("Name"), Text("Address")], EntityRole.Place),
            ["shop"] = ([Text("Name"), Text("Address")], EntityRole.Place),
            ["store"] = ([Text("Name"), Text("Address")], EntityRole.Place),

            ["item"] = ([Text("Name"), Text("Sku"), Money("Price")], EntityRole.Item),
            ["product"] = ([Text("Name"), Text("Sku"), Money("Price")], EntityRole.Item),
            ["good"] = ([Text("Name"), Text("Sku"), Money("Price")], EntityRole.Item),
            ["goods"] = ([Text("Name"), Text("Sku"), Money("Price")], EntityRole.Item),
            ["merchandise"] = ([Text("Name"), Text("Sku"), Money("Price")], EntityRole.Item),

            ["payment"] = ([Money("Amount"), When("OccurredOn"), Text("Method")], EntityRole.Transaction),
            ["transaction"] = ([Money("Amount"), When("OccurredOn"), Text("Method")], EntityRole.Transaction),
            ["tender"] = ([Money("Amount"), When("OccurredOn"), Text("Method")], EntityRole.Transaction),

            ["card"] = ([Text("Number"), Text("Holder"), When("ExpiresOn")], EntityRole.Item),

            ["money"] = ([Money("Amount"), Text("Currency")], EntityRole.None),
            ["currency"] = ([Money("Amount"), Text("Currency")], EntityRole.None),
        };

    /// <summary>
    /// Heads that name an abstraction rather than a thing. These are the drop signal that
    /// replaces the curated allow-list: System resolves to "group", Data to "collection",
    /// Business-the-activity to "practice".
    /// </summary>
    private static readonly HashSet<string> AbstractHeads =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "group", "collection", "set", "practice", "concept", "idea", "field",
            "discipline", "study", "science", "process", "method", "technique",
            "term", "activity", "administration", "period", "state", "condition",
            "branch", "area", "form", "way"
        };

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

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

public readonly record struct ArchetypeResult(
    ShapeVerdict Verdict, IReadOnlyList<FieldShape> Fields);

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

    private static readonly Dictionary<string, FieldShape[]> Archetypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["device"] = [Text("Name"), Text("Model"), Text("SerialNumber")],
            ["machine"] = [Text("Name"), Text("Model"), Text("SerialNumber")],
            ["terminal"] = [Text("Name"), Text("Model"), Text("SerialNumber")],
            ["equipment"] = [Text("Name"), Text("Model"), Text("SerialNumber")],

            ["document"] = [Text("Number"), When("IssuedOn"), Money("Total")],
            ["record"] = [Text("Number"), When("IssuedOn"), Money("Total")],
            ["statement"] = [Text("Number"), When("IssuedOn"), Money("Total")],

            ["person"] = [Text("FullName"), Text("Email"), Text("Phone")],
            ["recipient"] = [Text("FullName"), Text("Email"), Text("Phone")],
            ["buyer"] = [Text("FullName"), Text("Email"), Text("Phone")],
            ["customer"] = [Text("FullName"), Text("Email"), Text("Phone")],

            ["business"] = [Text("Name"), Text("Address")],
            ["organization"] = [Text("Name"), Text("Address")],
            ["organisation"] = [Text("Name"), Text("Address")],
            ["establishment"] = [Text("Name"), Text("Address")],
            ["company"] = [Text("Name"), Text("Address")],
            ["shop"] = [Text("Name"), Text("Address")],
            ["store"] = [Text("Name"), Text("Address")],

            ["item"] = [Text("Name"), Text("Sku"), Money("Price")],
            ["product"] = [Text("Name"), Text("Sku"), Money("Price")],
            ["good"] = [Text("Name"), Text("Sku"), Money("Price")],
            ["goods"] = [Text("Name"), Text("Sku"), Money("Price")],
            ["merchandise"] = [Text("Name"), Text("Sku"), Money("Price")],

            ["payment"] = [Money("Amount"), When("OccurredOn"), Text("Method")],
            ["transaction"] = [Money("Amount"), When("OccurredOn"), Text("Method")],
            ["tender"] = [Money("Amount"), When("OccurredOn"), Text("Method")],

            ["card"] = [Text("Number"), Text("Holder"), When("ExpiresOn")],
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

    public static ArchetypeResult Resolve(string hypernymHead)
    {
        if (string.IsNullOrWhiteSpace(hypernymHead))
            return new ArchetypeResult(ShapeVerdict.Unknown, Array.Empty<FieldShape>());

        if (Archetypes.TryGetValue(hypernymHead, out var fields))
            return new ArchetypeResult(ShapeVerdict.Shaped, fields);

        if (AbstractHeads.Contains(hypernymHead))
            return new ArchetypeResult(ShapeVerdict.Abstract, Array.Empty<FieldShape>());

        return new ArchetypeResult(ShapeVerdict.Unknown, Array.Empty<FieldShape>());
    }
}

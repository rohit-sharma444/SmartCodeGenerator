using FieldShape = DomainDiscovery.Enrichment.DomainFieldLexicon.FieldShape;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// The baseline properties every class of a given <see cref="EntityRole"/> carries.
/// </summary>
/// <remarks>
/// <see cref="ArchetypeLexicon"/> used to answer two questions at once: what KIND of thing a
/// candidate is, and what fields it therefore has. It answered the first well and the second
/// stingily — three fields, the same three for every device in the world.
/// <para>
/// So the two are split. ArchetypeLexicon keeps the question it is good at (the role, and the
/// Shaped/Abstract/Unknown verdict); this table answers the other one, more generously. It is
/// deliberately generic: what makes a class specific is <see cref="PropertyMiner"/>, which reads
/// the class's own article. This is only the floor beneath that.
/// </para>
/// </remarks>
public static class RoleTemplate
{
    private static FieldShape Text(string name) => new(name, "string");
    private static FieldShape Money(string name) => new(name, "decimal");
    private static FieldShape When(string name) => new(name, "DateTime");
    private static FieldShape Flag(string name) => new(name, "bool");

    private static readonly Dictionary<EntityRole, FieldShape[]> Templates = new()
    {
        [EntityRole.Party] =
            [Text("FullName"), Text("Email"), Text("Phone"), Text("Address"), Flag("Active")],

        [EntityRole.Place] =
            [Text("Name"), Text("Address"), Text("City"), Text("PostCode"), Text("Phone")],

        [EntityRole.Device] =
            [Text("Name"), Text("Model"), Text("SerialNumber"),
             Text("Manufacturer"), When("InstalledOn")],

        [EntityRole.Item] =
            [Text("Name"), Text("Sku"), Text("Description"), Money("Price"), Text("Unit")],

        [EntityRole.Document] =
            [Text("Number"), When("IssuedOn"), Text("Status"),
             Money("Total"), Text("Currency"), Text("Notes")],

        [EntityRole.Transaction] =
            [Money("Amount"), Text("Currency"), When("OccurredOn"),
             Text("Method"), Text("Reference"), Text("Status")],

        [EntityRole.Event] =
            [Text("Reference"), When("StartsOn"), When("EndsOn"), Text("Status")],

        // Not a kind of thing another class references, and so not a kind of thing with a shape.
        [EntityRole.None] = [],
    };

    /// <summary>The baseline fields of <paramref name="role"/>, or empty for a role with no shape.</summary>
    public static IReadOnlyList<FieldShape> For(EntityRole role)
        => Templates.TryGetValue(role, out var fields) ? fields : Array.Empty<FieldShape>();
}

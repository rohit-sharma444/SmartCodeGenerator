using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// What a relationship between two classes is made of, in one place.
/// </summary>
/// <remarks>
/// This was spelled out inside <c>DomainModelMapper.AddNavigation</c>, which only the
/// <c>build app</c> path could reach. Relationships set by hand have to produce the same triple —
/// <c>&lt;Target&gt;Id</c>, a forward reference, an inverse — or <c>ForeignKeys.TargetsOf</c>
/// cannot see them and neither can the seeder, the generated validators, the console picker or the
/// screen split. Two spellings of that triple is how the two paths come to disagree.
/// </remarks>
public static class RelationshipConvention
{
    public const string JsonSerializationNamespace = "System.Text.Json.Serialization";
    public const string ComponentModelNamespace = "System.ComponentModel";

    private const string JsonIgnoreAttribute = "JsonIgnore";
    private const string NotBrowsableAttribute = "Browsable(false)";

    /// <summary>The foreign key field name for a target class.</summary>
    public static string ForeignKeyName(string target) => target + "Id";

    /// <summary>The foreign key. Plain <c>int</c> and no attributes: it is data the user edits,
    /// and <c>ForeignKeys.TargetsOf</c> rejects any other type.</summary>
    public static PropertyDefinition ForeignKey(string target) =>
        new() { Name = ForeignKeyName(target), Type = "int" };

    /// <summary>The reference from the foreign-key owner to its target.</summary>
    public static PropertyDefinition Forward(string target) => Navigation(target, target);

    /// <summary>The collection on the target of a one-to-many.</summary>
    public static PropertyDefinition InverseCollection(string owner) =>
        Navigation(Pluralize(owner), $"List<{owner}>");

    /// <summary>The single reference on the target of a one-to-one.</summary>
    public static PropertyDefinition InverseReference(string owner) => Navigation(owner, owner);

    /// <summary>
    /// Whether <paramref name="propertyName"/> can be added to <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// Two refusals, both from <c>AddNavigation</c>: a member may not share its enclosing type's
    /// name (CS0542), and a name already present must not be written twice.
    /// </remarks>
    public static bool CanAdd(ClassDefinition target, string propertyName)
    {
        if (string.Equals(propertyName, target.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        return !target.Properties.Any(
            p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// English plural, well enough for a collection property name. Not a linguistics engine: the
    /// worst case is an inverse collection called "Companys", which compiles and reads oddly.
    /// </summary>
    public static string Pluralize(string name)
    {
        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
            return name + "es";

        if (name.Length > 1
            && name.EndsWith("y", StringComparison.OrdinalIgnoreCase)
            && !"aeiou".Contains(char.ToLowerInvariant(name[^2])))
            return name[..^1] + "ies";

        return name + "s";
    }

    private static PropertyDefinition Navigation(string name, string type) =>
        new()
        {
            Name = name,
            Type = type,
            Attributes = { JsonIgnoreAttribute, NotBrowsableAttribute }
        };
}

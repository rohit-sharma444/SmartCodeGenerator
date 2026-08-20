using CodeGenerationEngine.Models;

namespace AppScaffolding.Screens;

/// <summary>
/// Decides which discovered entities are masters and which are transactions.
/// </summary>
/// <remarks>
/// A pure function: no I/O, no UI, no dependency on a target. Everything it needs is the class
/// definitions and, later, the user's overrides.
/// </remarks>
public static class EntityRoles
{
    /// <summary>
    /// The date fields <c>DomainModelMapper.AddAuditFields</c> puts on EVERY discovered class.
    /// </summary>
    /// <remarks>
    /// The single most important line in this file. Because every class has these, "has a
    /// <c>DateTime</c> field" is true of everything and carries no information — leave them in and
    /// the entire model classifies as transactions, silently, with nothing in the emitted diff to
    /// show for it.
    /// </remarks>
    public static IReadOnlyList<string> AuditFields { get; } = ["CreatedOn", "ModifiedOn"];

    /// <summary>The role of every class in <paramref name="classes"/>.</summary>
    public static EntityRoleMap Classify(IReadOnlyList<ClassDefinition> classes)
    {
        var roles = new Dictionary<string, EntityRole>(StringComparer.OrdinalIgnoreCase);
        var names = ForeignKeys.ClassNameSet(classes);

        foreach (var definition in classes)
        {
            roles[definition.Name] = IsTransaction(definition, names)
                ? EntityRole.Transaction
                : EntityRole.Master;
        }

        return new EntityRoleMap(
            roles, new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Both halves are required: an outgoing foreign key, AND either a date that is not an audit
    /// field or a name the gazetteer recognises.
    /// </summary>
    /// <remarks>
    /// The foreign key alone would make every child entity a transaction — an Address belonging to
    /// a Customer is not one. The other half alone would make an Employee with a HireDate one.
    /// </remarks>
    private static bool IsTransaction(ClassDefinition definition, HashSet<string> allNames)
    {
        if (ForeignKeys.TargetsOf(definition, allNames).Count == 0)
            return false;

        return HasNonAuditDate(definition) || TransactionWords.Contains(definition.Name);
    }

    private static bool HasNonAuditDate(ClassDefinition definition)
        => definition.Properties.Any(p =>
            string.Equals(p.Type, "DateTime", StringComparison.Ordinal)
            && !AuditFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
}

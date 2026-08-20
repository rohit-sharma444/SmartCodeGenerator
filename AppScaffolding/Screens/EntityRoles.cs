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

    /// <summary>
    /// What a line entity's name ends in, or is named after its parent plus.
    /// </summary>
    /// <remarks>
    /// A strict NAME test, deliberately, not a shape test. A line gets no screen of its own,
    /// so a false positive hides one — and a hidden screen is far harder to notice than an
    /// extra one.
    /// </remarks>
    public static IReadOnlyList<string> LineSuffixes { get; } = ["Line", "Item", "Detail", "Row"];

    /// <summary>The role of every class in <paramref name="classes"/>.</summary>
    /// <remarks>
    /// Two passes, because lines are defined in terms of transactions: nothing can be
    /// recognised as a line until its parent is known to be one.
    /// </remarks>
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

        var lines = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in classes)
        {
            var parent = LineParentOf(definition, names, roles);

            if (parent is null)
                continue;

            roles[definition.Name] = EntityRole.DocumentLine;

            var existing = lines.TryGetValue(parent, out var already) ? already.ToList() : [];

            existing.Add(definition.Name);
            lines[parent] = existing;
        }

        return new EntityRoleMap(roles, lines);
    }

    /// <summary>
    /// The transaction <paramref name="definition"/> is the lines of, or null when it is not
    /// lines at all.
    /// </summary>
    /// <remarks>
    /// Both conditions are required: a foreign key to something already classed as a
    /// Transaction, and a name that says "lines". A child of a MASTER called CustomerLine is
    /// not lines, because there is no document to edit it inside.
    /// </remarks>
    private static string? LineParentOf(
        ClassDefinition definition,
        HashSet<string> allNames,
        IReadOnlyDictionary<string, EntityRole> roles)
    {
        if (!LooksLikeLines(definition.Name))
            return null;

        foreach (var target in ForeignKeys.TargetsOf(definition, allNames))
        {
            if (roles.TryGetValue(target, out var role) && role == EntityRole.Transaction)
                return target;
        }

        return null;
    }

    private static bool LooksLikeLines(string name)
        => LineSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

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

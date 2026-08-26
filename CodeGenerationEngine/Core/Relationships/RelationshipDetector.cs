namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// Works out which relationships a set of classes is missing.
/// </summary>
/// <remarks>
/// Pure: classes in, proposals out, nothing read from disk and nothing written. Three signals,
/// and each one is checked against <see cref="RelationshipConvention.CanAdd"/> before it becomes a
/// proposal — a proposal that cannot be written is worse than no proposal, because the user
/// accepts it and nothing happens.
/// </remarks>
public static class RelationshipDetector
{
    public static IReadOnlyList<RelationshipProposal> Detect(
        IReadOnlyList<ClassSnapshot> classes)
    {
        var byName = new Dictionary<string, ClassSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in classes)
            byName[snapshot.Name] = snapshot;

        var proposals = new List<RelationshipProposal>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var owner in classes)
        {
            foreach (var target in OrphanForeignKeys(owner, byName))
                Add(owner, target,
                    $"because {owner.Name}.{RelationshipConvention.ForeignKeyName(target.Name)} "
                    + $"is an int and {target.Name} exists");

            foreach (var target in OrphanNavigations(owner, byName))
                Add(owner, target,
                    $"because {owner.Name}.{target.Name} is typed as {target.Name} "
                    + "and has no foreign key beside it");

            if (NamedAfterParent(owner, byName) is { } parent)
                Add(owner, parent,
                    $"because {owner.Name} is named after {parent.Name}, which exists");
        }

        return proposals;

        void Add(ClassSnapshot owner, ClassSnapshot target, string reason)
        {
            var proposal = new RelationshipProposal(
                owner.Name, target.Name, RelationshipKind.OneToMany, reason);

            if (!Writable(owner, target) || !seen.Add(proposal.Key))
                return;

            proposals.Add(proposal);
        }
    }

    /// <summary>An <c>int &lt;Target&gt;Id</c> whose target exists and which has no navigation
    /// property beside it. Exactly <c>int</c>: <c>ForeignKeys.TargetsOf</c> rejects anything
    /// else, so a nullable one is not a foreign key and must not be finished as though it were.
    /// </summary>
    private static IEnumerable<ClassSnapshot> OrphanForeignKeys(
        ClassSnapshot owner, Dictionary<string, ClassSnapshot> byName)
    {
        foreach (var property in owner.Properties)
        {
            if (property.Type != "int" || property.Name.Length <= 2)
                continue;

            if (!property.Name.EndsWith("Id", StringComparison.Ordinal)
                || string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetName = property.Name[..^2];

            if (string.Equals(targetName, owner.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!byName.TryGetValue(targetName, out var target))
                continue;

            if (HasNavigationTo(owner, target.Name))
                continue;

            yield return target;
        }
    }

    /// <summary>A property typed as another class, with no <c>&lt;Target&gt;Id</c> beside it.</summary>
    private static IEnumerable<ClassSnapshot> OrphanNavigations(
        ClassSnapshot owner, Dictionary<string, ClassSnapshot> byName)
    {
        foreach (var property in owner.Properties)
        {
            if (!byName.TryGetValue(property.Type, out var target))
                continue;

            if (string.Equals(target.Name, owner.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (HasForeignKeyTo(owner, target.Name))
                continue;

            yield return target;
        }
    }

    /// <summary>A class whose name begins with another class's name, and which is not already
    /// related to it. The one rule that invents a foreign key rather than completing one.</summary>
    private static ClassSnapshot? NamedAfterParent(
        ClassSnapshot owner, Dictionary<string, ClassSnapshot> byName)
    {
        ClassSnapshot? best = null;

        foreach (var candidate in byName.Values)
        {
            if (string.Equals(candidate.Name, owner.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!owner.Name.StartsWith(candidate.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (HasForeignKeyTo(owner, candidate.Name) || HasNavigationTo(owner, candidate.Name))
                continue;

            // The longest matching prefix: `SalesOrderLine` belongs to `SalesOrder`, not `Sales`.
            if (best is null || candidate.Name.Length > best.Name.Length)
                best = candidate;
        }

        return best;
    }

    private static bool HasNavigationTo(ClassSnapshot owner, string targetName) =>
        owner.Properties.Any(p => string.Equals(p.Type, targetName, StringComparison.Ordinal));

    private static bool HasForeignKeyTo(ClassSnapshot owner, string targetName) =>
        owner.Properties.Any(p =>
            p.Type == "int"
            && string.Equals(
                p.Name, RelationshipConvention.ForeignKeyName(targetName),
                StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether both sides can actually take the properties the convention would add.</summary>
    private static bool Writable(ClassSnapshot owner, ClassSnapshot target)
    {
        var ownerDefinition = owner.ToDefinition();
        var targetDefinition = target.ToDefinition();

        var forward = RelationshipConvention.Forward(target.Name);
        var inverse = RelationshipConvention.InverseCollection(owner.Name);

        var forwardOk = RelationshipConvention.CanAdd(ownerDefinition, forward.Name)
                        || HasNavigationTo(owner, target.Name);

        // The foreign key needs its own arm, and it is not the same shape as the forward one.
        // CanAdd refuses a name that is already present whatever its type, and HasForeignKeyTo
        // accepts it only when it is an int - so a class holding a `string CustomerId` fails both
        // and is not proposed. Without this, the pair looks writable, the writer skips the
        // property because the NAME is taken, everything compiles, and the user is told the two
        // are related while ForeignKeys.TargetsOf still sees nothing.
        var foreignKeyOk =
            RelationshipConvention.CanAdd(
                ownerDefinition, RelationshipConvention.ForeignKeyName(target.Name))
            || HasForeignKeyTo(owner, target.Name);

        return forwardOk
               && foreignKeyOk
               && RelationshipConvention.CanAdd(targetDefinition, inverse.Name);
    }
}

using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// One definition of what counts as a foreign key in a generated app, shared by the seeder (which
/// needs it for dependency ordering and runtime FK assignment) and the generated validators (which
/// check that a non-zero FK points at a real row).
/// </summary>
/// <remarks>
/// Extracted from SeedDataTemplate so the two callers cannot drift. The generated console picker
/// applies the same rule at runtime by reflection (see ConsoleMenuTemplate.TryPickForeignKey), so
/// changing this definition means changing that too.
/// </remarks>
public static class ForeignKeys
{
    /// <summary>The class names in <paramref name="classes"/>, for use as the lookup set.</summary>
    public static HashSet<string> ClassNameSet(IReadOnlyList<ClassDefinition> classes) =>
        new(classes.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One foreign key a class holds: the <c>int</c> property holding it, the navigation property
    /// beside it, and the class it points at.
    /// </summary>
    public sealed record Link(string Property, string Navigation, string Target)
    {
        /// <summary>Whether the key is spelled for its target (<c>CustomerId</c> beside
        /// <c>Customer Customer</c>), rather than for a role (<c>PaidById</c> beside
        /// <c>Staff PaidBy</c>).</summary>
        public bool IsConventional
            => string.Equals(Property, Target + "Id", StringComparison.Ordinal)
               && string.Equals(Navigation, Target, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every foreign key <paramref name="c"/> holds: an <c>int &lt;Nav&gt;Id</c> whose sibling
    /// <c>&lt;Nav&gt;</c> is typed as one of the classes in <paramref name="allNames"/>.
    /// </summary>
    /// <remarks>
    /// The same rule the generated app applies at run time by reflection (the console picker,
    /// the grids' lookup columns, the form's lookups and the relationship resolver all pair a key
    /// with the navigation named for it), so a key named for a role (<c>PaidById</c> beside
    /// <c>Staff PaidBy</c>, <c>ManagerId</c> beside <c>Staff Manager</c> on Staff itself) is one
    /// here too, with its exists check and its delete refusal. A key spelled
    /// <c>&lt;Target&gt;Id</c> with a navigation of that type under another name still counts, as
    /// it always has.
    /// </remarks>
    public static IReadOnlyList<Link> LinksOf(ClassDefinition c, HashSet<string> allNames)
    {
        var links = new List<Link>();
        foreach (var p in c.Properties)
        {
            if (p.Type != "int" || p.Name.Length <= 2)
                continue;
            if (!p.Name.EndsWith("Id", StringComparison.Ordinal)
                || string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
                continue;

            var navName = p.Name[..^2];

            var nav = c.Properties.FirstOrDefault(n => string.Equals(n.Name, navName, StringComparison.Ordinal));
            if (nav is not null && allNames.Contains(nav.Type))
            {
                links.Add(new Link(p.Name, nav.Name, nav.Type));
                continue;
            }

            if (allNames.Contains(navName)
                && !string.Equals(navName, c.Name, StringComparison.OrdinalIgnoreCase)
                && c.Properties.FirstOrDefault(n => string.Equals(n.Type, navName, StringComparison.Ordinal))
                    is { } typed)
                links.Add(new Link(p.Name, typed.Name, navName));
        }
        return links;
    }

    /// <summary>The class names this class references via a <c>&lt;Target&gt;Id</c> foreign key
    /// whose sibling nav property is typed as that target entity.</summary>
    /// <remarks>
    /// Only the keys spelled for their target, and never the class itself: the screen split and
    /// the line detection read a document's parent from <c>&lt;Parent&gt;Id</c>. Everything that
    /// must see every key - the validators, the delete refusal, the seeder - reads
    /// <see cref="LinksOf"/>.
    /// </remarks>
    public static IReadOnlyList<string> TargetsOf(ClassDefinition c, HashSet<string> allNames)
        => LinksOf(c, allNames)
            .Where(l => string.Equals(l.Property, l.Target + "Id", StringComparison.Ordinal)
                        && !string.Equals(l.Target, c.Name, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Target)
            .ToList();

    /// <summary>
    /// Every (class, property) that points at <paramref name="target"/> through a foreign key.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="LinksOf"/>, and deliberately built ON it rather than beside it:
    /// a delete refused for a reference the seeder does not consider a foreign key would refuse a
    /// delete no other part of the app can explain. Every key counts, a role's (a payment's
    /// <c>ApprovedById</c>) and the class's own (a manager's staff) included.
    /// </remarks>
    public static IReadOnlyList<(string Entity, string Property)> ReferencesTo(
        string target, IReadOnlyList<ClassDefinition> classes)
    {
        var names = ClassNameSet(classes);
        var referencing = new List<(string Entity, string Property)>();

        foreach (var c in classes)
            foreach (var link in LinksOf(c, names))
                if (string.Equals(link.Target, target, StringComparison.OrdinalIgnoreCase))
                    referencing.Add((c.Name, link.Property));

        return referencing;
    }

    /// <summary>The field names that are genuine foreign keys across <paramref name="classes"/> —
    /// a <c>&lt;Nav&gt;Id</c> whose owner also has a nav property typed as an entity.</summary>
    public static IReadOnlyList<string> FieldNames(IReadOnlyList<ClassDefinition> classes)
    {
        var names = ClassNameSet(classes);
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in classes)
            foreach (var link in LinksOf(c, names))
            {
                if (seen.Add(link.Property))
                    fields.Add(link.Property);
            }
        return fields;
    }
}

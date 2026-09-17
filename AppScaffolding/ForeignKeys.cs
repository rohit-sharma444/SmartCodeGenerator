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

    /// <summary>The class names this class references via a <c>&lt;Target&gt;Id</c> foreign key
    /// whose sibling nav property is typed as that target entity.</summary>
    public static IReadOnlyList<string> TargetsOf(ClassDefinition c, HashSet<string> allNames)
    {
        var targets = new List<string>();
        foreach (var p in c.Properties)
        {
            if (p.Type != "int" || p.Name.Length <= 2)
                continue;
            if (!p.Name.EndsWith("Id", StringComparison.Ordinal)
                || string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
                continue;

            var target = p.Name[..^2];
            if (allNames.Contains(target)
                && !string.Equals(target, c.Name, StringComparison.OrdinalIgnoreCase)
                && c.Properties.Any(n => string.Equals(n.Type, target, StringComparison.Ordinal)))
                targets.Add(target);
        }
        return targets;
    }

    /// <summary>
    /// Every (class, property) that points at <paramref name="target"/> through a foreign key.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="TargetsOf"/>, and deliberately built ON it rather than beside it:
    /// a delete refused for a reference the seeder does not consider a foreign key would refuse a
    /// delete no other part of the app can explain.
    /// </remarks>
    public static IReadOnlyList<(string Entity, string Property)> ReferencesTo(
        string target, IReadOnlyList<ClassDefinition> classes)
    {
        var names = ClassNameSet(classes);
        var referencing = new List<(string Entity, string Property)>();

        foreach (var c in classes)
            foreach (var t in TargetsOf(c, names))
                if (string.Equals(t, target, StringComparison.OrdinalIgnoreCase))
                    referencing.Add((c.Name, t + "Id"));

        return referencing;
    }

    /// <summary>The field names that are genuine foreign keys across <paramref name="classes"/> —
    /// a <c>&lt;Target&gt;Id</c> whose owner also has a nav property typed as that target.</summary>
    public static IReadOnlyList<string> FieldNames(IReadOnlyList<ClassDefinition> classes)
    {
        var names = ClassNameSet(classes);
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in classes)
            foreach (var target in TargetsOf(c, names))
            {
                var field = target + "Id";
                if (seen.Add(field))
                    fields.Add(field);
            }
        return fields;
    }
}

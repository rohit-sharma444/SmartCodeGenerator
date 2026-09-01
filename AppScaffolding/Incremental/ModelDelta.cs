using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace AppScaffolding.Incremental;

/// <summary>What a proposed class turns out to be, against what is already on disk.</summary>
public enum ClassFate
{
    /// <summary>No class of this name is there.</summary>
    New,

    /// <summary>There, with the same properties.</summary>
    Unchanged,

    /// <summary>There, with different properties. Adding will not reconcile it.</summary>
    Changed
}

public sealed record ClassDelta(string Name, ClassFate Fate);

/// <summary>
/// Which of a proposed model's classes are already in a generated app.
/// </summary>
/// <remarks>
/// Pure, so it can be asserted without a filesystem or a scaffolder — the same reason
/// <see cref="Screens.EntityRoles"/> and <see cref="Screens.ScreenOverrides"/> are pure.
/// <para>
/// Compared on property NAMES, not types or order. A type change is worth knowing about, but
/// comparing types would have every run of a differently-versioned generator report churn nobody
/// can act on, and this exists to decide what to write rather than to audit.
/// </para>
/// </remarks>
public static class ModelDelta
{
    public static IReadOnlyList<ClassDelta> Between(
        IReadOnlyList<ClassSnapshot> existing,
        IReadOnlyList<ClassDefinition> proposed)
    {
        // Case-insensitive: a class name is also a file name, and Windows file names are.
        var onDisk = new Dictionary<string, ClassSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in existing)
            onDisk[snapshot.Name] = snapshot;

        var delta = new List<ClassDelta>();

        foreach (var definition in proposed)
        {
            if (!onDisk.TryGetValue(definition.Name, out var snapshot))
            {
                delta.Add(new ClassDelta(definition.Name, ClassFate.New));
                continue;
            }

            delta.Add(new ClassDelta(
                definition.Name,
                SameProperties(snapshot, definition) ? ClassFate.Unchanged : ClassFate.Changed));
        }

        // Classes on disk that the model does not have are deliberately absent. Removing is out
        // of scope, and a class the user wrote by hand is not a discrepancy — it is their code.
        return delta;
    }

    /// <summary>The names an add would write, in the order the model has them.</summary>
    public static IReadOnlyList<string> NewNames(IReadOnlyList<ClassDelta> delta)
        => [.. delta.Where(d => d.Fate == ClassFate.New).Select(d => d.Name)];

    private static bool SameProperties(ClassSnapshot snapshot, ClassDefinition definition)
    {
        var there = snapshot.Properties
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var wanted = definition.Properties
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return there.SetEquals(wanted);
    }
}

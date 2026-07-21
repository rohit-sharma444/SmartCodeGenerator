using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// Emits <c>Data/SeedData.cs</c>: a reflection-based seeder that populates every empty store
/// with sample rows on first run. Consistent with JsonStore/Repository, which already reflect
/// on Id. Seed-if-empty and idempotent, so a running app never accrues duplicate sample data.
/// </summary>
public static class SeedDataTemplate
{
    /// <summary>
    /// Entity class names in seed order: an entity referenced by a foreign key is placed before
    /// the entity that references it, so a child's FK can point at a parent row that already
    /// exists. A dependency cycle degrades to declaration order.
    /// </summary>
    public static IReadOnlyList<string> OrderByDependency(IReadOnlyList<ClassDefinition> classes)
    {
        var names = new HashSet<string>(classes.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var parents = classes.ToDictionary(
            c => c.Name, c => ForeignKeyTargets(c, names), StringComparer.OrdinalIgnoreCase);

        var ordered = new List<string>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool progress = true;
        while (progress && placed.Count < classes.Count)
        {
            progress = false;
            foreach (var c in classes)
            {
                if (placed.Contains(c.Name))
                    continue;

                // Placeable once every parent is already placed (a self-reference never blocks).
                if (parents[c.Name].All(p =>
                        placed.Contains(p) || string.Equals(p, c.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    ordered.Add(c.Name);
                    placed.Add(c.Name);
                    progress = true;
                }
            }
        }

        // Any remaining (a cycle) fall back to declaration order.
        foreach (var c in classes)
            if (placed.Add(c.Name))
                ordered.Add(c.Name);

        return ordered;
    }

    /// <summary>The class names this class references via a <c>&lt;Target&gt;Id</c> foreign key
    /// whose sibling nav property is typed as that target entity.</summary>
    private static IReadOnlyList<string> ForeignKeyTargets(ClassDefinition c, HashSet<string> allNames)
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

    /// <summary>Source of <c>Data/SeedData.cs</c> in <paramref name="projectName"/>.Data, seeding
    /// the entities in <paramref name="orderedEntityNames"/> order.</summary>
    public static string Source(string projectName, IReadOnlyList<string> orderedEntityNames)
    {
        var seedCalls = string.Join("\n",
            orderedEntityNames.Select(n => $"        Seed<{n}>(seededIds);"));

        return $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using {{projectName}}.Models;

namespace {{projectName}}.Data;

/// <summary>
/// Populates empty stores with sample rows on first run. Seed-if-empty and idempotent: a store
/// that already holds data is left untouched, so re-running never accrues duplicates. This is a
/// starting point — edit the sample values, or delete this file, as the app grows.
/// </summary>
public static class SeedData
{
    /// <summary>Rows inserted into each empty store.</summary>
    public const int SeedRowCount = 3;

    /// <summary>Seeds every empty store, referenced entities before their referencers so a
    /// foreign key points at a row that exists.</summary>
    public static void EnsureSeeded()
    {
        var seededIds = new Dictionary<string, List<int>>();
{{seedCalls}}
    }

    private static void Seed<T>(Dictionary<string, List<int>> seededIds) where T : class, new()
    {
        var store = new JsonStore<T>();
        var existing = store.GetAll();
        if (existing.Count > 0)
        {
            seededIds[typeof(T).Name] = existing.Select(GetId).ToList();
            return;
        }

        var rows = new List<T>();
        var ids = new List<int>();
        for (var n = 1; n <= SeedRowCount; n++)
        {
            var item = new T();
            foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite)
                    continue;

                if (string.Equals(prop.Name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    prop.SetValue(item, n);
                    continue;
                }

                var value = SampleValue(prop, n, seededIds);
                if (value is not null)
                    prop.SetValue(item, value);
            }

            rows.Add(item);
            ids.Add(n);
        }

        store.SaveAll(rows);
        seededIds[typeof(T).Name] = ids;
    }

    private static object? SampleValue(
        PropertyInfo prop, int n, Dictionary<string, List<int>> seededIds)
    {
        var type = prop.PropertyType;
        var name = prop.Name;

        // Foreign key: an int named <Target>Id -> an existing seeded <Target> row's Id.
        if (type == typeof(int) && name.Length > 2 && name.EndsWith("Id", StringComparison.Ordinal))
        {
            var target = name.Substring(0, name.Length - 2);
            if (seededIds.TryGetValue(target, out var parentIds) && parentIds.Count > 0)
                return parentIds[(n - 1) % parentIds.Count];
            return 0;
        }

        if (type == typeof(string))
        {
            if (name.Contains("Email", StringComparison.OrdinalIgnoreCase))
                return $"user{n}@example.com";
            if (name.Contains("Phone", StringComparison.OrdinalIgnoreCase))
                return $"555-010{n}";
            return $"{name} {n}";
        }

        if (type == typeof(int)) return n;
        if (type == typeof(decimal)) return n * 10 + 0.99m;
        if (type == typeof(double)) return n * 10 + 0.99;
        if (type == typeof(DateTime)) return DateTime.Today.AddDays(-n);
        if (type == typeof(bool)) return n % 2 == 0;

        // Navigation properties (entity types, List<>) and anything else are left at default.
        return null;
    }

    private static int GetId<T>(T item)
    {
        var id = typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return (int)(id?.GetValue(item) ?? 0);
    }
}
""";
    }
}

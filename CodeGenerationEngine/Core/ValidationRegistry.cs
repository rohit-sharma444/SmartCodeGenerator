using System.Linq;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerationEngine.Core;

/// <summary>In-memory store of validation rules per class, keyed case-insensitively.
/// Holds both data and business rules; the command layer filters by category.</summary>
public static class ValidationRegistry
{
    private static readonly Dictionary<string, List<IValidationRule>> _rules =
        new(System.StringComparer.OrdinalIgnoreCase);

    public static void Add(string className, IValidationRule rule)
    {
        if (string.IsNullOrWhiteSpace(className) || rule == null)
            return;

        if (!_rules.TryGetValue(className, out var list))
        {
            list = new List<IValidationRule>();
            _rules[className] = list;
        }

        list.Add(rule);
    }

    public static IReadOnlyList<IValidationRule> Get(string className) =>
        _rules.TryGetValue(className ?? string.Empty, out var list)
            ? list
            : System.Array.Empty<IValidationRule>();

    public static IReadOnlyList<IValidationRule> Get(string className, ValidationCategory category) =>
        Get(className).Where(r => r.Category == category).ToList();

    /// <summary>Removes every rule of <paramref name="category"/> that names
    /// <paramref name="property"/>, and returns them.</summary>
    public static IReadOnlyList<IValidationRule> RemoveOn(
        string className, ValidationCategory category, string property)
    {
        if (!_rules.TryGetValue(className ?? string.Empty, out var list))
            return System.Array.Empty<IValidationRule>();

        var removed = list
            .Where(r => r.Category == category
                && r.Properties.Any(p => p.Equals(property, System.StringComparison.OrdinalIgnoreCase)))
            .ToList();

        list.RemoveAll(removed.Contains);
        return removed;
    }

    /// <summary>Removes the <paramref name="number"/>th rule of <paramref name="category"/>,
    /// counting from 1 in the order <see cref="Get(string, ValidationCategory)"/> returns
    /// them — the numbers the show commands print. Null when there is no such rule.</summary>
    public static IValidationRule? RemoveNumber(string className, ValidationCategory category, int number)
    {
        var rules = Get(className, category);
        if (number < 1 || number > rules.Count)
            return null;

        var rule = rules[number - 1];
        _rules[className].Remove(rule);
        return rule;
    }

    /// <summary>A copy of every class's rules, for undo. Rules are immutable, so copying the lists
    /// is enough.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<IValidationRule>> Snapshot() =>
        _rules.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<IValidationRule>)kv.Value.ToList(),
            System.StringComparer.OrdinalIgnoreCase);

    public static void Restore(IReadOnlyDictionary<string, IReadOnlyList<IValidationRule>> snapshot)
    {
        _rules.Clear();
        foreach (var (className, rules) in snapshot)
            _rules[className] = rules.ToList();
    }

    /// <summary>Replaces one class's rules, leaving every other class's alone.</summary>
    public static void Set(string className, IReadOnlyList<IValidationRule> rules) =>
        _rules[className] = rules.ToList();

    /// <summary>
    /// Moves every rule of <paramref name="oldName"/> to <paramref name="newName"/>, after any it
    /// already has, for a class that was renamed. Returns how many rules moved.
    /// </summary>
    public static int RenameClass(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)
            || !_rules.TryGetValue(oldName, out var moving) || moving.Count == 0)
            return 0;

        // Only the case changed: the key is case-insensitive, so there is nothing to move.
        if (oldName.Equals(newName, System.StringComparison.OrdinalIgnoreCase))
            return moving.Count;

        _rules.Remove(oldName);
        if (_rules.TryGetValue(newName, out var existing))
            existing.AddRange(moving);
        else
            _rules[newName] = moving;

        return moving.Count;
    }

    /// <summary>Drops every rule of a class that was deleted. Returns how many rules were dropped.</summary>
    public static int RemoveClass(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_rules.TryGetValue(name, out var list))
            return 0;

        _rules.Remove(name);
        return list.Count;
    }

    public static void Clear() => _rules.Clear();
}

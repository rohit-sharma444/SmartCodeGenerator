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

    public static void Clear() => _rules.Clear();
}

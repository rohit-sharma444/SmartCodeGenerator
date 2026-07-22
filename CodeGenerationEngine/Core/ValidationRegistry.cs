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

    public static void Clear() => _rules.Clear();
}

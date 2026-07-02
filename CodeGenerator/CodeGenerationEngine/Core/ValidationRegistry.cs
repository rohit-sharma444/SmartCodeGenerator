using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// A registry to store validation rules for properties of classes. It allows adding and retrieving validation rules based on the class name.
/// </summary>
public static class ValidationRegistry
{
    private static readonly Dictionary<string, List<PropertyRule>>
        _rules = new(StringComparer.OrdinalIgnoreCase);

    public static void AddRule(string className, PropertyRule rule)
    {
        if (!_rules.TryGetValue(className, out var list))
        {
            list = new List<PropertyRule>();
            _rules[className] = list;
        }

        list.Add(rule);
    }

    public static List<PropertyRule> GetRules(string className)
    {
        return _rules.TryGetValue(className, out var list)
            ? list
            : new List<PropertyRule>();
    }
}
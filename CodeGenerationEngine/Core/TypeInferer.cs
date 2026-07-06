using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Infers C# property types from names using:
/// 1. User-defined rules
/// 2. Built-in conventions
/// 3. Collection detection
/// 4. Nullable detection
/// </summary>
public static class TypeInferer
{
    private static readonly Dictionary<string, string> BuiltInMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "int",
            ["age"] = "int",
            ["count"] = "int",
            ["quantity"] = "int",
            ["number"] = "int",

            ["price"] = "decimal",
            ["amount"] = "decimal",
            ["cost"] = "decimal",
            ["salary"] = "decimal",
            ["total"] = "decimal",

            ["email"] = "string",
            ["phone"] = "string",
            ["mobile"] = "string",
            ["address"] = "string",
            ["name"] = "string",

            ["birthdate"] = "DateTime",
            ["createddate"] = "DateTime",
            ["modifieddate"] = "DateTime",
            ["createdat"] = "DateTime",
            ["updatedat"] = "DateTime",

            ["isactive"] = "bool",
            ["isenabled"] = "bool",
            ["isdeleted"] = "bool",
            ["haschildren"] = "bool",

            ["guid"] = "Guid"
        };

    public static string InferType(string propertyName, List<TypeInferenceRule> rules)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return "string";

        propertyName = propertyName.Trim();

        // Collection detection
        var collectionType = DetectCollection(propertyName);

        if (collectionType != null)
            return collectionType;

        // User rules first
        var userRuleResult = InferFromRules(propertyName, rules);

        if (!string.IsNullOrWhiteSpace(userRuleResult))
            return userRuleResult;

        // Built-in conventions
        var builtIn = InferBuiltIn(propertyName);

        if (!string.IsNullOrWhiteSpace(builtIn))
            return builtIn;

        // Boolean naming conventions
        if (propertyName.StartsWith("Is", StringComparison.OrdinalIgnoreCase))
            return "bool";

        if (propertyName.StartsWith("Has", StringComparison.OrdinalIgnoreCase))
            return "bool";

        if (propertyName.StartsWith("Can", StringComparison.OrdinalIgnoreCase))
            return "bool";

        return "string";
    }

    private static string? InferFromRules(string propertyName, List<TypeInferenceRule> rules)
    {
        var lower = propertyName.ToLowerInvariant();

        foreach (var rule in rules)
        {
            if (rule.ExactMatches.Any(x => x.Equals(lower,
                StringComparison.OrdinalIgnoreCase)))
                return rule.TypeName;

            if (rule.StartsWith.Any(x => lower.StartsWith(x,
               StringComparison.OrdinalIgnoreCase)))
                return rule.TypeName;

            if (rule.EndsWith.Any(x => lower.EndsWith(x,
                        StringComparison.OrdinalIgnoreCase)))
                return rule.TypeName;

            if (rule.Contains.Any(x => lower.Contains(x,
                        StringComparison.OrdinalIgnoreCase)))
                return rule.TypeName;
        }

        return null;
    }

    private static string? InferBuiltIn(string propertyName)
    {
        var lower = propertyName.ToLowerInvariant();

        foreach (var mapping in BuiltInMappings)
        {
            if (lower == mapping.Key)
                return mapping.Value;

            if (lower.Contains(mapping.Key))
                return mapping.Value;
        }

        return null;
    }

    private static readonly string[] CollectionSuffixes =
    [
        "List",
        "Items",
        "Collection"
    ];
    private static string? DetectCollection(string propertyName)
    {
        if (CollectionSuffixes.Any(s =>
            propertyName.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            return "List<string>";
        }
        return null;
    }

}
using CodeGenerationEngine.Models;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for validating property definitions and runtime values against defined rules.
/// </summary>
public static class PropertyValidationService
{
    // Validate property definitions (inline rules + global rules) for structural issues
    public static List<string> ValidatePropertyDefinitions(IEnumerable<PropertyDefinition> properties, IEnumerable<PropertyRule> globalRules)
    {
        var errors = new List<string>();
        var props = properties?.ToList() ?? new List<PropertyDefinition>();
        var globals = globalRules?.ToList() ?? new List<PropertyRule>();

        foreach (var prop in props)
        {
            // collect applicable rules: global rules matching this property name or wildcard "*"
            var applicable = globals
                .Where(r => string.Equals(r.PropertyName, prop.Name, StringComparison.OrdinalIgnoreCase) || r.PropertyName == "*")
                .ToList();

            // include inline rules defined on the property
            if (prop.Rules != null && prop.Rules.Any())
                applicable.AddRange(prop.Rules);

            // validate property-level constraints (structure, type expectations)
            errors.AddRange(ValidatePropertyDefinition(prop, applicable));
        }

        // check uniqueness constraints from global rules
        foreach (var uniq in globals.Where(r => r.IsUnique))
        {
            var matches = props.Where(p => string.Equals(p.Name, uniq.PropertyName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 1)
                errors.Add($"Property '{uniq.PropertyName}' must be unique but appears {matches.Count} times.");
        }

        return errors;
    }

    // Validate a live instance using PropertyRule list
    public static List<string> ValidateInstance(object instance, IEnumerable<PropertyRule> rules)
    {
        var errors = new List<string>();
        if (instance == null) return errors;

        var ruleList = rules?.ToList() ?? new List<PropertyRule>();
        var type = instance.GetType();

        foreach (var rule in ruleList)
        {
            if (string.IsNullOrWhiteSpace(rule.PropertyName) || rule.PropertyName == "*")
                continue;

            var propInfo = type.GetProperty(rule.PropertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            object? val = propInfo != null ? propInfo.GetValue(instance) : null;

            errors.AddRange(ValidateValue(rule.PropertyName, val, rule));
        }

        return errors;
    }

    // Validate a single PropertyDefinition against rules (structural checks)
    private static IEnumerable<string> ValidatePropertyDefinition(PropertyDefinition prop, IEnumerable<PropertyRule> rules)
    {
        var errors = new List<string>();
        if (prop == null) return errors;

        foreach (var rule in rules ?? Enumerable.Empty<PropertyRule>())
        {
            // Required at structure level means property must be present (we already have it), but we can check type presence
            if (rule.IsRequired && string.IsNullOrWhiteSpace(prop.Name))
                errors.Add($"Property is required but missing a name.");

            if (!string.IsNullOrWhiteSpace(rule.ExpectedType))
            {
                var expected = NormalizeType(rule.ExpectedType);
                var actual = NormalizeType(prop.Type ?? string.Empty);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Property '{prop.Name}' expected type '{expected}' but was '{actual}'.");
            }

            if ((rule.MinLength.HasValue || rule.MaxLength.HasValue) && !IsStringType(prop.Type))
                errors.Add($"Property '{prop.Name}': length constraints apply to string types only.");

            if (rule.IsUnique)
            {
                // uniqueness handled at collection level
            }
        }

        return errors;
    }

    // Validate a runtime value against a single PropertyRule
    private static IEnumerable<string> ValidateValue(string propertyName, object? value, PropertyRule rule)
    {
        var errors = new List<string>();

        // Required
        if (rule.IsRequired)
        {
            if (value == null || string.IsNullOrWhiteSpace(value?.ToString()))
            {
                errors.Add($"{propertyName} is required.");
                // continue to gather other errors
            }
        }

        if (value == null)
            return errors;

        var text = value.ToString() ?? string.Empty;

        // Lengths
        if (rule.MinLength.HasValue && text.Length < rule.MinLength.Value)
            errors.Add($"{propertyName} minimum length is {rule.MinLength.Value}.");

        if (rule.MaxLength.HasValue && text.Length > rule.MaxLength.Value)
            errors.Add($"{propertyName} maximum length is {rule.MaxLength.Value}.");

        // Numeric ranges
        if (rule.MinValue.HasValue || rule.MaxValue.HasValue)
        {
            if (decimal.TryParse(text, out var number))
            {
                if (rule.MinValue.HasValue && number < rule.MinValue.Value)
                    errors.Add($"{propertyName} must be >= {rule.MinValue.Value}.");
                if (rule.MaxValue.HasValue && number > rule.MaxValue.Value)
                    errors.Add($"{propertyName} must be <= {rule.MaxValue.Value}.");
            }
            else
            {
                errors.Add($"{propertyName} is not a numeric value for range validation.");
            }
        }

        // Allowed values
        if (rule.AllowedValues != null && rule.AllowedValues.Count > 0)
        {
            var found = rule.AllowedValues.Exists(v => string.Equals(v?.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!found)
                errors.Add($"{propertyName} contains invalid value '{text}'.");
        }

        // Regex/pattern
        var pattern = rule.RegexPattern ?? rule.Pattern;
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            try
            {
                if (!Regex.IsMatch(text, pattern))
                    errors.Add($"{propertyName} format is invalid.");
            }
            catch (Exception ex)
            {
                errors.Add($"Invalid regex for {propertyName}: {ex.Message}");
            }
        }

        return errors;
    }

    // Helpers
    private static bool IsStringType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return true;
        var t = NormalizeType(type);
        return string.Equals(t, "string", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "string";
        switch (type.Trim().ToLowerInvariant())
        {
            case "int":
            case "int32":
            case "integer":
                return "int";
            case "long":
            case "int64":
            case "bigint":
                return "long";
            case "string":
            case "text":
            case "varchar":
            case "nvarchar":
                return "string";
            case "bool":
            case "boolean":
                return "bool";
            case "decimal":
            case "money":
                return "decimal";
            case "datetime":
            case "date":
            case "timestamp":
                return "System.DateTime";
            case "guid":
            case "uuid":
                return "System.Guid";
            default:
                return type;
        }
    }
}
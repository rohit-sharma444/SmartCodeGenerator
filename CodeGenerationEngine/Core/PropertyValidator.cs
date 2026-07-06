using CodeGenerationEngine.Models;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for validating property definitions.
/// </summary>
public static class PropertyValidator
{
    public static ValidationResults Validate(PropertyDefinition property)
    {
        if (string.IsNullOrWhiteSpace(property.Name))
            return ValidationResults.Fail("Property name cannot be empty.");

        if (string.IsNullOrWhiteSpace(property.Type))
            return ValidationResults.Fail($"Property '{property.Name}' has no type.");

        if (!Regex.IsMatch(property.Name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return ValidationResults.Fail($"Invalid property name '{property.Name}'.");

        return ValidationResults.Success();
    }
}
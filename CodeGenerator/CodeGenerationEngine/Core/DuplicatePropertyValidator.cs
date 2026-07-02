using System;
using System.Collections.Generic;
using System.Linq;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides functionality to validate that there are no duplicate property names in the list of properties defined for a class.
/// </summary>
public static class DuplicatePropertyValidator
{
    public static ValidationResults Validate(IEnumerable<PropertyDefinition> properties)
    {
        var duplicates = properties
                .Where(p => !string.IsNullOrWhiteSpace(p?.Name))
                .GroupBy(p => p.Name!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

        if (duplicates.Count != 0)
        {
            return ValidationResults.Fail(
                $"Duplicate properties: {string.Join(", ", duplicates)}");
        }

        return ValidationResults.Success();
    }
}
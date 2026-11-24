namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for validating that the specified type is supported and correctly formatted.
/// </summary>
public static class TypeValidator
{
    // FIX: Extended supported types list to include more C# types and handle case variations
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Primitive numeric types
        "string",
        "int",
        "long",
        "double",
        "decimal",
        "float",
        "byte",
        "sbyte",
        "short",
        "ushort",
        "uint",
        "ulong",
        "char",
        
        // Boolean type
        "bool",
        "boolean",
        
        // Date/Time types
        "datetime",
        "date",
        "time",
        "timespan",
        
        // Guid/UUID types
        "guid",
        "uuid",
        
        // Nullable variants (for future support)
        "int?",
        "long?",
        "decimal?",
        "double?",
        "float?",
        "byte?",
        "bool?",
        "datetime?",
        "guid?",
        
        // Collections (basic support)
        "list",
        "array",
        "ienumerable",
        "enumerable"
    };

    /// <summary>
    /// A supported type made nullable (<c>string?</c>), an array of one (<c>int[]</c>), or a list of
    /// one (<c>List&lt;int&gt;</c>): what create class reads from its property list, and what add
    /// property already writes.
    /// </summary>
    private static bool IsSupportedComposite(string type)
    {
        foreach (var suffix in (string[])["?", "[]"])
        {
            if (type.Length > suffix.Length && type.EndsWith(suffix, StringComparison.Ordinal))
                return SupportedTypes.Contains(type[..^suffix.Length].TrimEnd());
        }

        var list = System.Text.RegularExpressions.Regex.Match(type, @"^List\s*<\s*(?<item>[^<>,]+?)\s*>$");
        return list.Success && Validate(list.Groups["item"].Value).IsValid;
    }

    /// <summary>
    /// Validates that the specified type is supported.
    /// </summary>
    public static ValidationResults Validate(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return ValidationResults.Fail("Type is missing.");

        var t = type.Trim();

        // Check if type is supported
        if (!SupportedTypes.Contains(t) && !IsSupportedComposite(t))
        {
            // Provide helpful suggestions for common mistakes
            string suggestion = "";

            if (t.Equals("datetime", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
                suggestion = " Did you mean 'datetime'?";
            else if (t.Equals("guid", StringComparison.OrdinalIgnoreCase) ||
                     t.Equals("Guid", StringComparison.OrdinalIgnoreCase))
                suggestion = " Did you mean 'guid'?";
            else if (t.Equals("Guid", StringComparison.OrdinalIgnoreCase))
                suggestion = " Did you mean 'guid'?";
            else if (t.Equals("Int32", StringComparison.OrdinalIgnoreCase))
                suggestion = " Did you mean 'int'?";
            else if (t.Equals("Decimal", StringComparison.OrdinalIgnoreCase))
                suggestion = " Did you mean 'decimal'?";
            else if (t.Equals("Double", StringComparison.OrdinalIgnoreCase))
                suggestion = " Did you mean 'double'?";
            else if (t.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
                suggestion = " Did you mean 'bool'?";

            return ValidationResults.Fail($"Unsupported type '{type}'.{suggestion}");
        }

        return ValidationResults.Success();
    }

    /// <summary>
    /// Gets list of all supported types for help/documentation.
    /// </summary>
    public static IEnumerable<string> GetSupportedTypes()
    {
        return SupportedTypes.OrderBy(t => t);
    }

    /// <summary>
    /// Normalizes a type name to the canonical form.
    /// </summary>
    public static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return "string";

        var t = type.Trim();

        // Handle case variations
        return t.ToLowerInvariant() switch
        {
            "datetime" => "datetime",
            "date" => "date",
            "time" => "time",
            "timespan" => "timespan",
            "guid" => "guid",
            "uuid" => "guid",  // Alias for guid
            "bool" => "bool",
            "boolean" => "bool",
            "int" => "int",
            "int32" => "int",
            "integer" => "int",
            "long" => "long",
            "int64" => "long",
            "double" => "double",
            "float" => "float",
            "single" => "float",
            "decimal" => "decimal",
            "string" => "string",
            "byte" => "byte",
            "sbyte" => "sbyte",
            "short" => "short",
            "int16" => "short",
            "ushort" => "ushort",
            "uint16" => "ushort",
            "uint" => "uint",
            "uint32" => "uint",
            "ulong" => "ulong",
            "uint64" => "ulong",
            "char" => "char",
            _ => t.ToLowerInvariant()
        };
    }
}

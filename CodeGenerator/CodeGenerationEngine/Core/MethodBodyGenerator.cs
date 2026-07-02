using CodeGenerationEngine.Models;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Generates intelligent method bodies based on method names,
/// return types and class metadata.
/// </summary>
public static class MethodBodyGenerator
{
    public static IEnumerable<string> Generate(string className,
        MethodDefinition method, List<PropertyDefinition> properties)
    {
        if (method == null)
            return [];

        var name = method.Name?.Trim() ?? string.Empty;
        var lower = name.ToLowerInvariant();

        if (lower == "tostring")
            return GenerateToString(properties);

        if (lower == "equals")
            return GenerateEquals(className, properties);

        if (lower == "gethashcode")
            return GenerateGetHashCode(properties);

        if (lower.Contains("byid"))
            return GenerateGetById(method);

        if (Regex.IsMatch(name, @"By[A-Z]"))
            return GenerateFindBy(method);

        if (StartsWithAny(lower, "exists", "has"))
            return GenerateExists();

        if (StartsWithAny(lower, "count"))
            return GenerateCount(method);

        if (StartsWithAny(lower, "validate", "isvalid"))
            return GenerateValidate(properties);

        if (StartsWithAny(lower, "clone", "copy"))
            return GenerateClone(className, properties);

        if (StartsWithAny(lower, "create", "add", "insert"))
            return GenerateCreate(className, properties);

        if (StartsWithAny(lower, "update", "edit", "modify"))
            return GenerateUpdate();

        if (StartsWithAny(lower, "delete", "remove"))
            return GenerateDelete();

        if (StartsWithAny(lower, "get", "find", "load", "fetch"))
            return GenerateGet(method);

        return GenerateDefault(method.ReturnType);
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
        => prefixes.Any(value.StartsWith);

    #region Get Methods

    private static IEnumerable<string> GenerateGet(MethodDefinition method)
    {
        var lines = new List<string>
        {
            "// TODO: Implement retrieval logic"
        };

        lines.AddRange(GenerateDefault(method.ReturnType));

        return lines;
    }

    private static IEnumerable<string> GenerateGetById(MethodDefinition method)
    {
        var lines = new List<string>
        {
            "// TODO: Replace with repository lookup",
            "// Example:",
            "// return _items.FirstOrDefault(x => x.Id == id);"
        };

        lines.AddRange(GenerateDefault(method.ReturnType));

        return lines;
    }

    private static IEnumerable<string> GenerateFindBy(MethodDefinition method)
    {
        var match = Regex.Match(method.Name, @"By([A-Z]\w+)");

        var property = match.Success
                ? match.Groups[1].Value
                : "Property";

        var parameter = char.ToLowerInvariant(property[0]) + property.Substring(1);

        var lines = new List<string>
        {
            $"// TODO: Search by {property}",
            $"// Example:",
            $"// return _items.FirstOrDefault(x => x.{property} == {parameter});"
        };

        lines.AddRange(GenerateDefault(method.ReturnType));

        return lines;
    }

    #endregion

    #region CRUD

    private static IEnumerable<string> GenerateCreate(string className,
        List<PropertyDefinition> properties)
    {
        if (properties.Count == 0)
        {
            return
            [
                $"return new {className}();"
            ];
        }

        var lines = new List<string>
        {
            $"return new {className}",
            "{"
        };

        for (int i = 0; i < properties.Count; i++)
        {
            var property = properties[i];

            var comma =
                i < properties.Count - 1
                    ? ","
                    : string.Empty;

            lines.Add($"    {property.Name} = {GetDefaultValue(property.Type)}{comma}");
        }

        lines.Add("};");

        return lines;
    }

    private static IEnumerable<string> GenerateUpdate()
    {
        return
        [
            "// TODO: Update entity",
            "// Apply changes here"
        ];
    }

    private static IEnumerable<string> GenerateDelete()
    {
        return
        [
            "// TODO: Delete entity"
        ];
    }

    #endregion

    #region Utility Methods

    private static IEnumerable<string> GenerateExists()
    {
        return
        [
            "// TODO: Check existence",
            "return false;"
        ];
    }

    private static IEnumerable<string> GenerateCount(MethodDefinition method)
    {
        return
        [
            "// TODO: Count matching records",
            "return 0;"
        ];
    }

    #endregion

    #region Validation

    private static IEnumerable<string> GenerateValidate(
        List<PropertyDefinition> properties)
    {
        var lines = new List<string>();

        foreach (var property in properties)
        {
            switch (property.Type.ToLowerInvariant())
            {
                case "string":

                    lines.Add($"if (string.IsNullOrWhiteSpace({property.Name}))");
                    lines.Add("    return false;");
                    lines.Add(string.Empty);
                    break;

                case "int":
                case "long":
                case "short":
                    lines.Add($"if ({property.Name} <= 0)");
                    lines.Add("    return false;");
                    lines.Add(string.Empty);
                    break;

                case "decimal":
                case "double":
                case "float":
                    lines.Add($"if ({property.Name} <= 0)");
                    lines.Add("    return false;");
                    lines.Add(string.Empty);
                    break;

                case "guid":
                    lines.Add($"if ({property.Name} == Guid.Empty)");
                    lines.Add("    return false;");
                    lines.Add(string.Empty);
                    break;
            }

            foreach (var rule in property.Rules)
            {
                if (rule.MinLength.HasValue)
                {
                    lines.Add($"if ({property.Name}.Length < {rule.MinLength.Value})");
                    lines.Add("    return false;");
                    lines.Add(string.Empty);
                }

                if (rule.MaxLength.HasValue)
                {
                    lines.Add($"if ({property.Name}.Length > {rule.MaxLength.Value})");
                    lines.Add("    return false;");
                    lines.Add(string.Empty);
                }
            }
        }

        lines.Add("return true;");

        return lines;
    }

    #endregion

    #region Object Helpers

    private static IEnumerable<string> GenerateClone(
        string className, List<PropertyDefinition> properties)
    {
        var lines = new List<string>
        {
            $"return new {className}",
            "{"
        };

        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];

            var comma =
                i < properties.Count - 1
                    ? ","
                    : string.Empty;

            lines.Add($"    {property.Name} = {property.Name}{comma}");
        }

        lines.Add("};");

        return lines;
    }

    private static IEnumerable<string> GenerateToString(
        List<PropertyDefinition> properties)
    {
        if (properties.Count == 0)
            return ["return string.Empty;"];

        var text = string.Join(", ", properties.Select(
                    p => $"{p.Name}: {{{p.Name}}}"));

        return
        [
            $"return $\"{text}\";"
        ];
    }

    private static IEnumerable<string> GenerateEquals(
        string className, List<PropertyDefinition> properties)
    {
        if (properties.Count == 0)
        {
            return
            [
                $"return obj is {className};"
            ];
        }

        var comparison = string.Join(
                " && ", properties.Select(
                    p => $"{p.Name} == other.{p.Name}"));

        return
        [
            $"if (obj is not {className} other)",
            "{",
            "    return false;",
            "}",
            "",
            $"return {comparison};"
        ];
    }

    private static IEnumerable<string> GenerateGetHashCode(
        List<PropertyDefinition> properties)
    {
        if (properties.Count == 0)
            return ["return 0;"];

        var values = string.Join(
                ", ", properties.Select(p => p.Name));

        return
        [
            $"return HashCode.Combine({values});"
        ];
    }

    #endregion

    #region Defaults

    private static IEnumerable<string> GenerateDefault(string returnType)
    {
        if (string.IsNullOrWhiteSpace(returnType))
            return [];

        if (returnType.EndsWith("Async"))
        {
            return
            [
                "return Task.CompletedTask;"
            ];
        }

        if (returnType.StartsWith("List<"))
            return ["return [];"];

        if (returnType.StartsWith("IEnumerable<"))
            return ["return [];"];

        if (returnType.StartsWith("ICollection<"))
            return ["return [];"];

        if (returnType.StartsWith("IReadOnlyCollection<"))
            return ["return [];"];

        return returnType switch
        {
            "void" => [],
            "string" => ["return string.Empty;"],
            "bool" => ["return false;"],
            "byte" => ["return 0;"],
            "short" => ["return 0;"],
            "int" => ["return 0;"],
            "long" => ["return 0L;"],
            "float" => ["return 0f;"],
            "double" => ["return 0d;"],
            "decimal" => ["return 0m;"],
            "char" => ["return '\\0';"],
            "DateTime" => ["return DateTime.MinValue;"],
            "Guid" => ["return Guid.Empty;"],
            _ => ["return default!;"]
        };
    }

    private static string GetDefaultValue(string type)
    {
        return type switch
        {
            "string" => "string.Empty",
            "bool" => "false",
            "int" => "0",
            "long" => "0L",
            "decimal" => "0m",
            "double" => "0d",
            "float" => "0f",
            "DateTime" => "DateTime.MinValue",
            "Guid" => "Guid.Empty",
            _ => "default!"
        };
    }

    #endregion
}
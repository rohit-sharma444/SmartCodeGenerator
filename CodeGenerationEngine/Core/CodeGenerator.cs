using CodeGenerationEngine.Models;
using System.Text;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides functionality to generate C# code based on parsed commands and definitions.
/// </summary>
public static class CodeGenerationEngine
{
    public static string Generate(ParsedCommand command)
    {
        return command == null
            ? throw new ArgumentNullException(nameof(command))
            : command.Intent switch
            {
                IntentType.CreateClass => GenerateClass(command),
                IntentType.CreateInterface => GenerateInterface(command),
                _ => "// Unsupported command"
            };
    }

    private static string GenerateClass(ParsedCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sb = new StringBuilder();

        //var className = ToPascalCase(command.ClassName ?? "NewClass");
        //sb.AppendLine($"public class {className}");
        var className = ToPascalCase(command.ClassName ?? "NewClass");

        var inheritanceParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(command.BaseClass) &&
            string.Equals(command.ClassName, command.BaseClass,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A class cannot inherit from itself.");
        }

        if (!string.IsNullOrWhiteSpace(command.BaseClass))
            inheritanceParts.Add(ToPascalCase(command.BaseClass));

        if (command.Interfaces != null)
        {
            inheritanceParts.AddRange(command.Interfaces
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Select(ToPascalCase)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        var inheritanceClause =
            inheritanceParts.Count > 0
                ? $" : {string.Join(", ", inheritanceParts)}"
                : string.Empty;

        sb.AppendLine($"public class {className}{inheritanceClause}");

        sb.AppendLine("{");

        foreach (var property in command.Properties ?? Enumerable.Empty<PropertyDefinition>())
        {
            if (property is null)
                continue;

            var propertyName = ToPascalCase(property.Name ?? string.Empty);
            var propertyType = ResolvePropertyType(property);

            foreach (var rule in property.Rules)
            {
                if (rule.IsRequired)
                    sb.AppendLine("    [Required]");

                if (rule.MaxLength.HasValue)
                    sb.AppendLine($"    [StringLength({rule.MaxLength.Value})]");

                if (rule.MinLength.HasValue)
                    sb.AppendLine($"    [MinLength({rule.MinLength.Value})]");

                if (rule.MinValue.HasValue)
                    sb.AppendLine($"    [Range({rule.MinValue.Value}, double.MaxValue)]");

                if (rule.MaxValue.HasValue)
                    sb.AppendLine($"    [Range(double.MinValue, {rule.MaxValue.Value})]");
            }

            // The initial value add property gives, so a project with Nullable on does not warn
            // CS8618 for each string and collection a new class declares.
            var initial = PropertyInitializers.SafeFor(
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseTypeName(propertyType), bound: null);
            var initializer = initial is null ? string.Empty : $" = {initial};";

            sb.AppendLine($"    public {propertyType} {propertyName} {{ get; set; }}{initializer}");
            sb.AppendLine();
        }

        // Methods
        if (command.Methods != null && command.Methods.Any())
        {
            sb.AppendLine();
            foreach (var method in command.Methods)
            {
                var methodName = ToPascalCase(method.Name);

                var returnType = NormalizeType(
                    string.IsNullOrWhiteSpace(method.ReturnType)
                        ? "void"
                        : method.ReturnType);

                var parameters = string.Join(", ",
                    method.Parameters.Select(p =>
                        $"{NormalizeType(p.Type)} {p.Name}"));

                if (returnType.Equals("void",
                    StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(
                        $"    public {returnType} {methodName}({parameters})");
                    sb.AppendLine("    {");
                    sb.AppendLine("    }");
                }
                else
                {
                    sb.AppendLine(
                        $"    public {returnType} {methodName}({parameters})");
                    sb.AppendLine("    {");
                    sb.AppendLine("        return default!;");
                    sb.AppendLine("    }");
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateInterface(ParsedCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var interfaceName = command.InterfaceName;

        if (string.IsNullOrWhiteSpace(interfaceName))
            interfaceName = command.ClassName;

        if (string.IsNullOrWhiteSpace(interfaceName))
            interfaceName = "INewInterface";

        interfaceName = InterfaceNaming.NameFor(interfaceName);

        var sb = new StringBuilder();

        sb.AppendLine($"public interface {interfaceName}");
        sb.AppendLine("{");

        // Properties
        foreach (var property in command.Properties ?? Enumerable.Empty<PropertyDefinition>())
        {
            var propertyName = ToPascalCase(property.Name);
            var propertyType = ResolvePropertyType(property);

            sb.AppendLine(
                $"    {propertyType} {propertyName} {{ get; set; }}");
        }

        if (command.Properties is { Count: > 0 } &&
            command.Methods is { Count: > 0 })
        {
            sb.AppendLine();
        }

        // Methods
        foreach (var method in command.Methods ?? Enumerable.Empty<MethodDefinition>())
        {
            var methodName = ToPascalCase(method.Name);

            var returnType = NormalizeType(
                string.IsNullOrWhiteSpace(method.ReturnType)
                    ? "void"
                    : method.ReturnType);

            var parameters = string.Join(", ",
                method.Parameters.Select(p =>
                    $"{NormalizeType(p.Type)} {p.Name}"));

            sb.AppendLine(
                $"    {returnType} {methodName}({parameters});");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string ResolvePropertyType(PropertyDefinition? property)
    {
        if (property == null)
            return "string";

        if (!string.IsNullOrWhiteSpace(property.Type))
            return NormalizeType(property.Type);

        return NormalizeType(FieldTypeHeuristics.Infer(property.Name ?? string.Empty));
    }

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return "string";

        // `guid?` and `datetime[]` are the built-in name with a suffix: the name is mapped and
        // the suffix kept, as `guid` alone becomes System.Guid.
        var trimmed = type.Trim();
        foreach (var suffix in (string[])["?", "[]"])
        {
            if (trimmed.Length > suffix.Length && trimmed.EndsWith(suffix, StringComparison.Ordinal))
                return NormalizeType(trimmed[..^suffix.Length]) + suffix;
        }

        return trimmed.ToLowerInvariant() switch
        {
            "int" or "integer" or "int32" => "int",
            "long" or "int64" or "bigint" => "long",
            "string" or "text" or "varchar" or "nvarchar" => "string",
            "bool" or "boolean" => "bool",
            "decimal" or "money" or "currency" => "decimal",
            "double" => "double",
            "float" => "float",
            "datetime" or "date" or "timestamp" => "System.DateTime",
            "datetimeoffset" or "timestamptz" => "System.DateTimeOffset",
            "guid" or "uuid" or "uid" or "uniqueidentifier" => "System.Guid",
            "byte" => "byte",
            "void" => "void",
            "short" or "int16" => "short",
            "char" => "char",
            "object" => "object",
            _ => type,
        };
    }

    private static IEnumerable<string> KnownTypes()
    {
        return ["int", "string", "bool", "datetime", "guid", "double", "decimal", "float", "long", "byte", "short", "char", "object"];
    }


    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var parts = name.Split(['_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p.Substring(1) : string.Empty)));
    }
}
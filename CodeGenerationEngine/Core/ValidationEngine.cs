using CodeGenerationEngine.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// A simple validation engine that can validate an object instance against a set of property rules defined in PropertyRule objects.
/// </summary>
public static class ValidationEngine
{
    /// <summary>
    /// Validate an object instance against a set of property rules.
    /// </summary>
    public static List<string> Validate(object instance, IEnumerable<PropertyRule> rules)
    {
        var errors = new List<string>();
        if (instance == null)
        {
            errors.Add("Instance is null.");
            return errors;
        }

        var type = instance.GetType();
        foreach (var rule in rules ?? Array.Empty<PropertyRule>())
        {
            if (string.IsNullOrWhiteSpace(rule.PropertyName))
                continue;

            // Try to find a matching property on the instance (case-insensitive)
            var propInfo = type.GetProperty(rule.PropertyName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            object? value = null;
            if (propInfo != null)
                value = propInfo.GetValue(instance);

            // Required check
            if (rule.IsRequired)
            {
                if (value == null || string.IsNullOrWhiteSpace(value?.ToString()))
                {
                    errors.Add($"{rule.PropertyName} is required.");
                    // continue validating other rules for this property (report multiple issues)
                }
            }

            if (value == null)
                continue;

            var text = value.ToString() ?? string.Empty;

            // Length constraints
            if (rule.MinLength.HasValue && text.Length < rule.MinLength.Value)
                errors.Add($"{rule.PropertyName} minimum length is {rule.MinLength.Value}.");

            if (rule.MaxLength.HasValue && text.Length > rule.MaxLength.Value)
                errors.Add($"{rule.PropertyName} maximum length is {rule.MaxLength.Value}.");

            // Numeric range checks (try parse as decimal)
            if (rule.MinValue.HasValue || rule.MaxValue.HasValue)
            {
                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                {
                    if (rule.MinValue.HasValue && number < rule.MinValue.Value)
                        errors.Add($"{rule.PropertyName} must be >= {rule.MinValue.Value}.");

                    if (rule.MaxValue.HasValue && number > rule.MaxValue.Value)
                        errors.Add($"{rule.PropertyName} must be <= {rule.MaxValue.Value}.");
                }
                else
                {
                    errors.Add($"{rule.PropertyName} is not a numeric value for range validation.");
                }
            }

            // Allowed values
            if (rule.AllowedValues != null && rule.AllowedValues.Count > 0)
            {
                var found = rule.AllowedValues
                    .Exists(v => string.Equals(v?.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase));
                if (!found)
                    errors.Add($"{rule.PropertyName} contains invalid value '{text}'.");
            }

            // Regex pattern
            var pattern = rule.RegexPattern ?? rule.Pattern;
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                try
                {
                    if (!Regex.IsMatch(text, pattern!))
                        errors.Add($"{rule.PropertyName} format is invalid.");
                }
                catch (Exception ex)
                {
                    errors.Add($"Invalid regex for {rule.PropertyName}: {ex.Message}");
                }
            }

            // Expected type check (best-effort via simple normalization)
            if (!string.IsNullOrWhiteSpace(rule.ExpectedType))
            {
                var expected = NormalizeType(rule.ExpectedType);
                var actual = InferTypeFromString(text);
                if (!string.IsNullOrWhiteSpace(actual) && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{rule.PropertyName} expected type '{expected}' but value looks like '{actual}'. ");
                }
            }

            // Custom rule placeholder: if present, include as info (no execution)
            if (!string.IsNullOrWhiteSpace(rule.CustomRule))
            {
                errors.Add($"{rule.PropertyName}: custom rule present ({rule.CustomRule}).");
            }
        }

        return errors;
    }

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return "string";

        return type.Trim().ToLowerInvariant() switch
        {
            "int" or "int32" or "integer" => "int",
            "long" or "int64" or "bigint" => "long",
            "string" or "text" or "varchar" or "nvarchar" => "string",
            "bool" or "boolean" => "bool",
            "decimal" or "money" => "decimal",
            "datetime" or "date" or "timestamp" => "System.DateTime",
            "guid" or "uuid" => "System.Guid",
            _ => type,
        };
    }

    private static string InferTypeFromString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "string";

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return "int";

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return "long";

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            return "decimal";

        if (bool.TryParse(value, out _))
            return "bool";

        if (Guid.TryParse(value, out _))
            return "System.Guid";

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "System.DateTime";

        return "string";
    }

    // Generates a partial class containing an instance Validate() method for the generated class.
    // The generated Validate method runs the property rules and returns a List<string> of errors.
    public static string GenerateValidationPartial(string ns, string className, IEnumerable<PropertyDefinition> properties, IEnumerable<PropertyRule> rules)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        sb.AppendLine($"    public partial class {className}");
        sb.AppendLine("    {");
        sb.AppendLine("        public System.Collections.Generic.List<string> Validate()");
        sb.AppendLine("        {");
        sb.AppendLine("            var errors = new System.Collections.Generic.List<string>();");

        // For each property, gather applicable rules and emit checks
        var propList = properties.ToList();
        var ruleList = rules?.ToList() ?? new List<PropertyRule>();

        foreach (var prop in propList)
        {
            var propName = prop.Name ?? "";
            var checks = ruleList
                .Where(r => string.Equals(r.PropertyName, propName, StringComparison.OrdinalIgnoreCase) || r.PropertyName == "*")
                .Concat(prop.Rules ?? Enumerable.Empty<PropertyRule>())
                .ToList();

            if (checks.Count == 0)
                continue;

            var safeProp = propName;
            sb.AppendLine($"            // rules for {safeProp}");
            sb.AppendLine($"            try");
            sb.AppendLine("            {");

            // Access property value as object and string
            sb.AppendLine($"                var valObj_{safeProp} = (object)this.{safeProp};");
            sb.AppendLine($"                var val_{safeProp} = valObj_{safeProp} == null ? null : valObj_{safeProp}.ToString();");

            foreach (var r in checks)
            {
                if (r.IsRequired)
                {
                    sb.AppendLine($"                if (valObj_{safeProp} == null || string.IsNullOrWhiteSpace(val_{safeProp}))");
                    sb.AppendLine($"                    errors.Add(\"{safeProp} is required.\");");
                }

                if (r.MinLength.HasValue)
                {
                    sb.AppendLine($"                if (val_{safeProp} != null && val_{safeProp}.Length < {r.MinLength.Value})");
                    sb.AppendLine($"                    errors.Add(\"{safeProp} minimum length is {r.MinLength.Value}.\");");
                }

                if (r.MaxLength.HasValue)
                {
                    sb.AppendLine($"                if (val_{safeProp} != null && val_{safeProp}.Length > {r.MaxLength.Value})");
                    sb.AppendLine($"                    errors.Add(\"{safeProp} maximum length is {r.MaxLength.Value}.\");");
                }

                if (r.MinValue.HasValue)
                {
                    sb.AppendLine($"                if (val_{safeProp} != null && decimal.TryParse(val_{safeProp}, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var num_{safeProp}))");
                    sb.AppendLine($"                {{");
                    sb.AppendLine($"                    if (num_{safeProp} < {r.MinValue.Value}m) errors.Add(\"{safeProp} must be >= {r.MinValue.Value}.\");");
                    sb.AppendLine($"                }}");
                }

                if (r.MaxValue.HasValue)
                {
                    sb.AppendLine($"                if (val_{safeProp} != null && decimal.TryParse(val_{safeProp}, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var num2_{safeProp}))");
                    sb.AppendLine($"                {{");
                    sb.AppendLine($"                    if (num2_{safeProp} > {r.MaxValue.Value}m) errors.Add(\"{safeProp} must be <= {r.MaxValue.Value}.\");");
                    sb.AppendLine($"                }}");
                }

                if (r.AllowedValues != null && r.AllowedValues.Any())
                {
                    var allowedArray = string.Join(", ", r.AllowedValues.Select(v => "@\"" + v.Replace("\"", "\"\"") + "\""));
                    sb.AppendLine($"                if (val_{safeProp} != null)");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    var allowed = new[] {{ {allowedArray} }};");
                    sb.AppendLine($"                    if (!allowed.Any(a => string.Equals(a, val_{safeProp}, StringComparison.OrdinalIgnoreCase)))");
                    sb.AppendLine($"                        errors.Add(\"{safeProp} contains invalid value '{'{'}' + val_{safeProp} + '{'}'}'.\");");
                    sb.AppendLine("                }");
                }

                if (!string.IsNullOrWhiteSpace(r.RegexPattern))
                {
                    var pattern = r.RegexPattern.Replace("\"", "\\\"");
                    sb.AppendLine($"                if (val_{safeProp} != null)");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    try {{ if (!System.Text.RegularExpressions.Regex.IsMatch(val_{safeProp}, @\"{pattern}\")) errors.Add(\"{safeProp} format is invalid.\"); }}");
                    sb.AppendLine($"                    catch (System.Exception) {{ errors.Add(\"{safeProp} has invalid pattern configured.\"); }}");
                    sb.AppendLine("                }");
                }
            }

            sb.AppendLine("            }");
            sb.AppendLine("            catch (System.Exception) { /* best-effort validation - ignore runtime reflection errors */ }");
        }

        sb.AppendLine("            return errors;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

}

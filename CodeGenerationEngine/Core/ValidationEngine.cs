using CodeGenerationEngine.Models;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerationEngine.Core;

/// <summary>
/// A simple validation engine that can validate an object instance against a set of property rules defined in PropertyRule objects.
/// </summary>
public static class ValidationEngine
{
    /// <summary>
    /// Emits a partial class with a single <c>Validate()</c> that runs every stored
    /// rule. Data rules emit first (a "// Data validation" section), then business
    /// rules ("// Business rules"). Each rule's checks are wrapped in try/catch so one
    /// bad rule cannot abort the rest — best-effort, matching the generator's style.
    /// </summary>
    public static string EmitValidationPartial(
        string ns,
        string className,
        IReadOnlyList<PropertyDefinition> properties,
        IReadOnlyList<IValidationRule> rules)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        sb.AppendLine($"    public partial class {className}");
        sb.AppendLine("    {");
        sb.AppendLine("        public System.Collections.Generic.List<string> Validate()");
        sb.AppendLine("        {");
        sb.AppendLine("            var errors = new System.Collections.Generic.List<string>();");

        var ctx = new ValidationEmitContext(sb, properties);

        EmitSection(sb, ctx, "// Data validation",
            rules.Where(r => r.Category == ValidationCategory.Data));
        EmitSection(sb, ctx, "// Business rules",
            rules.Where(r => r.Category == ValidationCategory.Business));

        sb.AppendLine("            return errors;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitSection(
        System.Text.StringBuilder sb,
        ValidationEmitContext ctx,
        string header,
        IEnumerable<IValidationRule> rules)
    {
        var list = rules.ToList();
        if (list.Count == 0)
            return;

        sb.AppendLine("            " + header);
        foreach (var rule in list)
        {
            // A rule on a field the class no longer has (removed or renamed since the rule was
            // added) would read a member that does not exist, and the file would not compile.
            // The two-field business rules already check this themselves; the data rules did not.
            var missing = rule.Properties.Where(p => !ctx.HasProperty(p)).ToList();
            if (missing.Count > 0)
            {
                sb.AppendLine($"            // Skipped: {rule.Describe().Replace("\r", " ").Replace("\n", " ")}"
                    + $" ({string.Join(", ", missing)} is not a property of this class)");
                continue;
            }

            sb.AppendLine("            try");
            sb.AppendLine("            {");
            rule.Emit(ctx);            // each rule's locals are scoped to this block
            sb.AppendLine("            }");
            sb.AppendLine("            catch (System.Exception) { }");
        }
    }

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

}

using CodeGenerationEngine.Models;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Parses human-readable validation rules into structured PropertyRule objects
/// and can generate a C# class (with a runtime Validate() partial) from natural-language rules.
/// </summary>
public static class ValidationRuleParser
{
    /// <summary>
    /// Parse one or more natural-language rule sentences into PropertyRule objects.
    /// Accepts multi-line input; attempts to parse common phrases like:
    /// - "Name is required"
    /// - "Name length must be between 3 and 50"
    /// - "Age must be between 18 and 60"
    /// - "Email must be a valid email"
    /// - "Country must be India, USA, UK"
    /// Falls back to simple tokenization when necessary. Returns an empty list when nothing recognized.
    /// </summary>
    public static List<PropertyRule> Parse(string input)
    {
        var rules = new List<PropertyRule>();

        if (string.IsNullOrWhiteSpace(input))
            return rules;

        // Split into candidate sentences/lines
        var segments = input
            .Replace("\r", "")
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(l => l.Split(new[] { '.', ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        foreach (var segment in segments)
        {
            // try the existing single-line patterns (case-insensitive)
            var s = segment.Trim();

            // Name is required
            var match = Regex.Match(s, @"^(?<prop>\w+)\s+is\s+required$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                rules.Add(new PropertyRule
                {
                    PropertyName = match.Groups["prop"].Value,
                    IsRequired = true
                });
                continue;
            }

            // Name length must be between 3 and 50
            match = Regex.Match(s, @"^(?<prop>\w+)\s+length\s+must\s+be\s+between\s+(?<min>\d+)\s+and\s+(?<max>\d+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                rules.Add(new PropertyRule
                {
                    PropertyName = match.Groups["prop"].Value,
                    MinLength = int.Parse(match.Groups["min"].Value),
                    MaxLength = int.Parse(match.Groups["max"].Value)
                });
                continue;
            }

            // Age must be between 18 and 60
            match = Regex.Match(s, @"^(?<prop>\w+)\s+must\s+be\s+between\s+(?<min>[-+]?[0-9]*\.?[0-9]+)\s+and\s+(?<max>[-+]?[0-9]*\.?[0-9]+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                rules.Add(new PropertyRule
                {
                    PropertyName = match.Groups["prop"].Value,
                    MinValue = decimal.Parse(match.Groups["min"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    MaxValue = decimal.Parse(match.Groups["max"].Value, System.Globalization.CultureInfo.InvariantCulture)
                });
                continue;
            }

            // Salary must be greater than 0
            match = Regex.Match(s, @"^(?<prop>\w+)\s+must\s+be\s+greater\s+than\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var v = decimal.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
                rules.Add(new PropertyRule
                {
                    PropertyName = match.Groups["prop"].Value,
                    MinValue = v + 0.0001m
                });
                continue;
            }

            // Salary must be less than 100000
            match = Regex.Match(s, @"^(?<prop>\w+)\s+must\s+be\s+less\s+than\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var v = decimal.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
                rules.Add(new PropertyRule
                {
                    PropertyName = match.Groups["prop"].Value,
                    MaxValue = v - 0.0001m
                });
                continue;
            }

            // Email must be valid email
            match = Regex.Match(s, @"^(?<prop>\w+)\s+must\s+be\s+(a\s+)?valid\s+email", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                rules.Add(new PropertyRule
                {
                    PropertyName = match.Groups["prop"].Value,
                    RegexPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$"
                });
                continue;
            }

            // Phone must match pattern
            match = Regex.Match(s, @"^(?<prop>\w+)\s+must\s+match\s+(?<pattern>.+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                rules.Add(new PropertyRule
                {
                    PropertyName = match.Groups["prop"].Value,
                    RegexPattern = match.Groups["pattern"].Value.Trim().Trim('\'', '"')
                });
                continue;
            }

            // Country must be India, USA, UK  -> allowed values
            match = Regex.Match(s, @"^(?<prop>\w+)\s+must\s+be\s+(?<values>.+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var vals = match.Groups["values"].Value;
                if (vals.Contains(',') || vals.Contains(" or ") || vals.Contains(" and "))
                {
                    // split by commas or the words 'or'/'and'
                    var parts = vals.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                    .SelectMany(p => p.Split(new[] { " or ", " and " }, System.StringSplitOptions.RemoveEmptyEntries))
                                    .Select(x => x.Trim().Trim('"', '\''))
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .ToList();

                    if (parts.Any())
                    {
                        rules.Add(new PropertyRule
                        {
                            PropertyName = match.Groups["prop"].Value,
                            AllowedValues = parts
                        });
                        continue;
                    }
                }
            }

            // Attempt to parse simple "Name required / min length N / pattern / allowed" inline forms
            // Fallback: try RuleParser with a single constructed line "Prop rest..."
            try
            {
                var fallback = RuleParser.Parse(new[] { s });
                if (fallback != null && fallback.Any())
                {
                    rules.AddRange(fallback);
                }
            }
            catch
            {
                // ignore parse failures on fallback
            }
        }

        return rules;
    }

    /// <summary>
    /// Generate a C# class from a natural-language specification.
    /// The input should contain a class name (e.g. "Create class Person") and a list of properties
    /// (after the word "properties" or simply a comma/newline separated list).
    /// The method returns source code containing the class (auto-properties) and a generated partial Validate() method
    /// (using the existing ValidationEngine.GenerateValidationPartial implementation).
    /// </summary>
    public static string GenerateClassFromNaturalLanguage(string input, string targetNamespace = "Generated")
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Determine class name
        var classMatch = Regex.Match(input, @"(?:create\s+class|class)\s+(?<c>\w+)", RegexOptions.IgnoreCase);
        var className = classMatch.Success ? classMatch.Groups["c"].Value : "GeneratedClass";

        // Locate properties block if present
        string propsText;
        var propsIndex = input.IndexOf("properties", System.StringComparison.OrdinalIgnoreCase);
        if (propsIndex >= 0)
        {
            propsText = input.Substring(propsIndex + "properties".Length);
        }
        else
        {
            // try after class name
            var afterClass = Regex.Match(input, @"(?:class\s+\w+)(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            propsText = afterClass.Success ? afterClass.Groups["rest"].Value : input;
        }

        // Split candidate property fragments by newline or comma/semicolon
        var fragments = propsText
            .Replace("\r", "")
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(l => l.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();

        var properties = new List<PropertyDefinition>();
        var aggregatedRules = new List<PropertyRule>();

        foreach (var fragment in fragments)
        {
            // Attempt to extract: Name [Type] [rule...]
            // Example fragments:
            // "Name string required min length 3"
            // "Age int must be between 18 and 120"
            var tokens = fragment.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count == 0) continue;

            var propName = tokens[0].Trim().Trim(':');
            string typeHint = "string";
            var typeCandidates = new[] { "string", "int", "int32", "long", "int64", "decimal", "money", "bool", "boolean", "datetime", "date", "guid", "uuid", "double", "float" };

            // if second token is a known type, consume it
            if (tokens.Count > 1 && typeCandidates.Contains(tokens[1].ToLowerInvariant()))
            {
                typeHint = tokens[1];
                tokens.RemoveAt(1); // keep name as tokens[0]
            }

            // rest of fragment is rule text
            var rest = string.Join(' ', tokens.Skip(1)).Trim();

            var propDef = new PropertyDefinition
            {
                Name = propName,
                Type = MapToCSharpType(typeHint)
            };

            if (!string.IsNullOrWhiteSpace(rest))
            {
                // Normalize into a form RuleParser can understand: "Name rest..."
                var singleLine = $"{propName} {rest}";
                try
                {
                    var parsed = RuleParser.Parse(new[] { singleLine });
                    if (parsed.Any())
                    {
                        propDef.Rules = parsed;
                        aggregatedRules.AddRange(parsed);
                    }
                    else
                    {
                        // As a fallback also try the simpler Parse rules (sentence style)
                        var sentenceParsed = Parse(singleLine);
                        if (sentenceParsed.Any())
                        {
                            propDef.Rules = sentenceParsed;
                            aggregatedRules.AddRange(sentenceParsed);
                        }
                    }
                }
                catch
                {
                    // ignore parse errors and continue
                }
            }

            properties.Add(propDef);
        }

        // Build class source
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using System.ComponentModel.DataAnnotations;");
        sb.AppendLine();
        sb.AppendLine($"namespace {targetNamespace}");
        sb.AppendLine("{");
        sb.AppendLine($"    public partial class {className}");
        sb.AppendLine("    {");

        foreach (var p in properties)
        {
            // emit a comment summarizing inline rules
            if (p.Rules != null && p.Rules.Any())
            {
                var summary = string.Join("; ", p.Rules.Select(r => BuildRuleSummary(r)));
                sb.AppendLine($"        // rules: {summary}");
            }

            sb.AppendLine($"        public {p.Type} {p.Name} {{ get; set; }}");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // append generated Validate() partial using ValidationEngine helper
        try
        {
            var validationPartial = ValidationEngine.GenerateValidationPartial(targetNamespace, className, properties, aggregatedRules);
            sb.AppendLine(validationPartial);
        }
        catch
        {
            // If validation partial generation fails, still return the class source without it
        }

        return sb.ToString();
    }

    private static string MapToCSharpType(string hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return "string";
        switch (hint.Trim().ToLowerInvariant())
        {
            case "int":
            case "int32":
            case "integer":
                return "int";
            case "long":
            case "int64":
            case "bigint":
                return "long";
            case "bool":
            case "boolean":
                return "bool";
            case "decimal":
            case "money":
                return "decimal";
            case "double":
                return "double";
            case "float":
                return "float";
            case "datetime":
            case "date":
            case "timestamp":
                return "DateTime";
            case "guid":
            case "uuid":
                return "System.Guid";
            default:
                return "string";
        }
    }

    private static string BuildRuleSummary(PropertyRule r)
    {
        var parts = new List<string>();
        if (r.IsRequired) parts.Add("required");
        if (r.MinLength.HasValue) parts.Add($"minLen={r.MinLength.Value}");
        if (r.MaxLength.HasValue) parts.Add($"maxLen={r.MaxLength.Value}");
        if (r.MinValue.HasValue) parts.Add($"minVal={r.MinValue.Value}");
        if (r.MaxValue.HasValue) parts.Add($"maxVal={r.MaxValue.Value}");
        if (r.AllowedValues != null && r.AllowedValues.Any()) parts.Add($"allowed=[{string.Join(", ", r.AllowedValues)}]");
        if (!string.IsNullOrWhiteSpace(r.RegexPattern)) parts.Add($"pattern={r.RegexPattern}");
        if (!string.IsNullOrWhiteSpace(r.ExpectedType)) parts.Add($"type={r.ExpectedType}");
        if (!string.IsNullOrWhiteSpace(r.CustomRule)) parts.Add($"custom={r.CustomRule}");
        return string.Join(", ", parts);
    }
}
using CodeGenerationEngine.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Parses simple human readable rule lines into structured PropertyRule instances.
/// </summary>
public static class RuleParser
{
    /// <summary>
    /// Parse simple human readable rule lines into PropertyRule instances.
    /// Supported fragments:
    /// - "PropertyName required"
    /// - "min length N" / "max length N"
    /// - "min value N" / "max value N"
    /// - "allowed [a,b,c]" or "allowed: a, b, c"
    /// - "regex /pattern/" or "pattern: PATTERN"
    /// - "custom: text"
    /// </summary>
    public static List<PropertyRule> Parse(IEnumerable<string> lines)
    {
        var rules = new List<PropertyRule>();
        if (lines == null)
            return rules;

        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var line = raw.Trim();
            // First token is treated as property name if it doesn't contain a colon form like "name: value"
            var firstTokenMatch = Regex.Match(line, @"^\s*([A-Za-z0-9_\-\*]+)\s*(.*)$");
            if (!firstTokenMatch.Success)
                continue;

            var propName = firstTokenMatch.Groups[1].Value.Trim();
            var rest = firstTokenMatch.Groups[2].Value.Trim();

            var rule = new PropertyRule
            {
                PropertyName = propName
            };

            var lower = rest.ToLowerInvariant();

            if (lower.Contains("required"))
                rule.Required = true;

            // Min/Max length
            var minLen = Regex.Match(rest, @"min(?:imum)?\s*length\s*[:=]?\s*(\d+)", RegexOptions.IgnoreCase);
            if (minLen.Success && int.TryParse(minLen.Groups[1].Value, out var minLenVal))
                rule.MinLength = minLenVal;

            var maxLen = Regex.Match(rest, @"max(?:imum)?\s*length\s*[:=]?\s*(\d+)", RegexOptions.IgnoreCase);
            if (maxLen.Success && int.TryParse(maxLen.Groups[1].Value, out var maxLenVal))
                rule.MaxLength = maxLenVal;

            // Min/Max value (decimal)
            var minVal = Regex.Match(rest, @"min(?:imum)?\s*value\s*[:=]?\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            if (minVal.Success && decimal.TryParse(minVal.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var minValDec))
                rule.MinValue = minValDec;

            var maxVal = Regex.Match(rest, @"max(?:imum)?\s*value\s*[:=]?\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            if (maxVal.Success && decimal.TryParse(maxVal.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxValDec))
                rule.MaxValue = maxValDec;

            // Allowed values: [a,b,c] or allowed: a,b
            var allowedMatch = Regex.Match(rest, @"allowed\s*(?:\:|\[)?\s*([\w\W]*?)\]?$", RegexOptions.IgnoreCase);
            if (allowedMatch.Success)
            {
                var group = allowedMatch.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(group))
                {
                    // remove trailing/leading brackets or colons if present
                    group = group.Trim().Trim('[', ']', ' ', ':');
                    var parts = group.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim())
                                     .Where(p => !string.IsNullOrWhiteSpace(p))
                                     .ToList();
                    if (parts.Any())
                        rule.AllowedValues = parts;
                }
            }

            // Regex /pattern/ style or pattern: xyz
            var regexSlash = Regex.Match(rest, @"\/(.+)\/", RegexOptions.Singleline);
            if (regexSlash.Success)
            {
                rule.RegexPattern = regexSlash.Groups[1].Value.Trim();
            }
            else
            {
                var patternMatch = Regex.Match(rest, @"(?:pattern|regex)\s*(?:\:)?\s*(.+)$", RegexOptions.IgnoreCase);
                if (patternMatch.Success)
                    rule.RegexPattern = patternMatch.Groups[1].Value.Trim().Trim('\'', '"');
            }

            // Custom rule
            var customMatch = Regex.Match(rest, @"custom\s*[:=]\s*(.+)$", RegexOptions.IgnoreCase);
            if (customMatch.Success)
                rule.CustomRule = customMatch.Groups[1].Value.Trim();

            rules.Add(rule);
        }

        return rules;
    }
}

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace NaturalLanguageBridge;

/// <summary>
/// Reads the value list of <c>create enum Status with values Active, Closed and Pending</c>: the
/// names are split on commas and <c>and</c>, each an identifier that is no keyword, none repeated
/// (a value is the same name whatever its case, so <c>Active, active</c> is refused).
/// </summary>
public static class EnumValueParser
{
    private static readonly Regex Separator =
        new(@"\s*,\s*(?:and\s+)?|\s+and\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Identifier =
        new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);

    /// <summary>
    /// True with the values (an empty list for empty text), or false with the reason the list
    /// cannot be written.
    /// </summary>
    public static bool TryParse(string text, out List<string> values, out string? error)
    {
        values = [];
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in Separator.Split(text.Trim()))
        {
            var value = raw.Trim();
            if (!Identifier.IsMatch(value) || SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None)
            {
                error = $"'{value}' is not a valid enum value name.";
                values = [];
                return false;
            }

            if (!seen.Add(value))
            {
                error = $"The value '{value}' is repeated.";
                values = [];
                return false;
            }

            values.Add(value);
        }

        return true;
    }
}

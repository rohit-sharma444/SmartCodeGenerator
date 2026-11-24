using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;

namespace NaturalLanguageBridge;

/// <summary>
/// Reads a method's parameter list: <c>decimal percent, string code</c> in the C# form, or
/// <c>percent decimal, code string</c> in the English form, where the name comes first as it does
/// in a property list.
/// </summary>
/// <remarks>
/// Only the text is checked here: each entry is a type and a name, the name is an identifier and
/// not repeated. Whether the type exists is for the class's project to say, so it is bound where
/// the method is written.
/// </remarks>
public static class MethodParameterParser
{
    public enum Order { TypeFirst, NameFirst }

    /// <summary>The words that start a parameter list in the English form.</summary>
    public const string EnglishLead = @"(?:with\s+parameters?|taking|that\s+takes)";

    /// <summary>
    /// A type as a command names it: <c>decimal</c>, <c>int?</c>, <c>string[]</c>,
    /// <c>Dictionary&lt;string, int&gt;</c>. A space is allowed only inside the angle brackets, so
    /// "returns decimal value" is not read as the type "decimal value".
    /// </summary>
    public const string TypePattern = @"[A-Za-z_][\w\.]*(?:<.+>)?\??(?:\[\])*\??";

    public static bool TryParse(
        string text, Order order, out List<ParameterDefinition> parameters, out string? error)
    {
        parameters = [];
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        foreach (var raw in SplitOutsideBrackets(text))
        {
            var entry = raw.Trim();
            if (entry.Length == 0)
            {
                error = $"The parameter list '{text.Trim()}' has an empty entry.";
                return false;
            }

            var cut = order == Order.TypeFirst ? entry.LastIndexOf(' ') : entry.IndexOf(' ');
            if (cut < 0)
            {
                error = $"The parameter '{entry}' needs a type and a name.";
                return false;
            }

            var (name, type) = order == Order.TypeFirst
                ? (entry[(cut + 1)..], entry[..cut].Trim())
                : (entry[..cut], entry[(cut + 1)..].Trim());

            if (!SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
            {
                error = $"'{name}' is not a valid parameter name.";
                return false;
            }

            var parsed = SyntaxFactory.ParseTypeName(type);
            if (parsed.ContainsDiagnostics || parsed.FullSpan.Length != type.Length)
            {
                error = $"'{type}' is not a C# type (in the parameter '{entry}').";
                return false;
            }

            if (parameters.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"The parameter name '{name}' is repeated.";
                return false;
            }

            parameters.Add(new ParameterDefinition { Name = name, Type = type });
        }

        return true;
    }

    // Commas inside <> belong to a generic type: Dictionary<string, int> map is one entry.
    private static IEnumerable<string> SplitOutsideBrackets(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<' or '(' or '[': depth++; break;
                case '>' or ')' or ']': depth--; break;
                case ',' when depth == 0:
                    yield return text[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return text[start..];
    }
}

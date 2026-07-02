namespace CodeCommenterPro.Roslyn;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeCommenterPro.Helpers;
using System.Linq;
using System;

public static class PatternCommentGenerator
{
    public static string? Generate(SyntaxNode? node, SemanticModel? model = null)
    {
        if (node == null)
            return null;

        return Generate(node.ToString(), node, model);
    }

    public static string? Generate(SyntaxToken token, SemanticModel? model = null) => Generate(token.ToString(), null, model);

    public static string? Generate(SyntaxTrivia trivia, SemanticModel? model = null) => Generate(trivia.ToString(), null, model);

    private static string? Generate(string? text, SyntaxNode? node = null, SemanticModel? model = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var rules = RuleLoader.Load();

        foreach (var p in rules.Patterns)
        {
            if (string.IsNullOrWhiteSpace(p.Match))
                continue;

            // Case-insensitive substring match first
            if (text.IndexOf(p.Match, StringComparison.OrdinalIgnoreCase) >= 0)
                return p.Description;

            // If pattern contains regex-specific characters, attempt regex match as a fallback
            try
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(text, p.Match, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return p.Description;
            }
            catch
            {
                // Ignore malformed regex and continue
            }
        }

        if (model != null && node != null)
        {
            // Analyze invocation expressions to find repository/logger/file operations
            var invocations = node.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var inv in invocations)
            {
                try
                {
                    var sym = model.GetSymbolInfo(inv).Symbol as Microsoft.CodeAnalysis.IMethodSymbol;
                    if (sym == null) continue;

                    var container = sym.ContainingType?.Name ?? string.Empty;
                    var methodName = sym.Name ?? string.Empty;

                    if ((container.IndexOf("Repository", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (methodName.IndexOf("Get", StringComparison.OrdinalIgnoreCase) >= 0 && methodName.IndexOf("Async", StringComparison.OrdinalIgnoreCase) < 0))
                        return "Retrieve data from repository";

                    if (methodName.IndexOf("Find", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Find matching data";

                    if (methodName.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0 || methodName.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Persist data";

                    if (container.IndexOf("Logger", StringComparison.OrdinalIgnoreCase) >= 0 || methodName.IndexOf("Log", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Write log entry";
                }
                catch
                {
                    // ignore symbol resolution issues
                }
            }
        }

        return null;
    }
}

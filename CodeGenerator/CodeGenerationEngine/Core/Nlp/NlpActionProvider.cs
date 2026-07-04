using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

namespace CodeGenerationEngine.Core.Nlp;

/// <summary>
/// Isolates the NaturalLanguageEngine dependency. Exposes only the POS-tagged
/// primary action verb of a command so the rest of CodeGenerationEngine never
/// sees an NLE type. The engine is built once (lazily) and reused. The pipeline
/// returned by <c>EngineBuilder</c> shares mutable stage instances across calls
/// and is not safe for concurrent <c>Analyze</c> invocations (observed as
/// corrupted/empty results when MSTest ran data-row cases in parallel), so
/// calls are serialized here with a lock rather than relying on callers to be
/// single-threaded.
/// </summary>
public static class NlpActionProvider
{
    private static readonly Lazy<ISemanticAnalyzer> Analyzer =
        new(() => EngineBuilder.BuildDefault().Analyzer);

    private static readonly object AnalyzeLock = new();

    /// <summary>
    /// Returns the lowercased action verb NLE identified for <paramref name="input"/>,
    /// or null when the input is blank, has no recognized action, or NLE fails.
    /// Never throws.
    /// </summary>
    public static string? GetPrimaryAction(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        try
        {
            SemanticResult result;
            lock (AnalyzeLock)
            {
                result = Analyzer.Value.Analyze(input);
            }

            var action = result.Entities
                .FirstOrDefault(e => e.Type == EntityType.Action);

            if (action is not null)
            {
                var verb = action.Text
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault();

                if (!string.IsNullOrWhiteSpace(verb))
                    return verb.ToLowerInvariant();
            }

            // Fallback: derive from the intent name ("AddIntent" -> "add"),
            // ignoring the generic no-verb "DescribeIntent".
            var name = result.Intent?.Name;
            if (!string.IsNullOrWhiteSpace(name)
                && name.EndsWith("Intent", StringComparison.Ordinal)
                && !name.Equals("DescribeIntent", StringComparison.Ordinal))
            {
                var verb = name[..^"Intent".Length];
                if (verb.Length > 0)
                    return verb.ToLowerInvariant();
            }

            return null;
        }
        catch
        {
            // NLE throws on null/blank input and could throw on unexpected
            // shapes; a missing signal must never break command parsing.
            return null;
        }
    }
}

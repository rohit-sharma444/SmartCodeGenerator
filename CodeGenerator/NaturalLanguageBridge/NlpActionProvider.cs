using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>
/// Wraps NaturalLanguageEngine for the code-generation front. Builds the engine
/// once (lazily) and reuses it. The pipeline returned by <c>EngineBuilder</c>
/// shares mutable stage instances across calls and is not safe for concurrent
/// <c>Analyze</c> invocations, so calls are serialized with a lock.
/// </summary>
public static class NlpActionProvider
{
    private static readonly Lazy<ISemanticAnalyzer> Analyzer =
        new(() => EngineBuilder.BuildDefault().Analyzer);

    private static readonly object AnalyzeLock = new();

    /// <summary>
    /// Runs NLE once over <paramref name="input"/>. Returns null when the input
    /// is blank or NLE throws. Never throws.
    /// </summary>
    public static SemanticResult? Analyze(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        try
        {
            lock (AnalyzeLock)
            {
                return Analyzer.Value.Analyze(input);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the lowercased primary action verb from an NLE result, or null
    /// when the result is null or has no recognized action.
    /// </summary>
    public static string? GetPrimaryAction(SemanticResult? result)
    {
        if (result is null)
            return null;

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

    /// <summary>Convenience: analyze then extract. Null on blank / no-action / failure.</summary>
    public static string? GetPrimaryAction(string input)
        => GetPrimaryAction(Analyze(input));
}

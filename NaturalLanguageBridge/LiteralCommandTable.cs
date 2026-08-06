using CodeGenerationEngine.Models;

namespace NaturalLanguageBridge;

/// <summary>
/// Exact-match commands that are not natural language and should never enter the
/// NLP path: learning and view meta-commands, plus the demo short-circuit.
/// Mirrors the regex guards in IntentAnalyzer that return score 999. Matching is
/// whitespace-trimmed and case-insensitive over the whole input.
/// </summary>
public static class LiteralCommandTable
{
    private static readonly Dictionary<string, IntentType> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        ["show learning"] = IntentType.ShowLearning,
        ["list learning"] = IntentType.ShowLearning,
        ["view learning"] = IntentType.ShowLearning,
        ["reset learning"] = IntentType.ResetLearning,
        ["clear learning"] = IntentType.ResetLearning,
        ["forget learning"] = IntentType.ResetLearning,
        ["show project"] = IntentType.ShowProject,
        ["add validation"] = IntentType.AddValidation,
        ["show validation"] = IntentType.ShowValidation,
        ["list validation"] = IntentType.ShowValidation,
        ["view validation"] = IntentType.ShowValidation,
        ["add business validation"] = IntentType.AddBusinessValidation,
        ["show business validation"] = IntentType.ShowBusinessValidation,
        ["list business validation"] = IntentType.ShowBusinessValidation,
        ["view business validation"] = IntentType.ShowBusinessValidation,
        ["help"] = IntentType.Help,
        ["validate project"] = IntentType.ValidateProject,
        ["check project"] = IntentType.ValidateProject,
        ["compile project"] = IntentType.ValidateProject,
    };

    /// <summary>The intents this table can produce. Consumed by the reachable-intent
    /// gate test, which asserts every recognizable intent has a handler.</summary>
    public static IReadOnlyCollection<IntentType> Intents => [.. Exact.Values.Distinct()];

    /// <summary>The exact phrases that resolve to <paramref name="intent"/>. Consumed by
    /// the AI corpus harvester, which embeds real user phrasings rather than enum names.</summary>
    public static IReadOnlyList<string> PhrasesFor(IntentType intent) =>
        [.. Exact.Where(pair => pair.Value == intent).Select(pair => pair.Key)];

    public static bool TryResolve(string input, out IntentType intent)
    {
        var key = (input ?? string.Empty).Trim();
        return Exact.TryGetValue(key, out intent);
    }
}

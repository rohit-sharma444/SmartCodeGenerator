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
        ["help"] = IntentType.Help,
    };

    public static bool TryResolve(string input, out IntentType intent)
    {
        var key = (input ?? string.Empty).Trim();
        return Exact.TryGetValue(key, out intent);
    }
}

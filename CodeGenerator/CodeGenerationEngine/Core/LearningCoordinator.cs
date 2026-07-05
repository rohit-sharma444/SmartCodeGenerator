using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Single feedback sink for the command pipeline. Turns command outcomes into
/// reinforcement/weakening of the learned intent memory, treats a quick rephrase
/// of a near-identical command as a negative signal for the prior interpretation,
/// and folds successful commands into vocabulary learning.
/// </summary>
public static class LearningCoordinator
{
    public const double RephraseSimilarity = 0.6;
    public const int RephraseWindowSeconds = 20;

    private static string? _lastSentence;
    private static IntentType _lastIntent;
    private static DateTime _lastUtc = DateTime.MinValue;

    public static void RecordOutcome(
        string sentence,
        IntentType intent,
        bool success,
        ParsedCommand? command)
    {
        if (!LearnedIntentStore.Enabled || string.IsNullOrWhiteSpace(sentence))
            return;

        // The learning-management commands are deterministic meta-commands; never
        // learn them (reinforcing "reset learning" would immediately re-populate
        // the memory the reset just cleared).
        if (intent == IntentType.ShowLearning || intent == IntentType.ResetLearning)
            return;

        var nowUtc = DateTime.UtcNow;

        // Quick-rephrase: a near-identical follow-up soon after the last command
        // means the last interpretation probably missed - weaken it.
        if (_lastSentence != null
            && _lastIntent != IntentType.Unknown
            && (nowUtc - _lastUtc).TotalSeconds <= RephraseWindowSeconds
            && IsRephrase(_lastSentence, sentence))
        {
            LearnedIntentStore.Current.Weaken(_lastSentence, _lastIntent);
        }

        if (intent == IntentType.Unknown || !success)
        {
            if (intent != IntentType.Unknown)
                LearnedIntentStore.Current.Weaken(sentence, intent);
        }
        else
        {
            LearnedIntentStore.Current.Reinforce(sentence, intent);

            if (command != null)
                IntentLearningEngine.Learn(sentence, command);
        }

        LearnedIntentStore.Save();

        _lastSentence = sentence;
        _lastIntent = intent;
        _lastUtc = nowUtc;
    }

    public static void ResetHistory()
    {
        _lastSentence = null;
        _lastIntent = IntentType.Unknown;
        _lastUtc = DateTime.MinValue;
    }

    private static bool IsRephrase(string a, string b)
    {
        var sa = LearnedIntentMemory.Signature(a);
        var sb = LearnedIntentMemory.Signature(b);

        if (sa.Length == 0 || sb.Length == 0)
            return false;

        if (sa.Equals(sb, StringComparison.Ordinal))
            return false; // identical signature is a repeat, not a rephrase

        var setA = sa.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var setB = sb.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        var intersection = setA.Count(setB.Contains);
        var union = setA.Count + setB.Count - intersection;

        return union > 0 && (double)intersection / union >= RephraseSimilarity;
    }
}

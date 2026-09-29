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

    /// <summary>The last command the CLI did not understand, still waiting for the retry that
    /// would say what it meant.</summary>
    private static (string Sentence, DateTime Utc)? _pendingMiss;

    /// <summary>The time, settable so the rephrase window can be tested. Restored by
    /// <see cref="ResetHistory"/>.</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

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

        var nowUtc = Clock();

        PairWithPendingMiss(sentence, intent, success, command, nowUtc);

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
        _pendingMiss = null;
        Clock = () => DateTime.UtcNow;
    }

    /// <summary>
    /// A miss followed within the window by a working retry becomes a candidate phrasing for
    /// the retry's intent; a new miss replaces the one waiting, and any other command ends the wait.
    /// </summary>
    /// <remarks>
    /// The quick-rephrase branch above cannot see this: it skips a previous command whose intent
    /// was Unknown - there is no interpretation to weaken - and that is exactly the case where the
    /// user has just shown what they meant.
    /// </remarks>
    private static void PairWithPendingMiss(
        string sentence, IntentType intent, bool success, ParsedCommand? command, DateTime nowUtc)
    {
        if (intent == IntentType.Unknown)
        {
            _pendingMiss = (sentence, nowUtc);
            return;
        }

        var miss = _pendingMiss;
        _pendingMiss = null;

        if (miss is null || !success || command is null
            || (nowUtc - miss.Value.Utc).TotalSeconds > RephraseWindowSeconds
            || !PhraseCandidates.IsRetry(miss.Value.Sentence, sentence))
            return;

        var phrase = PhraseCandidates.Generalize(miss.Value.Sentence, command);
        if (phrase is not null)
            PhraseCandidates.Record(new PhraseCandidates.Candidate(phrase, intent, miss.Value.Sentence, sentence, nowUtc));
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

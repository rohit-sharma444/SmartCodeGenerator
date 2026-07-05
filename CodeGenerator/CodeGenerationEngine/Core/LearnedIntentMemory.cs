using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// A separate, usage-driven memory that associates a coarse command signature
/// with the intents that have succeeded for it. It only ever nudges intent
/// scoring (see <see cref="BonusCap"/>); it never mutates the base rules.
/// </summary>
public sealed class LearnedIntentMemory
{
    /// <summary>Maximum score bonus a learned suggestion may contribute.</summary>
    public const int BonusCap = 35;

    /// <summary>Minimum confidence before a suggestion is applied at all.</summary>
    public const double SuggestThreshold = 0.5;

    private readonly Dictionary<(string Signature, IntentType Intent), LearnedIntentEntry> _entries = new();

    public IReadOnlyCollection<LearnedIntentEntry> Entries => _entries.Values;

    /// <summary>
    /// Canonical signature: distinct normalized tokens, lower-cased, sorted, so
    /// phrasings that differ only in word order or duplicates collapse together.
    /// </summary>
    public static string Signature(string sentence)
    {
        var tokens = TextTokenizer.Tokenize(sentence ?? string.Empty)
            .Select(t => t.Normalized)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal);

        return string.Join(' ', tokens);
    }

    public void Reinforce(string sentence, IntentType intent)
        => Record(sentence, intent, success: true);

    public void Weaken(string sentence, IntentType intent)
        => Record(sentence, intent, success: false);

    public IntentType? Suggest(string sentence, out double confidence)
    {
        confidence = 0d;

        var signature = Signature(sentence);
        if (string.IsNullOrEmpty(signature))
            return null;

        LearnedIntentEntry? best = null;

        foreach (var entry in _entries.Values)
        {
            if (!entry.Signature.Equals(signature, StringComparison.Ordinal))
                continue;

            if (best == null || entry.Confidence > best.Confidence)
                best = entry;
        }

        if (best == null || best.Confidence < SuggestThreshold)
            return null;

        confidence = best.Confidence;
        return best.Intent;
    }

    public void Reset() => _entries.Clear();

    private void Record(string sentence, IntentType intent, bool success)
    {
        if (intent == IntentType.Unknown)
            return;

        var signature = Signature(sentence);
        if (string.IsNullOrEmpty(signature))
            return;

        var key = (signature, intent);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new LearnedIntentEntry { Signature = signature, Intent = intent };
            _entries[key] = entry;
        }

        if (success)
            entry.SuccessCount++;
        else
            entry.FailureCount++;

        entry.LastUsedUtc = DateTime.UtcNow;
    }
}

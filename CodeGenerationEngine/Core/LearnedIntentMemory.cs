using System.Text.Json;
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
        if (string.IsNullOrWhiteSpace(sentence))
            return string.Empty;

        // Normalize with the surviving command normalizer, then reduce to distinct,
        // lower-cased, sorted tokens so phrasings differing only in word order or
        // duplicates collapse together. (Previously also ran EnglishLanguageModel +
        // TextTokenizer, both retired with the duplicate NLP stack.)
        var normalized = CommandParser.Normalize(sentence);

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
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

    public const int DecayAfterDays = 30;

    public void Save(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(
            _entries.Values.ToList(),
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(filePath, json);
    }

    public void Load(string filePath)
    {
        _entries.Clear();

        if (!File.Exists(filePath))
            return;

        List<LearnedIntentEntry>? entries;
        try
        {
            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return;

            entries = JsonSerializer.Deserialize<List<LearnedIntentEntry>>(json);
        }
        catch (Exception)
        {
            // A corrupt/truncated file (e.g. a crash mid-write) must not brick
            // startup - treat it as no learned state.
            return;
        }

        if (entries == null)
            return;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Signature) || entry.Intent == IntentType.Unknown)
                continue;

            _entries[(entry.Signature, entry.Intent)] = entry;
        }
    }

    /// <summary>
    /// Halves the tallies of entries not used within <see cref="DecayAfterDays"/>
    /// so stale/bad associations fade instead of compounding.
    /// </summary>
    public void Decay(DateTime nowUtc)
    {
        foreach (var entry in _entries.Values)
        {
            if ((nowUtc - entry.LastUsedUtc).TotalDays < DecayAfterDays)
                continue;

            entry.SuccessCount /= 2;
            entry.FailureCount /= 2;
        }
    }

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

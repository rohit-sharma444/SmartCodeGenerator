using NaturalLanguageEngine.Knowledge.Models;
using NaturalLanguageEngine.Knowledge.Providers;

namespace NaturalLanguageEngine.Knowledge.Repositories;

/// <summary>
/// In-memory language repository. Builds case-insensitive indexes
/// from an <see cref="ILanguageProvider"/> for fast lookup during analysis.
/// </summary>
public sealed class LanguageRepository : ILanguageRepository
{
    private readonly Dictionary<string, WordEntry> _words;
    private readonly Dictionary<string, string> _synonyms;
    private readonly Dictionary<string, string> _contractions;
    private readonly HashSet<string> _stopWords;
    private readonly HashSet<string> _abbreviations;
    private readonly Dictionary<string, string[]> _irregulars;
    private readonly HashSet<string> _locations;
    private readonly HashSet<string> _organizations;

    public LanguageRepository(ILanguageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _words = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in provider.LoadWords())
            _words[entry.Word] = entry;

        _synonyms = new(StringComparer.OrdinalIgnoreCase);
        foreach (var set in provider.LoadSynonyms())
            foreach (var word in set.Words)
                _synonyms[word] = set.Canonical;

        _contractions = new(provider.LoadContractions(), StringComparer.OrdinalIgnoreCase);
        _stopWords = new(provider.LoadStopWords(), StringComparer.OrdinalIgnoreCase);
        _abbreviations = new(provider.LoadAbbreviations(), StringComparer.OrdinalIgnoreCase);
        _irregulars = new(provider.LoadIrregularWords(), StringComparer.OrdinalIgnoreCase);
        _locations = new(provider.LoadKnownLocations(), StringComparer.OrdinalIgnoreCase);
        _organizations = new(provider.LoadKnownOrganizations(), StringComparer.OrdinalIgnoreCase);
    }

    public bool IsStopWord(string word)
        => !string.IsNullOrEmpty(word) && _stopWords.Contains(word);

    public bool TryGetWord(string word, out WordEntry entry)
    {
        if (!string.IsNullOrEmpty(word) && _words.TryGetValue(word, out var found))
        {
            entry = found;
            return true;
        }

        entry = default!;
        return false;
    }

    public bool TryGetSynonym(string word, out string canonicalWord)
    {
        if (!string.IsNullOrEmpty(word) && _synonyms.TryGetValue(word, out var canonical))
        {
            canonicalWord = canonical;
            return true;
        }

        canonicalWord = string.Empty;
        return false;
    }

    public bool TryExpandContraction(string word, out string expanded)
    {
        if (!string.IsNullOrEmpty(word) && _contractions.TryGetValue(word, out var value))
        {
            expanded = value;
            return true;
        }

        expanded = string.Empty;
        return false;
    }

    public bool IsAbbreviation(string word)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        // Tolerate a trailing period (e.g. "Dr." -> "Dr")
        var trimmed = word.TrimEnd('.');
        return _abbreviations.Contains(trimmed);
    }

    public IReadOnlyCollection<string> GetIrregularForms(string lemma)
        => !string.IsNullOrEmpty(lemma) && _irregulars.TryGetValue(lemma, out var forms)
            ? forms
            : Array.Empty<string>();

    public bool IsKnownLocation(string word)
        => !string.IsNullOrEmpty(word) && _locations.Contains(word);

    public bool IsKnownOrganization(string word)
        => !string.IsNullOrEmpty(word) && _organizations.Contains(word.TrimEnd('.'));
}

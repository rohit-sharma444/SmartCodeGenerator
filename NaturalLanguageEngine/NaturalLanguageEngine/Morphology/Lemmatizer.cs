using NaturalLanguageEngine.Abstractions.Morphology;
using NaturalLanguageEngine.Knowledge.Repositories;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Morphology;

namespace NaturalLanguageEngine.Morphology;

/// <summary>
/// Resolves a token to its dictionary lemma. Strategy:
/// 1. Direct lexicon hit, 2. irregular-form reverse lookup,
/// 3. fall back to the stemmer.
/// </summary>
public sealed class Lemmatizer : ILemmatizer
{
    private readonly ILanguageRepository _language;
    private readonly IStemmer _stemmer;

    // Lazily-built reverse map: inflected form -> lemma.
    // Lazy<T> (default mode = ExecutionAndPublication) guarantees the map is
    // built exactly once even when GetLemma is called concurrently from
    // multiple threads, which the previous "build on first access, cache in
    // a plain field" approach did not.
    private readonly Lazy<Dictionary<string, string>> _irregularReverse;

    public Lemmatizer(ILanguageRepository language, IStemmer stemmer)
    {
        _language = language;
        _stemmer = stemmer;
        _irregularReverse = new Lazy<Dictionary<string, string>>(BuildIrregularReverse);
    }

    public Lemma GetLemma(Token token)
    {
        var original = token.Text ?? string.Empty;
        var word = original.ToLowerInvariant();

        if (word.Length == 0)
            return new Lemma { Root = string.Empty, Original = original };

        // 1. Direct dictionary entry carries its own lemma - but only trust
        // it when the lemma actually differs from the surface form. The
        // bundled EnglishLanguageProvider currently sets Lemma == Word for
        // every entry (including inflected forms like "went" or "is"), so
        // treating any non-empty Lemma as authoritative would silently
        // short-circuit the irregular-form lookup below and return "went"
        // instead of "go", "is" instead of "be", etc. This condition keeps
        // that lookup meaningful today, and will transparently start using
        // a richer provider's real lemma annotations if one is supplied.
        if (_language.TryGetWord(word, out var entry) &&
            !string.IsNullOrEmpty(entry.Lemma) &&
            !string.Equals(entry.Lemma, word, StringComparison.OrdinalIgnoreCase))
        {
            return new Lemma { Root = entry.Lemma.ToLowerInvariant(), Original = original };
        }

        // 2. Known irregular inflection?
        if (_irregularReverse.Value.TryGetValue(word, out var lemma))
            return new Lemma { Root = lemma, Original = original };

        // 3. Stemmer fallback.
        return new Lemma { Root = _stemmer.Stem(word), Original = original };
    }

    private Dictionary<string, string> BuildIrregularReverse()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[]
        {
            "be", "go", "have", "do", "make", "take", "see", "come",
            "give", "buy", "find", "run", "child", "person", "mouse", "foot"
        })
        {
            foreach (var form in _language.GetIrregularForms(root))
                map[form] = root;
        }

        return map;
    }
}

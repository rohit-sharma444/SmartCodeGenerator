using NaturalLanguageEngine.Abstractions.Morphology;

namespace NaturalLanguageEngine.Morphology;

/// <summary>
/// A lightweight suffix-stripping stemmer (a small Porter-style subset).
/// Not linguistically exhaustive, but deterministic and dependency-free.
/// </summary>
public sealed class Stemmer : IStemmer
{
    public string Stem(string word)
    {
        if (string.IsNullOrEmpty(word))
            return string.Empty;

        word = word.ToLowerInvariant();

        if (word.Length <= 3)
            return word;

        // Plural / 3rd-person singular
        if (word.EndsWith("ies") && word.Length > 4)
            return word[..^3] + "y";

        if (word.EndsWith("sses"))
            return word[..^2];

        if (word.EndsWith("es") && word.Length > 4)
            return word[..^2];

        if (word.EndsWith("s") && !word.EndsWith("ss"))
            return word[..^1];

        // Past tense / progressive
        if (word.EndsWith("ing") && word.Length > 5)
            return DropDoubledConsonant(word[..^3]);

        if (word.EndsWith("ed") && word.Length > 4)
            return DropDoubledConsonant(word[..^2]);

        // Adverbs
        if (word.EndsWith("ly") && word.Length > 4)
            return word[..^2];

        return word;
    }

    private static string DropDoubledConsonant(string stem)
    {
        if (stem.Length >= 2 &&
            stem[^1] == stem[^2] &&
            !IsVowel(stem[^1]))
        {
            return stem[..^1];
        }

        return stem;
    }

    private static bool IsVowel(char c)
        => c is 'a' or 'e' or 'i' or 'o' or 'u';
}

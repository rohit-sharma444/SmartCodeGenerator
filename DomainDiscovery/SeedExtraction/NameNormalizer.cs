namespace DomainDiscovery.SeedExtraction;

/// <summary>Turns a raw entity phrase into a PascalCase C# identifier stub.</summary>
public static class NameNormalizer
{
    public static string ToEntityName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Take the head noun (last word) of a phrase like "library member".
        var word = text.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? string.Empty;

        // Keep only letters/digits.
        var cleaned = new string(word.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length == 0)
            return string.Empty;

        // Prose mentions "employee" and "employees" as one concept, so fold them onto
        // one name — otherwise both become separate entities, and separate classes.
        var singular = Singularize(cleaned.ToLowerInvariant());

        return char.ToUpperInvariant(singular[0]) + singular[1..];
    }

    /// <summary>
    /// A deliberately small English plural-stripper for the lowercase <paramref name="word"/>.
    /// It errs towards leaving a word alone: a missed plural costs a duplicate entity, while
    /// a wrongly stripped one ("address" -> "addres") corrupts a class name.
    /// </summary>
    private static string Singularize(string word)
    {
        // Too short to carry a plural suffix meaningfully ("tax" vs "as", "gas", "bus").
        if (word.Length <= 3)
            return word;

        // Not plurals at all: address, business, gross / status, campus / analysis, basis.
        if (word.EndsWith("ss", StringComparison.Ordinal)
            || word.EndsWith("us", StringComparison.Ordinal)
            || word.EndsWith("is", StringComparison.Ordinal))
            return word;

        // companies -> company, policies -> policy
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
            return string.Concat(word.AsSpan(0, word.Length - 3), "y");

        // taxes -> tax, boxes -> box, matches -> match, businesses -> business
        foreach (var suffix in EsSuffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal))
                return word[..^2];
        }

        // employees -> employee, accounts -> account
        if (word.EndsWith("s", StringComparison.Ordinal))
            return word[..^1];

        return word;
    }

    private static readonly string[] EsSuffixes = { "sses", "shes", "ches", "xes", "zes" };
}

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

        return char.ToUpperInvariant(cleaned[0]) + cleaned[1..].ToLowerInvariant();
    }
}

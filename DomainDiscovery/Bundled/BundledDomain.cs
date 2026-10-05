using System.Text.Json;
using System.Text.Json.Serialization;
using DomainDiscovery.Llm;

namespace DomainDiscovery.Bundled;

/// <summary>
/// One built-in domain: its name, its weighted match terms, the words that name the system itself,
/// and its classes.
/// </summary>
/// <remarks>
/// The classes are in the JSON shape a language model returns, so they go through the same
/// <see cref="LlmModelNormalizer"/> — which already fixes names, types and keys and settles links.
/// <para>
/// <see cref="Terms"/> is keyed by stem; a phrase term ("point of sale") by its words' stems joined
/// with single spaces. <see cref="Self"/> holds stems of single words.
/// </para>
/// </remarks>
public sealed record BundledDomain(
    string Name,
    IReadOnlyDictionary<string, int> Terms,
    IReadOnlyList<string> Self,
    LlmDomainModel Model)
{
    /// <summary>
    /// Whole words, lowercased, that score on their own spelling rather than their stem: "banking"
    /// names the domain where "bank" ("a food bank") does not, yet the stemmer makes them one.
    /// </summary>
    public IReadOnlyDictionary<string, int> Words { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    private sealed class DomainFile
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("self")] public List<string>? Self { get; set; }
        [JsonPropertyName("terms")] public Dictionary<string, int> Terms { get; set; } = new();
        [JsonPropertyName("words")] public Dictionary<string, int>? Words { get; set; }
        [JsonPropertyName("classes")] public List<LlmClass> Classes { get; set; } = new();
    }

    /// <summary>The key a term, or a run of description words, is looked up by.</summary>
    public static string KeyOf(string text) => string.Join(' ', WordStem.Words(text).Select(w => w.Stem));

    public static BundledDomain Parse(string json)
    {
        DomainFile file;
        try
        {
            file = JsonSerializer.Deserialize<DomainFile>(json) ?? throw new FormatException("empty domain file");
        }
        catch (JsonException ex)
        {
            throw new FormatException("not a domain file: " + ex.Message, ex);
        }

        if (string.IsNullOrWhiteSpace(file.Name))
            throw new FormatException("a domain file has no name");

        var terms = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (term, weight) in file.Terms)
        {
            // Words of letters, separated by spaces or hyphens: "point of sale", "e-commerce".
            if (string.IsNullOrWhiteSpace(term) || term.Any(c => !char.IsLetter(c) && c != ' ' && c != '-'))
                throw new FormatException($"{file.Name}: term \"{term}\" must be words of letters");
            if (weight is < 1 or > 3)
                throw new FormatException($"{file.Name}: term \"{term}\" has weight {weight}; use 1, 2 or 3");

            // Two words with one stem (purchasing/purchase) are one term to the matcher. Merging
            // their weights promoted the weaker word — "customers purchase products" picked
            // Purchasing — so a file that gives them different weights is refused instead.
            var key = KeyOf(term);
            if (terms.TryGetValue(key, out var existing) && existing != weight)
                throw new FormatException(
                    $"{file.Name}: term \"{term}\" shares the stem \"{key}\" with another term at a different weight");
            terms[key] = weight;
        }

        // The words that name the system rather than a class in it: a seed class called one of
        // them is dropped. Only these — a weight-3 term such as "landlord" names a real class.
        var self = (file.Self ?? [file.Name]).SelectMany(s => WordStem.Words(s)).Select(w => w.Stem).Distinct().ToList();

        var words = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (word, weight) in file.Words ?? [])
        {
            if (string.IsNullOrWhiteSpace(word) || word.Any(c => !char.IsLetter(c)))
                throw new FormatException($"{file.Name}: word \"{word}\" must be one word of letters");
            if (weight is < 1 or > 3)
                throw new FormatException($"{file.Name}: word \"{word}\" has weight {weight}; use 1, 2 or 3");
            words[word.ToLowerInvariant()] = weight;
        }

        return new BundledDomain(file.Name.Trim(), terms, self, new LlmDomainModel { Classes = file.Classes })
        {
            Words = words,
        };
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using DomainDiscovery.Llm;

namespace DomainDiscovery.Bundled;

/// <summary>One built-in domain: its name, its weighted match terms (as stems) and its classes.</summary>
/// <remarks>
/// The classes are in the JSON shape a language model returns, so they go through the same
/// <see cref="LlmModelNormalizer"/> — which already fixes names, types and keys and settles links.
/// </remarks>
public sealed record BundledDomain(string Name, IReadOnlyDictionary<string, int> Terms, LlmDomainModel Model)
{
    private sealed class DomainFile
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("terms")] public Dictionary<string, int> Terms { get; set; } = new();
        [JsonPropertyName("classes")] public List<LlmClass> Classes { get; set; } = new();
    }

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
            if (term.Length == 0 || term.Any(c => !char.IsLetter(c)))
                throw new FormatException($"{file.Name}: term \"{term}\" must be one word of letters");
            if (weight is < 1 or > 3)
                throw new FormatException($"{file.Name}: term \"{term}\" has weight {weight}; use 1, 2 or 3");

            // Two words can share a stem (purchasing/purchase): the stronger weight stands, or a
            // later supporting word would quietly demote the word that names the domain.
            var stem = WordStem.Of(term);
            terms[stem] = terms.TryGetValue(stem, out var existing) ? Math.Max(existing, weight) : weight;
        }

        return new BundledDomain(file.Name.Trim(), terms, new LlmDomainModel { Classes = file.Classes });
    }
}

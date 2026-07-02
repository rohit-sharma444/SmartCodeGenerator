namespace NaturalLanguageEngine.Knowledge.Models;

public sealed class SynonymSet
{
    public string Canonical { get; init; } = string.Empty;

    public IReadOnlyCollection<string> Words { get; init; }
        = Array.Empty<string>();
}
namespace NaturalLanguageEngine.Knowledge.Models;

public sealed class LanguageMetadata
{
    public string Name { get; init; } = string.Empty;

    public string Culture { get; init; } = string.Empty;

    public Version Version { get; init; } = new(1, 0);
}
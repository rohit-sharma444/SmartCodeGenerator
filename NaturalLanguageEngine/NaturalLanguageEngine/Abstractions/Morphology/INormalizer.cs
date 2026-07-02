namespace NaturalLanguageEngine.Abstractions.Morphology;

public interface INormalizer
{
    string Normalize(string text);
}
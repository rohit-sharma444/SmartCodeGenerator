namespace NaturalLanguageEngine.Abstractions.Morphology;

public interface IStemmer
{
    string Stem(string word);
}
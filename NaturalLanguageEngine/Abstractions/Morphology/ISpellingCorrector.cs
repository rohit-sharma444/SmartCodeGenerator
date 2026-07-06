namespace NaturalLanguageEngine.Abstractions.Morphology;

public interface ISpellingCorrector
{
    string Correct(string word);
}
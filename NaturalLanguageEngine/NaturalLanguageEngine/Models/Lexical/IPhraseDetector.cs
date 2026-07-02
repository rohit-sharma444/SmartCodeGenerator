namespace NaturalLanguageEngine.Abstractions.Syntax;

using NaturalLanguageEngine.Models.Lexical;

public interface IPhraseDetector
{
    void Detect(Sentence sentence);
}
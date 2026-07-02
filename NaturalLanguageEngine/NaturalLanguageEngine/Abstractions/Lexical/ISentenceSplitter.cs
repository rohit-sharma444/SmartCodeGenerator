using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Abstractions.Lexical;

public interface ISentenceSplitter
{
    IReadOnlyList<Sentence> Split(string text);
}
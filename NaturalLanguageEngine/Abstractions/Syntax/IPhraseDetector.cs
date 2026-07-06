using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Abstractions.Syntax;

public interface IPhraseDetector
{
    IReadOnlyList<Phrase> Detect(
        IReadOnlyList<Token> tokens,
        IReadOnlyDictionary<Token, PartOfSpeech> tags);
}
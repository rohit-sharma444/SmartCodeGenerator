using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Abstractions.Syntax;

public interface IPosTagger
{
    IReadOnlyDictionary<Token, PartOfSpeech> Tag(
        IReadOnlyList<Token> tokens);
}
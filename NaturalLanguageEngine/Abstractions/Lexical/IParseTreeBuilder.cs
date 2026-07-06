using NaturalLanguageEngine.Models.Common;
using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Abstractions.Syntax;

public interface IParseTreeBuilder
{
    ParseTree Build(Sentence sentence);
}
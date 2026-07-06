using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Abstractions.Lexical;

public interface ITokenizer
{
    IReadOnlyList<Token> Tokenize(string sentence);
}
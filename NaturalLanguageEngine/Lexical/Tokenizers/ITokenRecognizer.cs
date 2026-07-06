using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Lexical.Tokenizers;

public interface ITokenRecognizer
{
    int Priority { get; }

    bool CanRead(ICharacterReader reader);

    Token Read(ICharacterReader reader);
}
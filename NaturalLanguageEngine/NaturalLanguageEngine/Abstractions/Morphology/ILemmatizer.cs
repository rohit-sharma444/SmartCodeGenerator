using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Morphology;

namespace NaturalLanguageEngine.Abstractions.Morphology;

public interface ILemmatizer
{
    Lemma GetLemma(Token token);
}
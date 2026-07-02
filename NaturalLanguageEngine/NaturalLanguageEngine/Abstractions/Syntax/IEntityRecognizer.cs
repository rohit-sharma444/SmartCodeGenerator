using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Abstractions.Semantics;

public interface IEntityRecognizer
{
    IReadOnlyList<Entity> Recognize(
        IReadOnlyList<Phrase> phrases);
}
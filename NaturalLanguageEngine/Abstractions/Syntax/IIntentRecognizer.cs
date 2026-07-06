using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Abstractions.Semantics;

public interface IIntentRecognizer
{
    Intent? Recognize(
        IReadOnlyList<Entity> entities);
}
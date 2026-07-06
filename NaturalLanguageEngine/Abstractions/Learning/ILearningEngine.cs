using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Abstractions.Learning;

public interface ILearningEngine
{
    void Learn(SemanticResult result);

    void Correct(
        SemanticResult expected,
        SemanticResult actual);
}
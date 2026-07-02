using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Abstractions.Probability;

public interface IProbabilityScorer
{
    double Score(SemanticResult result);
}
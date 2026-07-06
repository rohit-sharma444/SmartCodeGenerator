using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Abstractions.Semantics;

public interface ISemanticAnalyzer
{
    SemanticResult Analyze(string text);
}
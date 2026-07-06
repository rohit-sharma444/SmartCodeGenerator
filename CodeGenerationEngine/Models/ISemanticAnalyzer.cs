using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Semantics;

public interface ISemanticAnalyzer
{
    SemanticModel Analyze(string input);
}
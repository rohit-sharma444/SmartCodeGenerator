using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class SemanticPipeline
{
    public static SemanticModel Process(string input)
    {
        var semantic = SemanticAnalyzer.Analyze(input);

        semantic.Intent = IntentAnalyzer.Detect(semantic);

        return semantic;
    }
}
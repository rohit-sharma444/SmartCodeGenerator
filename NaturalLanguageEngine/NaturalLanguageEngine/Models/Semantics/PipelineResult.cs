using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Core;

public sealed class PipelineResult
{
    public AnalysisContext Context { get; }

    public SemanticResult Result { get; }

    public PipelineResult(
        AnalysisContext context,
        SemanticResult result)
    {
        Context = context;
        Result = result;
    }
}
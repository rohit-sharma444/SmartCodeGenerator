using NaturalLanguageEngine.Abstractions.Pipeline;

namespace NaturalLanguageEngine.Core;

public abstract class PipelineStageBase : IPipelineStage
{
    public abstract string Name { get; }

    public virtual int Order => 0;

    public abstract AnalysisContext Execute(AnalysisContext context);

    public override string ToString()
        => Name;
}
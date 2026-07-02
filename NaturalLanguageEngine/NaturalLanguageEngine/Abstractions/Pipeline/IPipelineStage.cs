namespace NaturalLanguageEngine.Abstractions.Pipeline;

using NaturalLanguageEngine.Core;

public interface IPipelineStage
{
    string Name { get; }

    int Order { get; }

    AnalysisContext Execute(
        AnalysisContext context);
}
namespace NaturalLanguageEngine.Core;

public sealed class PipelineBuilder
{
    private readonly Pipeline _pipeline = new();

    public PipelineBuilder Add(PipelineStageBase stage)
    {
        _pipeline.Add(stage);
        return this;
    }

    public Pipeline Build()
        => _pipeline;
}
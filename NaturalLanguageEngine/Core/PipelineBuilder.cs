namespace NaturalLanguageEngine.Core;

/// <summary>
/// Fluent builder for constructing a Pipeline with optional features.
/// </summary>
public sealed class PipelineBuilder
{
    private readonly Pipeline _pipeline;

    /// <summary>
    /// Creates a new pipeline builder without logging.
    /// </summary>
    public PipelineBuilder()
        : this(null)
    {
    }

    /// <summary>
    /// Creates a new pipeline builder with optional logging.
    /// </summary>
    public PipelineBuilder(PipelineLogger? logger)
    {
        _pipeline = new Pipeline(logger);
    }

    /// <summary>
    /// Adds a stage to the pipeline.
    /// </summary>
    public PipelineBuilder Add(PipelineStageBase stage)
    {
        if (stage == null)
            throw new ArgumentNullException(nameof(stage));

        _pipeline.Add(stage);
        return this;
    }

    /// <summary>
    /// Builds and returns the configured pipeline.
    /// </summary>
    public Pipeline Build()
        => _pipeline;
}
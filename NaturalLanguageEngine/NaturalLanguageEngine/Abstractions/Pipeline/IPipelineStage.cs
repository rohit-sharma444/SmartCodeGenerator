namespace NaturalLanguageEngine.Abstractions.Pipeline;

using NaturalLanguageEngine.Core;

/// <summary>
/// Defines a stage in the analysis pipeline.
/// </summary>
public interface IPipelineStage
{
    /// <summary>Gets a human-readable name for this stage.</summary>
    string Name { get; }

    /// <summary>Gets the execution order (lower values execute first).</summary>
    int Order { get; }

    /// <summary>
    /// Executes this stage synchronously.
    /// </summary>
    AnalysisContext Execute(AnalysisContext context);

    /// <summary>
    /// Executes this stage asynchronously with cancellation support.
    /// Implementers should override this for I/O-bound operations.
    /// </summary>
    Task<AnalysisContext> ExecuteAsync(
        AnalysisContext context,
        CancellationToken cancellationToken = default)
    {
        // Default: call synchronous version
        return Task.FromResult(Execute(context));
    }
}
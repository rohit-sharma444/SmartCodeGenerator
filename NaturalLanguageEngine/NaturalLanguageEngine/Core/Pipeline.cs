using System.Diagnostics;
using NaturalLanguageEngine.Core.Diagnostics;
using NaturalLanguageEngine.Models.Diagnostics;

namespace NaturalLanguageEngine.Core;

/// <summary>
/// Executes the analysis pipeline with optional performance tracking.
/// </summary>
public sealed class Pipeline
{
    private readonly List<PipelineStageBase> _stages = new();
    private readonly PipelineLogger? _logger;

    /// <summary>
    /// Creates a pipeline without logging.
    /// </summary>
    public Pipeline()
        : this(null)
    {
    }

    /// <summary>
    /// Creates a pipeline with optional performance logging.
    /// </summary>
    public Pipeline(PipelineLogger? logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds a stage to the pipeline. Stages are automatically sorted by Order.
    /// </summary>
    public Pipeline Add(PipelineStageBase stage)
    {
        if (stage == null)
            throw new ArgumentNullException(nameof(stage));

        _stages.Add(stage);
        _stages.Sort((a, b) => a.Order.CompareTo(b.Order));

        return this;
    }

    /// <summary>
    /// Gets the logger if one was configured.
    /// </summary>
    public PipelineLogger? Logger => _logger;

    /// <summary>
    /// Runs every stage in order with optional performance tracking.
    /// If a stage throws, the exception is captured as a Critical Diagnostic
    /// on the context and the pipeline stops. Earlier results are preserved.
    /// </summary>
    public AnalysisContext Execute(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Input text cannot be null or empty.", nameof(text));

        var context = new AnalysisContext(text);
        var pipelineStopwatch = Stopwatch.StartNew();

        foreach (var stage in _stages)
        {
            var stageStopwatch = Stopwatch.StartNew();

            try
            {
                context = stage.Execute(context)
                    ?? throw new InvalidOperationException(
                        $"Stage '{stage.Name}' returned a null context.");

                stageStopwatch.Stop();

                _logger?.LogStageExecution(
                    stage.Name,
                    stage.Order,
                    stageStopwatch.Elapsed,
                    context.Entities.Count,
                    context.Relationships.Count,
                    success: true);
            }
            catch (Exception ex)
            {
                stageStopwatch.Stop();

                context.AddDiagnostic(
                    code: $"NLE.Stage.{stage.Name}",
                    severity: DiagnosticSeverity.Critical,
                    message: $"Stage '{stage.Name}' failed: {ex.Message}");

                _logger?.LogStageExecution(
                    stage.Name,
                    stage.Order,
                    stageStopwatch.Elapsed,
                    context.Entities.Count,
                    context.Relationships.Count,
                    success: false,
                    errorMessage: ex.Message);

                break;
            }
        }

        pipelineStopwatch.Stop();
        context.Properties["PipelineTotalElapsedMs"] = pipelineStopwatch.ElapsedMilliseconds;

        return context;
    }
}
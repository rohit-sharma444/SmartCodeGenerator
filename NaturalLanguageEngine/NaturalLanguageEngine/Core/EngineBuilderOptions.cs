namespace NaturalLanguageEngine.Core;

/// <summary>
/// Configuration options for building the NaturalLanguageEngine.
/// Allows customization of features like logging and caching.
/// </summary>
public sealed class EngineBuilderOptions
{
    /// <summary>
    /// Enable performance logging via PipelineLogger.
    /// When enabled, per-stage execution metrics are captured.
    /// Default: false.
    /// </summary>
    public bool EnableLogging { get; set; } = false;

    /// <summary>
    /// Enable result caching via CachedSemanticAnalyzer.
    /// When enabled, analysis results are memoized by input text.
    /// Default: false.
    /// </summary>
    public bool EnableCaching { get; set; } = false;

    /// <summary>
    /// Maximum number of cached results if caching is enabled.
    /// When the cache reaches this size, the oldest entry is evicted.
    /// Default: 100.
    /// </summary>
    public int MaxCacheSize { get; set; } = 100;

    /// <summary>
    /// Stop pipeline execution on the first critical error.
    /// When true, exceptions stop the pipeline and results are partial.
    /// When false, pipeline continues (not recommended).
    /// Default: true.
    /// </summary>
    public bool StopOnCriticalError { get; set; } = true;

    /// <summary>
    /// Include diagnostic information in analysis results.
    /// When enabled, warnings and errors are reported in SemanticResult.Diagnostics.
    /// Default: true.
    /// </summary>
    public bool IncludeDiagnostics { get; set; } = true;
}
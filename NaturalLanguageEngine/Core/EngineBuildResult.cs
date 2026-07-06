using NaturalLanguageEngine.Abstractions.Semantics;

namespace NaturalLanguageEngine.Core;

/// <summary>
/// Result of building the NaturalLanguageEngine.
/// Provides access to the semantic analyzer and optional performance logger.
/// </summary>
public sealed class EngineBuildResult
{
    /// <summary>
    /// Gets the semantic analyzer (either raw engine or cached wrapper).
    /// </summary>
    public required ISemanticAnalyzer Analyzer { get; init; }

    /// <summary>
    /// Gets the pipeline logger if logging was enabled; otherwise null.
    /// </summary>
    public PipelineLogger? Logger { get; init; }
}
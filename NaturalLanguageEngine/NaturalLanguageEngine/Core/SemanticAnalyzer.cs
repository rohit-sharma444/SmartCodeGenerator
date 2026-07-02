using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Core;

/// <summary>
/// High-level semantic analyzer that provides a unified interface for
/// analyzing natural language text and extracting semantic information
/// (entities, relationships, and intent).
/// </summary>
public sealed class SemanticAnalyzer : ISemanticAnalyzer
{
    private readonly NaturalLanguageEngine _engine;

    /// <summary>
    /// Creates a semantic analyzer with the default fully-configured
    /// NaturalLanguageEngine pipeline.
    /// </summary>
    public SemanticAnalyzer()
        : this(EngineBuilder.BuildDefault())
    {
    }

    /// <summary>
    /// Creates a semantic analyzer with a custom engine instance.
    /// </summary>
    /// <param name="engine">The NaturalLanguageEngine to use for analysis.</param>
    /// <exception cref="ArgumentNullException">Thrown when engine is null.</exception>
    public SemanticAnalyzer(NaturalLanguageEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Analyzes the given text and returns a comprehensive semantic result
    /// containing entities, relationships, intent, confidence score, and diagnostics.
    /// </summary>
    /// <param name="text">The natural language text to analyze.</param>
    /// <returns>A SemanticResult containing extracted semantic information.</returns>
    /// <exception cref="ArgumentException">Thrown when text is null, empty, or whitespace.</exception>
    public SemanticResult Analyze(string text)
    {
        return _engine.Analyze(text);
    }
}
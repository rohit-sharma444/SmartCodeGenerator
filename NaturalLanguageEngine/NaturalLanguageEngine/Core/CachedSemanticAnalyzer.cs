using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Core;

/// <summary>
/// Wraps an ISemanticAnalyzer with result memoization.
/// Caches analysis results by input text (case-insensitive).
/// Useful for batch processing and repeated analyses.
/// </summary>
public sealed class CachedSemanticAnalyzer : ISemanticAnalyzer
{
    private readonly ISemanticAnalyzer _innerAnalyzer;
    private readonly Dictionary<string, SemanticResult> _cache;
    private readonly int _maxCacheSize;
    private readonly object _lockObj = new();

    /// <summary>
    /// Creates a cached analyzer with the specified inner analyzer.
    /// </summary>
    /// <param name="innerAnalyzer">The analyzer to wrap and cache results for.</param>
    /// <param name="maxCacheSize">Maximum number of cached results. Default: 100.</param>
    /// <exception cref="ArgumentNullException">Thrown when innerAnalyzer is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxCacheSize is less than 1.</exception>
    public CachedSemanticAnalyzer(
        ISemanticAnalyzer innerAnalyzer,
        int maxCacheSize = 100)
    {
        if (maxCacheSize < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCacheSize), "Must be at least 1.");

        _innerAnalyzer = innerAnalyzer ?? throw new ArgumentNullException(nameof(innerAnalyzer));
        _maxCacheSize = maxCacheSize;
        _cache = new Dictionary<string, SemanticResult>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Analyzes the given text, returning a cached result if available.
    /// </summary>
    public SemanticResult Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Input text cannot be null or empty.", nameof(text));

        lock (_lockObj)
        {
            if (_cache.TryGetValue(text, out var cached))
                return cached;

            var result = _innerAnalyzer.Analyze(text);

            // Simple eviction: remove first entry when cache is full
            if (_cache.Count >= _maxCacheSize)
            {
                var firstKey = _cache.Keys.First();
                _cache.Remove(firstKey);
            }

            _cache[text] = result;
            return result;
        }
    }

    /// <summary>
    /// Clears all cached results to reclaim memory.
    /// </summary>
    public void ClearCache()
    {
        lock (_lockObj)
        {
            _cache.Clear();
        }
    }

    /// <summary>
    /// Gets the current cache size and capacity.
    /// </summary>
    public (int CachedItems, int MaxCapacity) GetCacheStats()
    {
        lock (_lockObj)
        {
            return (_cache.Count, _maxCacheSize);
        }
    }

    /// <summary>
    /// Gets the hit rate as a percentage.
    /// </summary>
    public double GetHitRate()
    {
        lock (_lockObj)
        {
            return _cache.Count > 0 ? (_cache.Count / (double)_maxCacheSize) * 100 : 0;
        }
    }
}
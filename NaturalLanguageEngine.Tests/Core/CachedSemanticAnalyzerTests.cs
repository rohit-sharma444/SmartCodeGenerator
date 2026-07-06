using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Core;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace NaturalLanguageEngine.Tests.Core;

/// <summary>
/// Tests for CachedSemanticAnalyzer memoization and cache eviction.
/// </summary>
public class CachedSemanticAnalyzerTests
{
    private class MockSemanticAnalyzer : ISemanticAnalyzer
    {
        private int _callCount = 0;

        public int CallCount => _callCount;

        public SemanticResult Analyze(string text)
        {
            _callCount++;
            return new SemanticResult
            {
                Intent = new IntentDefinition { Name = "TestIntent" },
                Confidence = (double)0.95m,
                Entities = new List<Entity>
                {
                    new Entity { Text = text, Type = EntityType.Value, Confidence = 0.9m }
                },
                Relationships = new List<Relationship>(),
                Diagnostics = new List<Diagnostic>()
            };
        }
    }

    [Fact]
    public void Analyze_FirstCall_InvokesInnerAnalyzer()
    {
        var mockAnalyzer = new MockSemanticAnalyzer();
        var cached = new CachedSemanticAnalyzer(mockAnalyzer, maxCacheSize: 10);

        var result = cached.Analyze("test input");

        Assert.Equal(1, mockAnalyzer.CallCount);
        Assert.NotNull(result);
    }

    [Fact]
    public void Analyze_SameInputTwice_CachesResult_InvokerOnce()
    {
        var mockAnalyzer = new MockSemanticAnalyzer();
        var cached = new CachedSemanticAnalyzer(mockAnalyzer, maxCacheSize: 10);

        var result1 = cached.Analyze("test input");
        var result2 = cached.Analyze("test input");

        Assert.Equal(1, mockAnalyzer.CallCount);
        Assert.Equal(result1.Confidence, result2.Confidence);
    }

    [Fact]
    public void Analyze_DifferentInputs_InvokerMultipleTimes()
    {
        var mockAnalyzer = new MockSemanticAnalyzer();
        var cached = new CachedSemanticAnalyzer(mockAnalyzer, maxCacheSize: 10);

        cached.Analyze("input1");
        cached.Analyze("input2");
        cached.Analyze("input3");

        Assert.Equal(3, mockAnalyzer.CallCount);
    }

    [Fact]
    public void Analyze_CacheExceededSize_EvictsOldestEntry()
    {
        var mockAnalyzer = new MockSemanticAnalyzer();
        var cached = new CachedSemanticAnalyzer(mockAnalyzer, maxCacheSize: 2);

        cached.Analyze("input1");
        cached.Analyze("input2");
        cached.Analyze("input3"); // Should evict input1

        // Access input1 again - should invoke analyzer again since it was evicted
        var result1 = cached.Analyze("input1");
        Assert.Equal(4, mockAnalyzer.CallCount);
    }

    [Fact]
    public void Analyze_NullInput_ThrowsArgumentException()
    {
        var mockAnalyzer = new MockSemanticAnalyzer();
        var cached = new CachedSemanticAnalyzer(mockAnalyzer, maxCacheSize: 10);

        Assert.Throws<ArgumentException>(() => cached.Analyze(null!));
    }

    [Fact]
    public void Analyze_EmptyStringInput_ThrowsArgumentException()
    {
        var mockAnalyzer = new MockSemanticAnalyzer();
        var cached = new CachedSemanticAnalyzer(mockAnalyzer, maxCacheSize: 10);

        Assert.Throws<ArgumentException>(() => cached.Analyze(""));
    }

    [Fact]
    public void ThreadSafety_ConcurrentAnalyze_ProducesConsistentResults()
    {
        var mockAnalyzer = new MockSemanticAnalyzer();
        var cached = new CachedSemanticAnalyzer(mockAnalyzer, maxCacheSize: 10);
        var results = new List<SemanticResult>();
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() =>
            {
                var result = cached.Analyze("shared input");
                lock (lockObj)
                {
                    results.Add(result);
                }
            }))
            .ToArray();

        Task.WaitAll(tasks);

        Assert.Equal(10, results.Count);
        Assert.All(results, r => Assert.Equal("TestIntent", r.Intent?.Name));
    }
}
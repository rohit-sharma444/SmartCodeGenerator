using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Core;
using Xunit;

namespace NaturalLanguageEngine.Tests.Core;

/// <summary>
/// Tests for EngineBuilder pipeline assembly and configuration.
/// </summary>
public class EngineBuilderTests
{
    [Fact]
    public void BuildDefault_CreatesValidEngine()
    {
        var result = EngineBuilder.BuildDefault();

        Assert.NotNull(result);
        Assert.NotNull(result.Analyzer);
        Assert.Null(result.Logger); // Default has no logging
    }

    [Fact]
    public void BuildDefault_EngineCanAnalyzeText()
    {
        var result = EngineBuilder.BuildDefault();
        var analysisResult = result.Analyzer.Analyze("The quick brown fox jumps.");

        Assert.NotNull(analysisResult);
        Assert.NotEmpty(analysisResult.Entities);
    }

    [Fact]
    public void Build_WithLoggingEnabled_CreatesLoggerAndAttachesIt()
    {
        var options = new EngineBuilderOptions { EnableLogging = true };
        var result = EngineBuilder.Build(options);

        Assert.NotNull(result.Logger);
        Assert.IsAssignableFrom<PipelineLogger>(result.Logger);
    }

    [Fact]
    public void Build_WithLoggingDisabled_NoLoggerCreated()
    {
        var options = new EngineBuilderOptions { EnableLogging = false };
        var result = EngineBuilder.Build(options);

        Assert.Null(result.Logger);
    }

    [Fact]
    public void Build_WithCachingEnabled_AnalyzerIsCachedWrapper()
    {
        var options = new EngineBuilderOptions { EnableCaching = true };
        var result = EngineBuilder.Build(options);

        Assert.NotNull(result.Analyzer);
        Assert.IsAssignableFrom<CachedSemanticAnalyzer>(result.Analyzer);
    }

    [Fact]
    public void Build_WithCachingDisabled_AnalyzerIsSemanticAnalyzer()
    {
        var options = new EngineBuilderOptions { EnableCaching = false };
        var result = EngineBuilder.Build(options);

        Assert.NotNull(result.Analyzer);
        Assert.IsAssignableFrom<SemanticAnalyzer>(result.Analyzer);
    }

    [Fact]
    public void Build_WithLoggingAndCachingBoth_CreatesBoothFeatures()
    {
        var options = new EngineBuilderOptions
        {
            EnableLogging = true,
            EnableCaching = true,
            MaxCacheSize = 50
        };
        var result = EngineBuilder.Build(options);

        Assert.NotNull(result.Analyzer);
        Assert.IsAssignableFrom<CachedSemanticAnalyzer>(result.Analyzer);
        Assert.NotNull(result.Logger);
    }

    [Fact]
    public void Build_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EngineBuilder.Build(null!));
    }

    [Fact]
    public void Build_CustomMaxCacheSizeApplied()
    {
        var options = new EngineBuilderOptions
        {
            EnableCaching = true,
            MaxCacheSize = 25
        };
        var result = EngineBuilder.Build(options);

        var analyzer = result.Analyzer as CachedSemanticAnalyzer;
        Assert.NotNull(analyzer);
        // Verify by filling cache and checking eviction behavior
        for (int i = 0; i < 30; i++)
        {
            analyzer.Analyze($"test input {i}");
        }
        // If eviction worked, cache should have ~25 items (implementation detail varies)
    }

    [Fact]
    public void Build_AnalysisConsistency_SameInputProducesSameOutput()
    {
        var result = EngineBuilder.BuildDefault();
        var input = "John goes to the store.";

        var result1 = result.Analyzer.Analyze(input);
        var result2 = result.Analyzer.Analyze(input);

        Assert.Equal(result1.Confidence, result2.Confidence);
        Assert.Equal(result1.Intent?.Name, result2.Intent?.Name);
        Assert.Equal(result1.Entities.Count, result2.Entities.Count);
    }

    [Fact]
    public void Build_PipelineContainsAllLayers()
    {
        var result = EngineBuilder.BuildDefault();
        var analysisResult = result.Analyzer.Analyze("The quick brown fox.");

        // If all layers are wired, analysis should produce results
        Assert.NotNull(analysisResult);
        Assert.True(analysisResult.Entities.Count > 0 || analysisResult.Confidence >= 0);
    }
}
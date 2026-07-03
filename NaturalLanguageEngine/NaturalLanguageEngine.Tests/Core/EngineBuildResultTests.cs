using NaturalLanguageEngine.Core;
using Xunit;

namespace NaturalLanguageEngine.Tests.Core;

/// <summary>
/// Tests for EngineBuildResult data structure.
/// </summary>
public class EngineBuildResultTests
{
    [Fact]
    public void Constructor_WithAnalyzerAndLogger_StoresValues()
    {
        var analyzer = EngineBuilder.BuildDefault().Analyzer;
        var logger = new PipelineLogger();

        var result = new EngineBuildResult
        {
            Analyzer = analyzer,
            Logger = logger
        };

        Assert.NotNull(result.Analyzer);
        Assert.NotNull(result.Logger);
    }

    [Fact]
    public void Constructor_WithAnalyzerAndNullLogger_StoresValues()
    {
        var analyzer = EngineBuilder.BuildDefault().Analyzer;

        var result = new EngineBuildResult
        {
            Analyzer = analyzer,
            Logger = null
        };

        Assert.NotNull(result.Analyzer);
        Assert.Null(result.Logger);
    }

    [Fact]
    public void Analyzer_CanAnalyzeText()
    {
        var buildResult = EngineBuilder.BuildDefault();

        var analysisResult = buildResult.Analyzer.Analyze("Test input");

        Assert.NotNull(analysisResult);
    }

    //[Fact]
    //public void Logger_CanLogStageExecution()
    //{
    //    var buildResult = EngineBuilder.Build(new EngineBuilderOptions { EnableLogging = true });

    //    buildResult.Logger?.LogStageExecution("TestStage", 10, 5.0, 1, 0, true, null);

    //    var logs = buildResult.Logger?.GetAllLogs();
    //    Assert.NotNull(logs);
    //    Assert.NotEmpty(logs);
    //}
}
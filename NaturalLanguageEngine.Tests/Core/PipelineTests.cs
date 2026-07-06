using NaturalLanguageEngine.Core;
using System.Text.Json;
using Xunit;

namespace NaturalLanguageEngine.Tests.Core;

/// <summary>
/// Tests for PipelineLogger performance monitoring and metrics collection.
/// </summary>
public class PipelineLoggerTests
{
    [Fact]
    public void LogStageExecution_RecordsMetricsForSingleStage()
    {
        var logger = new PipelineLogger();

        logger.LogStageExecution(
            stageName: "TestStage",
            order: 10,
            TimeSpan.FromSeconds(25.5),
            entityCount: 3,
            relationshipCount: 2,
            success: true,
            errorMessage: null);

        var logs = logger.GetAllLogs();
        Assert.Single(logs);
        Assert.Equal("TestStage", logs[0].StageName);
        Assert.Equal(10, logs[0].Order);
        Assert.Equal(25500L, logs[0].ElapsedMilliseconds);
        Assert.Equal(3, logs[0].EntityCount);
        Assert.Equal(2, logs[0].RelationshipCount);
        Assert.True(logs[0].Success);
    }

    [Fact]
    public void LogStageExecution_MultipleInvocations_AccumulatesLogs()
    {
        var logger = new PipelineLogger();

        for (int i = 0; i < 5; i++)
        {
            logger.LogStageExecution($"Stage{i}", i * 10, TimeSpan.FromSeconds(10.0 + i), i, i / 2, true, null);
        }

        var logs = logger.GetAllLogs();
        Assert.Equal(5, logs.Count);
    }

    [Fact]
    public void GetStats_ComputesCorrectAggregates()
    {
        var logger = new PipelineLogger();

        logger.LogStageExecution("Stage1", 10, TimeSpan.FromSeconds(20.0), 2, 1, true, null);
        logger.LogStageExecution("Stage2", 20, TimeSpan.FromSeconds(30.0), 3, 2, true, null);
        logger.LogStageExecution("Stage1", 10, TimeSpan.FromSeconds(20.0), 2, 1, true, null);

        var stats = logger.GetStats();
        Assert.Equal(3, stats.TotalExecutions);
        Assert.Equal(70000L, stats.TotalElapsedMilliseconds);
        Assert.Equal(70000.0 / 3, stats.AverageElapsedMilliseconds, precision: 2);
    }

    [Fact]
    public void ExportAsJson_ProducesValidJsonFormat()
    {
        var logger = new PipelineLogger();

        logger.LogStageExecution("TestStage", 10, TimeSpan.FromSeconds(15.5), 2, 1, true, null);

        var json = logger.ExportAsJson();
        Assert.NotEmpty(json);

        // Verify it's valid JSON
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public void LogStageExecution_WithError_RecordsErrorMessage()
    {
        var logger = new PipelineLogger();
        var errorMsg = "Test error occurred";

        logger.LogStageExecution("FailedStage", 10, TimeSpan.FromSeconds(5.0), 0, 0, false, errorMsg);

        var logs = logger.GetAllLogs();
        Assert.Single(logs);
        Assert.False(logs[0].Success);
        Assert.Equal(errorMsg, logs[0].ErrorMessage);
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentLogging_AllEntriesRecorded()
    {
        var logger = new PipelineLogger();
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            int stageId = i;
            tasks.Add(Task.Run(() =>
            {
                logger.LogStageExecution($"Stage{stageId}", stageId, TimeSpan.FromSeconds(10.0), 1, 0, true, null);
            }));
        }

        await Task.WhenAll(tasks);

        var logs = logger.GetAllLogs();
        Assert.Equal(10, logs.Count);
    }
}

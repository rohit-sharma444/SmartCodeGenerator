using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaturalLanguageEngine.Core;

/// <summary>
/// Captures structured execution metrics for each pipeline stage.
/// Useful for performance analysis and debugging.
/// </summary>
public sealed class PipelineLogger
{
    private readonly List<PipelineExecutionLog> _logs = new();
    private readonly object _lockObj = new();

    /// <summary>
    /// Logs the execution of a pipeline stage.
    /// </summary>
    public void LogStageExecution(
        string stageName,
        int order,
        TimeSpan elapsed,
        int entityCount,
        int relationshipCount,
        bool success,
        string? errorMessage = null)
    {
        if (stageName == null)
            throw new ArgumentNullException(nameof(stageName));

        var log = new PipelineExecutionLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            StageName = stageName,
            Order = order,
            ElapsedMilliseconds = (long)elapsed.TotalMilliseconds,
            EntityCount = entityCount,
            RelationshipCount = relationshipCount,
            Success = success,
            ErrorMessage = errorMessage
        };

        lock (_lockObj)
        {
            _logs.Add(log);
        }
    }

    /// <summary>
    /// Gets all captured logs in chronological order.
    /// </summary>
    public IReadOnlyList<PipelineExecutionLog> GetLogs()
    {
        lock (_lockObj)
        {
            return _logs.AsReadOnly();
        }
    }

    /// <summary>
    /// Gets statistics about the most recent execution.
    /// </summary>
    public PipelineExecutionStats GetStats()
    {
        lock (_lockObj)
        {
            if (_logs.Count == 0)
                return new PipelineExecutionStats();

            // Group by timestamp to find the most recent execution
            var lastExecution = _logs
                .GroupBy(l => l.Timestamp.Date)
                .OrderByDescending(g => g.Key)
                .FirstOrDefault()?
                .OrderByDescending(l => l.Timestamp)
                .TakeWhile((log, index) => index == 0 || log.Timestamp == _logs.Last().Timestamp)
                .ToList() ?? new List<PipelineExecutionLog>();

            if (lastExecution.Count == 0)
                return new PipelineExecutionStats();

            var totalMs = lastExecution.Sum(l => l.ElapsedMilliseconds);
            var successCount = lastExecution.Count(l => l.Success);
            var failureCount = lastExecution.Count(l => !l.Success);
            var totalEntities = lastExecution.Sum(l => l.EntityCount);
            var totalRelationships = lastExecution.Sum(l => l.RelationshipCount);

            var slowestStage = lastExecution
                .OrderByDescending(l => l.ElapsedMilliseconds)
                .FirstOrDefault();

            return new PipelineExecutionStats
            {
                TotalElapsedMilliseconds = totalMs,
                StageCount = lastExecution.Count,
                SuccessfulStages = successCount,
                FailedStages = failureCount,
                TotalEntitiesFound = totalEntities,
                TotalRelationshipsFound = totalRelationships,
                SlowestStageName = slowestStage?.StageName ?? string.Empty,
                SlowestStageMs = slowestStage?.ElapsedMilliseconds ?? 0
            };
        }
    }

    /// <summary>
    /// Exports logs as JSON for analysis and debugging.
    /// </summary>
    public string ExportAsJson()
    {
        lock (_lockObj)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(_logs, options);
        }
    }

    /// <summary>
    /// Clears all captured logs.
    /// </summary>
    public void Clear()
    {
        lock (_lockObj)
        {
            _logs.Clear();
        }
    }
}

/// <summary>
/// Represents a single stage execution event.
/// </summary>
public sealed class PipelineExecutionLog
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("stageName")]
    public string StageName { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("elapsedMilliseconds")]
    public long ElapsedMilliseconds { get; set; }

    [JsonPropertyName("entityCount")]
    public int EntityCount { get; set; }

    [JsonPropertyName("relationshipCount")]
    public int RelationshipCount { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Statistics from a pipeline execution.
/// </summary>
public sealed class PipelineExecutionStats
{
    public long TotalElapsedMilliseconds { get; set; }

    public int StageCount { get; set; }

    public int SuccessfulStages { get; set; }

    public int FailedStages { get; set; }

    public int TotalEntitiesFound { get; set; }

    public int TotalRelationshipsFound { get; set; }

    public string SlowestStageName { get; set; } = string.Empty;

    public long SlowestStageMs { get; set; }
}